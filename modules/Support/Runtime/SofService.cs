using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Host-owned SOF / JTAC domain: real camps per faction, the abstract teams (raise, route, exposure, pinned, missions), held buildings and the helicopter lift.
    /// It mirrors the CYBER service: a 1 Hz host tick, scene reset, one faction established per mission second, everything host-only; clients read the SOF state
    /// through the mirror. The class is split by concern: this file is the camps and the tick; <c>SofService.Effects</c> the mission effects and the reveal path;
    /// <c>.Lift</c> the helicopter lift, cover kills and the cover/lase posts; <c>.Mirror</c> the faction view and the host verbs.
    /// </summary>
    internal sealed partial class SofService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8, MaximumBases = 16, MaximumScanUnits = 4096;
        private const float RelocateRetrySeconds = 30f, RefreshSeconds = 10f, CampRadius = 6500f;

        private sealed class CampSlot
        {
            public int Index;
            public Unit Unit;
            public float Baseline;
            public GlobalPosition Spot;
            public Airbase Parent;
            public readonly RebuildBar Bar = new RebuildBar(CampRules.RebuildGoal);
            public float NextRebuildTry, LastFund;
            public CampSlot(int index, Unit unit, GlobalPosition spot, Airbase parent)
            { Index = index; Unit = unit; Spot = spot; Parent = parent; Baseline = UplinkSpawner.Health(unit); }
        }

        private sealed partial class FactionSof
        {
            public FactionHQ Owner;
            public int Key;
            public SofCampSet Camps;
            public readonly List<CampSlot> Slots = new List<CampSlot>(CampRules.MaxCamps);
            public SofDesk Desk;
            public readonly SofObservations Obs = new SofObservations();
            public readonly List<SofSeed> Seeds = new List<SofSeed>(64);
            public readonly HashSet<uint> Keep = new HashSet<uint>();
            public System.Random Rng;
            public float NextRefresh;
        }

        /// <summary>What the pure desk needs from the live game: the clock, the census, the wallet, the world scene and every effect.</summary>
        private sealed class Ports : ISofPorts
        {
            private readonly SofService service;
            private readonly FactionSof faction;
            public Ports(SofService service, FactionSof faction) { this.service = service; this.faction = faction; }
            public float Now => SupportManager.MissionNow();
            public int Humans => service.manager != null ? service.manager.HumanCount(faction.Owner) : 1;
            public int Owner => faction.Key;
            public bool Fob => faction.FobUntil > SupportManager.MissionNow();
            public SofOutcome TrySpend(ulong op, int cr, out int detail) => service.manager.SofSpend(faction.Owner, op, cr, out detail);
            public void Refund(ulong op, int cr) => service.manager.SofRefund(faction.Owner, op, cr);
            public SofScene Scene(float x, float z) => faction.Obs.Scene(x, z, service.Stared(faction.Owner, x, z));
            public bool CyberNear(float x, float z) => service.cyber != null && service.cyber.HoldsNodeNear(faction.Owner, x, z, SofRules.RingBoostMetres);
            public bool Roll(int chancePercent) => faction.Rng.Next(100) < chancePercent;
            public void Reveal(float x, float z, float radius, float seconds) => service.StartReveal(faction, x, z, radius, seconds);
            public bool LaseBegin(int slot, uint key, float x, float z) => service.Lase(faction, slot, key, true);
            public void LaseEnd(int slot, uint key) { service.Lase(faction, slot, key, false); }
            public bool TargetAlive(TargetKind kind, AnchorSub sub, uint key) => faction.Obs.Alive(kind, key);
            public bool Sabotage(AnchorSub sub, uint key) => service.Sabotage(faction, sub, key);
            public bool Tap(TargetKind kind, uint key, float seconds) => service.Tap(faction, key, seconds);
            public bool BuildingAlive(uint key) => faction.Obs.Alive(TargetKind.Building, key);
            public bool PostCover(int slot, float x, float z) => service.manager.PostSofPost(faction.Owner, SupportActionId.SofCover, slot, x, z, faction.Desk.Teams[slot].Raiser);
            public bool PostLase(int slot, float x, float z) => service.manager.PostSofPost(faction.Owner, SupportActionId.SofLase, slot, x, z, faction.Desk.Teams[slot].Raiser);
            public void Pay(ulong pilot, int cr) => service.manager.SofPay(faction.Owner, pilot, cr);
        }

        private readonly Dictionary<FactionHQ, FactionSof> factions = new Dictionary<FactionHQ, FactionSof>();
        private readonly Dictionary<FactionHQ, float> nextAttempt = new Dictionary<FactionHQ, float>();
        private readonly CyberAnchorSpawner spawner = new CyberAnchorSpawner();
        private readonly List<KeyValuePair<AnchorSub, Unit>> enemyAnchors = new List<KeyValuePair<AnchorSub, Unit>>(16);
        private SupportManager manager;
        private SpaceService space;
        private CyberService cyber;
        private float nextTick, nextCleanup, nextWarning;
        private bool loggedNoCampKeys;

        // Optional hooks: each is implemented by exactly one of the other files (the call is removed when it is not).
        partial void TickEffects(FactionSof f, float now);
        partial void TickLift(FactionSof f, float now);
        partial void RecordEvent(FactionSof f, SofEvent e);
        partial void ResetEffects();
        partial void TickFob(FactionSof f, float now);

        internal static SofService Active { get; private set; }
        internal CyberAnchorSpawner Spawner => spawner;

        public void Configure(SupportManager support, SpaceService spaceService, CyberService cyberService)
        {
            manager = support;
            space = spaceService;
            cyber = cyberService;
            Active = this;
        }

        public void ResetForScene()
        {
            foreach (var pair in factions) pair.Value.Desk.Reset();
            ResetEffects();
            factions.Clear();
            nextAttempt.Clear();
            nextTick = 0f;
            loggedNoCampKeys = false;
            spawner.ResetForScene();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            ResetForScene();
        }

        // ---- Reads ---------------------------------------------------------------------------------

        internal bool HasSof(FactionHQ owner) => owner != null && factions.ContainsKey(owner);

        internal bool TryDesk(FactionHQ owner, out SofDesk desk)
        {
            desk = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionSof f)) return false;
            desk = f.Desk;
            return true;
        }

        /// <summary>True when the mod's own camp units include this one (so the generic ground list never double counts it).</summary>
        internal bool IsCampUnit(Unit unit) => spawner.Owns(unit);

        /// <summary>Another faction's SPACE bird is looking at this point right now (the Overwatch stare: SPACE beats SOF).</summary>
        private bool Stared(FactionHQ owner, float x, float z)
        {
            if (space == null) return false;
            float now = SupportManager.MissionNow();
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return false;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null || hq == owner) continue;
                SpaceObservations obs = space.ObservationsFor(hq);
                if (obs != null && obs.Covers(x, z, now)) return true;
            }
            return false;
        }

        // ---- Host tick -------------------------------------------------------------------------------

        private void Update()
        {
            if (manager?.Settings == null || !manager.Settings.Enabled.Value || !manager.Settings.SofEnabled.Value)
            {
                if (factions.Count > 0) ResetForScene();
                if (Time.unscaledTime >= nextCleanup) { nextCleanup = Time.unscaledTime + 1f; spawner.RetryCleanup(); }
                return;
            }
            if (!GameAccess.IsServer() || (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)) return;
            float now = SupportManager.MissionNow();
            if (now < nextTick) return;
            nextTick = now + 1f;
            try { Tick(now); }
            catch (Exception e)
            {
                if (Time.unscaledTime >= nextWarning) { nextWarning = Time.unscaledTime + 10f; Plugin.Logger?.LogWarning("[Support.Sof] Tick failed: " + e.Message); }
            }
        }

        private void Tick(float now)
        {
            spawner.RetryCleanup();
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs == null) return;
            bool establishedOne = false;
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                if (factions.TryGetValue(hq, out FactionSof f)) { TickFaction(f, now); continue; }
                if (establishedOne || factions.Count >= MaximumFactions || (nextAttempt.TryGetValue(hq, out float retry) && now < retry)) continue;
                if (nextAttempt.Count >= MaximumFactions && !nextAttempt.ContainsKey(hq)) continue;
                nextAttempt[hq] = now + RelocateRetrySeconds;
                establishedOne = true; // initial world sampling is limited to one faction per mission second
                if (TryEstablish(hq, out f)) { factions.Add(hq, f); Plugin.Logger?.LogInfo("[Support.Sof] " + hq.name + ": " + f.Slots.Count + " native camp(s), " + CyberAnchorSpawner.DescribeCamp() + "."); }
                else LogNoCampKeys(hq);
            }
        }

        /// <summary>Once per mission: when no camp vehicle key resolves, say which keys were tried (a faction with no legal site just has no SOF, which is not logged here).</summary>
        private void LogNoCampKeys(FactionHQ hq)
        {
            if (loggedNoCampKeys) return;
            string described = CyberAnchorSpawner.DescribeCamp();
            if (!described.Contains("=none")) return;
            loggedNoCampKeys = true;
            Plugin.Logger?.LogWarning("[Support.Sof] " + hq.name + ": no camp vehicle key resolved (" + described + "; tried " + string.Join("/", CyberAnchorSpawner.CampKeys) + " and escorts " + string.Join("/", CyberAnchorSpawner.CampGuardKeys) + "): SOF stays offline.");
        }

        private void TickFaction(FactionSof f, float now)
        {
            foreach (CampSlot slot in f.Slots) Measure(f, slot, now);
            Rebuild(f, now);
            f.Obs.Snapshot(f.Owner, now);
            if (now >= f.NextRefresh) { f.NextRefresh = now + RefreshSeconds; RefreshTargets(f); }
            TickLift(f, now);
            f.Desk.Tick();
            TickEffects(f, now);
            TickFob(f, now);
        }

        private void RefreshTargets(FactionSof f)
        {
            enemyAnchors.Clear();
            foreach (var pair in factions)
            {
                if (pair.Key == f.Owner) continue;
                foreach (CampSlot s in pair.Value.Slots) if (s.Unit != null) enemyAnchors.Add(new KeyValuePair<AnchorSub, Unit>(AnchorSub.Camp, s.Unit));
            }
            cyber?.CollectEnemyAnchors(f.Owner, enemyAnchors);
            if (space != null)
            {
                var hqs = FactionRegistry.GetAllHQs();
                if (hqs != null)
                    foreach (FactionHQ hq in hqs)
                    {
                        if (hq == null || hq == f.Owner) continue;
                        foreach (Unit unit in space.UplinksFor(hq)) if (unit != null) enemyAnchors.Add(new KeyValuePair<AnchorSub, Unit>(AnchorSub.Uplink, unit));
                    }
            }
            f.Desk.CollectKeep(f.Keep);
            f.Obs.Build(f.Owner, u => spawner.Owns(u) || (cyber != null && cyber.IsAnchorUnit(u)) || (space != null && space.Spawner.Owns(u)), enemyAnchors, f.Keep, f.Seeds);
            f.Desk.Refresh(f.Seeds);
        }

        private void OnEvent(FactionSof f, SofEvent e)
        {
            RecordEvent(f, e);
            if (e.Kind == SofEventKind.Unpinned || e.Kind == SofEventKind.Lost) manager.WithdrawSofPost(f.Owner, SupportActionId.SofCover, e.Slot);
            if (e.Kind == SofEventKind.Lost) manager.WithdrawSofPost(f.Owner, SupportActionId.SofLase, e.Slot);
            try { Observed?.Invoke(f.Owner, e); }
            catch (Exception ex) { Plugin.Logger?.LogWarning("[Support.Sof] An observer failed: " + ex.Message); }
            Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " " + SofPageWords.EventLine(new SofEventRow { Kind = e.Kind, Slot = (byte)e.Slot, Mission = e.Mission }) + ".");
        }

        private static void Measure(FactionSof f, CampSlot slot, float now)
        {
            Unit unit = slot.Unit;
            float fraction = slot.Baseline > 0f ? Mathf.Clamp01(UplinkSpawner.Health(unit) / slot.Baseline) : 0f;
            bool down = UplinkSpawner.Down(unit, f.Owner);
            f.Camps.Set(slot.Index, fraction, down, now);
            if (!down && unit != null)
            {
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                f.Camps.SetPosition(slot.Index, (float)p.x, (float)p.z);
            }
        }

        /// <summary>Spec 2.1: a down camp starts its restore fund after the 120 s grace (goal 120); when the bar is full it is rebuilt where it stood. Teams in the field carry on meanwhile.</summary>
        private void Rebuild(FactionSof f, float now)
        {
            foreach (CampSlot slot in f.Slots)
            {
                if (f.Camps.Health(slot.Index) != AnchorHealth.Down) { slot.Bar.Reset(); slot.LastFund = now; continue; }
                if (!f.Camps.PastGrace(slot.Index, now)) { slot.LastFund = now; continue; }
                float dt = Mathf.Clamp(now - slot.LastFund, 0f, 5f);
                slot.LastFund = now;
                bool flat = manager.HumanCount(f.Owner) == 0;
                float took = slot.Bar.AutoFund(dt, manager.CyberTreasury(f.Owner), flat);
                if (took > 0f) manager.CyberTreasurySpend(f.Owner, took);
                if (!slot.Bar.Complete || now < slot.NextRebuildTry) continue;
                slot.NextRebuildTry = now + RelocateRetrySeconds;
                spawner.DiscardGroup(slot.Unit);
                if (!spawner.TryCreateCamp(f.Owner, slot.Index, slot.Spot, slot.Parent, out Unit made)) continue;
                slot.Unit = made;
                slot.Baseline = UplinkSpawner.Health(made);
                slot.Bar.Reset();
                GlobalPosition p = made.transform.position.ToGlobalPosition();
                f.Camps.Restore(slot.Index, (float)p.x, (float)p.z, now);
                Plugin.Logger?.LogInfo("[Support.Sof] " + f.Owner.name + " camp " + slot.Index + " rebuilt.");
            }
        }

        // ---- Establish -----------------------------------------------------------------------------------

        private sealed class Site
        {
            public Airbase Parent; public GlobalPosition Anchor;
            public Site(Airbase parent, GlobalPosition anchor) { Parent = parent; Anchor = anchor; }
        }

        private bool TryEstablish(FactionHQ owner, out FactionSof made)
        {
            made = null;
            if (UnitRegistry.allUnits != null && UnitRegistry.allUnits.Count > MaximumScanUnits) return false;
            Vector2 span = TheaterFrame.Resolve();
            var sites = new List<Site>(); var cands = new List<SiteCandidate>();
            if (FactionRegistry.airbaseLookup != null)
            {
                int inspected = 0, held = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (++inspected > 128 || held >= MaximumBases) break;
                    if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.CurrentHQ != owner || airbase.center == null) continue;
                    held++;
                    Collect(owner, airbase, sites, cands);
                }
            }
            var picks = new List<int>();
            CyberPlacement.Pick(cands, span.magnitude, CampRules.MaxCamps, null, 0f, picks);
            var slots = new List<CampSlot>(CampRules.MaxCamps);
            foreach (int id in picks)
            {
                if (!spawner.TryCreateCamp(owner, slots.Count, sites[id].Anchor, sites[id].Parent, out Unit camp)) continue;
                slots.Add(new CampSlot(slots.Count, camp, sites[id].Anchor, sites[id].Parent));
            }
            if (slots.Count == 0) return false;
            var camps = new SofCampSet(slots.Count);
            made = new FactionSof { Owner = owner, Key = manager.FactionKeyOf(owner), Camps = camps, Rng = new System.Random(unchecked(owner.GetInstanceID() * 7919 + 17)) };
            made.Slots.AddRange(slots);
            foreach (CampSlot slot in slots)
            {
                GlobalPosition p = slot.Unit.transform.position.ToGlobalPosition();
                camps.SetPosition(slot.Index, (float)p.x, (float)p.z);
            }
            FactionSof captured = made;
            made.Desk = new SofDesk(new Ports(this, made), camps);
            made.Desk.Happened += e => OnEvent(captured, e);
            return true;
        }

        /// <summary>Spec 2.1: a camp sits near a friendly airbase but at least 6 km from it. Candidates ring the airbase at 6.5 and 7.5 km.</summary>
        private void Collect(FactionHQ owner, Airbase parent, List<Site> sites, List<SiteCandidate> cands)
        {
            for (int ring = 0; ring < 2; ring++)
                for (int step = 0; step < 12; step++)
                {
                    if (cands.Count >= CyberPlacement.MaxCandidates) return;
                    float angle = (step * 5 % 12) * Mathf.PI / 6f;
                    Vector3 desired = parent.center.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (CampRadius + ring * 1000f);
                    GlobalPosition anchor = desired.ToGlobalPosition();
                    if (!spawner.TryPlanCamp(anchor, parent, out GlobalPosition[] cp, out _)) continue;
                    float front = FrontDistanceOf(owner, desired);
                    sites.Add(new Site(parent, anchor));
                    cands.Add(new SiteCandidate(sites.Count - 1, (float)cp[0].x, (float)cp[0].z, front, front));
                }
        }

        private static float FrontDistanceOf(FactionHQ owner, Vector3 point)
        {
            float nearest = float.PositiveInfinity;
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (airbase == null || airbase.disabled || airbase.center == null || airbase.CurrentHQ == null || airbase.CurrentHQ == owner) continue;
                    Vector3 d = point - airbase.center.position; d.y = 0f;
                    nearest = Mathf.Min(nearest, d.magnitude);
                }
            return float.IsInfinity(nearest) ? 1000000f : nearest;
        }
    }
}
