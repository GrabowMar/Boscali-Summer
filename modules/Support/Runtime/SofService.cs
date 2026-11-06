using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Host-owned SOF / JTAC domain: real camps per faction, the abstract teams (raise, route, exposure, pinned, missions), held buildings and the helicopter lift.
    /// It mirrors the CYBER service: a 1 Hz host tick, scene reset, everything host-only; clients read the SOF state through the mirror. Every faction's desk opens
    /// empty on its first host second; camps exist only once the SOF front builds them, and a destroyed one stays down until a new CAMP build takes its slot.
    /// The class is split by concern: this file is the camps and the tick; <c>SofService.Effects</c> the mission effects and the reveal path;
    /// <c>.Lift</c> the helicopter lift, cover kills and the cover/lase posts; <c>.Mirror</c> the faction view and the host verbs.
    /// </summary>
    internal sealed partial class SofService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8, MaximumBases = 16, MaximumScanUnits = 4096;
        private const float RefreshSeconds = 10f, CampRadius = 6500f;

        private sealed class CampSlot
        {
            public int Index;
            /// <summary>The standing camp, its wreck, or null (a reused slot whose new camp found no ground).</summary>
            public Unit Unit;
            public float Baseline;
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
        private readonly CyberAnchorSpawner spawner = new CyberAnchorSpawner();
        private readonly List<KeyValuePair<AnchorSub, Unit>> enemyAnchors = new List<KeyValuePair<AnchorSub, Unit>>(16);
        /// <summary>Placement scratch: every land airbase (ours flagged; the own-ground test), and the land airbases the faction holds.</summary>
        private readonly List<FrontPlacement.Base> landBases = new List<FrontPlacement.Base>(32);
        private readonly List<Airbase> heldAirbases = new List<Airbase>(MaximumBases);
        private SupportManager manager;
        private SpaceService space;
        private CyberService cyber;
        private PhasedGate tickGate;
        private float nextCleanup, nextWarning;

        // Optional hooks: each is implemented by exactly one of the other files (the call is removed when it is not).
        partial void TickEffects(FactionSof f, float now);
        partial void TickLift(FactionSof f, float now);
        partial void RecordEvent(FactionSof f, SofEvent e);
        partial void ResetEffects();
        partial void TickFob(FactionSof f, float now);
        partial void TickDefence(float now);

        internal static SofService Active { get; private set; }

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
            tickGate.Reset();
            spawner.ResetForScene();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            ResetForScene();
        }

        // ---- Reads ---------------------------------------------------------------------------------

        internal bool HasSof(FactionHQ owner) => owner != null && factions.ContainsKey(owner);

        /// <summary>
        /// TRAIN TEAM programme finished: one team is raised at the camp (or the FOB) for free, up to two per live camp (+2 with a FOB), four at most (<see cref="SofDesk.TeamCap"/>).
        /// It joins OVERLORD's pool until an operator tasks it. The refusal says why when none could be.
        /// </summary>
        internal SofResult TrainTeam(FactionHQ owner) =>
            owner != null && factions.TryGetValue(owner, out FactionSof f) ? f.Desk.Raise(Domain.Space.SpaceContacts.WatchOfficerId) : new SofResult(SofOutcome.Unavailable);

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
            if (!GameAccess.IsServer() || !MissionManager.IsRunning ||
                (GameManager.gameState != GameState.SinglePlayer && GameManager.gameState != GameState.Multiplayer)) return;
            float now = SupportManager.MissionNow();
            foreach (var pair in factions) pair.Value.Obs.Step();   // the rest of the enemy snapshot the last tick began, a slice a frame
            if (!tickGate.Due(now, 1f, 0.5f)) return;   // 1 Hz slot .5 (table in HighCommandManager)
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
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                if (!factions.TryGetValue(hq, out FactionSof f))
                {
                    if (factions.Count >= MaximumFactions) continue;
                    factions.Add(hq, f = Open(hq));
                    Plugin.Logger?.LogInfo("[Support.Sof] " + hq.name + ": desk open, no camp until the front builds one (" + CyberAnchorSpawner.DescribeCamp() + ").");
                }
                TickFaction(f, now);
            }
            TickDefence(now);
        }

        private void TickFaction(FactionSof f, float now)
        {
            foreach (CampSlot slot in f.Slots) Measure(f, slot, now);
            // ponytail: no grace timer, an operator who left the faction hands the team back to OVERLORD's pool at once; add a delay if reconnect flapping matters.
            foreach (SofTeam t in f.Desk.Teams)
                if (t.Active && t.Raiser != Domain.Space.SpaceContacts.WatchOfficerId && !manager.InFaction(f.Owner, t.Raiser)) t.Raiser = Domain.Space.SpaceContacts.WatchOfficerId;
            f.Obs.Snapshot(f.Owner, now);
            if (now >= f.NextRefresh) { f.NextRefresh = now + RefreshSeconds; RefreshTargets(f); }
            TickLift(f, now);
            f.Desk.Tick();
            TickEffects(f, now);
            TickFob(f, now);
        }

        private void RefreshTargets(FactionSof f)
        {
            FillEnemyAnchors(f);
            f.Desk.CollectKeep(f.Keep);
            f.Obs.Build(f.Owner, u => spawner.Owns(u) || (cyber != null && cyber.IsAnchorUnit(u)) || (space != null && space.Spawner.Owns(u)), enemyAnchors, f.Keep, f.Seeds);
            f.Desk.Refresh(f.Seeds);
        }

        /// <summary>Every other faction's camps, EW trucks, data centers and uplinks, in <see cref="enemyAnchors"/>.</summary>
        private void FillEnemyAnchors(FactionSof f)
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

        // ---- Building (OPS FRONTS programme effects, host) -------------------------------------------------

        /// <summary>The faction's SOF record and desk, empty: two unplaced camp slots, no team until a TEAM build raises one.</summary>
        private FactionSof Open(FactionHQ owner)
        {
            var f = new FactionSof { Owner = owner, Key = manager.FactionKeyOf(owner), Camps = new SofCampSet(CampRules.MaxCamps), Rng = new System.Random(unchecked(owner.GetInstanceID() * 7919 + 17)) };
            f.Desk = new SofDesk(new Ports(this, f), f.Camps);
            f.Desk.Happened += e => OnEvent(f, e);
            return f;
        }

        /// <summary>
        /// Host: can a camp go up (at the picked point when <paramref name="hasPoint"/>)? A slot must be free (fewer than two, or one down); a picked point must be own ground (the nearest
        /// land airbase is ours); without a point any held land airbase will do. Water passes here and fails the build (NO SPOT). Spawns nothing.
        /// </summary>
        internal FrontOutcome CheckCamp(FactionHQ owner, bool hasPoint, float x, float z)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionSof f)) return FrontOutcome.Unavailable;
            if (f.Camps.FreeSlot() < 0) return FrontOutcome.CampsMax;
            CollectBases(owner);
            if (hasPoint) return FrontPlacement.OwnGround(landBases, x, z) ? FrontOutcome.None : FrontOutcome.NotOwnGround;
            return heldAirbases.Count > 0 ? FrontOutcome.None : FrontOutcome.NoSite;
        }

        /// <summary>
        /// CAMP finished: a real camp and its two guards on dry level ground at the picked point (nudged up to 120 m round it), or without a point on the best rear ring spot round the held
        /// land airbases, clear of the standing camps. It takes the first free slot; a down one first loses its wreck and guards. False with the refusal in <paramref name="why"/>.
        /// </summary>
        internal bool TryBuildCamp(FactionHQ owner, bool hasPoint, float x, float z, out FrontOutcome why)
        {
            why = CheckCamp(owner, hasPoint, x, z);
            if (why != FrontOutcome.None) return false;
            FactionSof f = factions[owner];
            int index = f.Camps.FreeSlot();
            CampSlot slot = f.Slots.Find(s => s.Index == index);
            if (slot != null)
            {
                if (spawner.Owns(slot.Unit)) spawner.DiscardGroup(slot.Unit); // the 32-unit spawner cap is shared: the old group goes before the new one spawns
                slot.Unit = null;
            }
            Unit made = null;
            if (hasPoint)
            {
                SupportTargeting.TryMapPoint(new GlobalPosition(x, 0f, z), out Vector3 ground);
                spawner.TryCreateCamp(owner, index, ground.ToGlobalPosition(), null, out made);
            }
            else made = RearCamp(f, index);
            if (made == null) { why = FrontOutcome.NoSpot; return false; }
            if (slot == null)
            {
                f.Slots.Add(slot = new CampSlot { Index = index });
                f.Slots.Sort((a, b) => a.Index.CompareTo(b.Index));
            }
            slot.Unit = made; slot.Baseline = UplinkSpawner.Health(made);
            GlobalPosition p = made.transform.position.ToGlobalPosition();
            f.Camps.Place(index, (float)p.x, (float)p.z);
            Plugin.Logger?.LogInfo("[Support.Sof] " + owner.name + " CAMP " + (index + 1) + " built at " + Mathf.RoundToInt((float)p.x) + ", " + Mathf.RoundToInt((float)p.z) + ".");
            return true;
        }

        private sealed class Site
        {
            public Airbase Parent; public GlobalPosition Anchor;
            public Site(Airbase parent, GlobalPosition anchor) { Parent = parent; Anchor = anchor; }
        }

        /// <summary>
        /// The automatic camp (no picked point): the 6.5/7.5 km rings round the held land airbases (else the wide fan), deepest rear first, at least the CYBER separation from every
        /// standing camp. The first pick that spawns wins; null when none does.
        /// </summary>
        // ponytail: up to 16 bases x 24 ring spots (or 96 fan spots) planned synchronously at programme completion; step it across frames if a hitch shows.
        private Unit RearCamp(FactionSof f, int index)
        {
            var sites = new List<Site>(); var cands = new List<SiteCandidate>(); var avoid = new List<SiteCandidate>(CampRules.MaxCamps); var picks = new List<int>();
            foreach (Airbase airbase in heldAirbases) Collect(f.Owner, airbase, sites, cands);
            if (cands.Count == 0 && heldAirbases.Count > 0) CollectWide(f.Owner, heldAirbases, sites, cands);
            for (int i = 0; i < f.Camps.Capacity; i++)
                if (i != index && f.Camps.Health(i) != AnchorHealth.Down && f.Camps.TryPosition(i, out float cx, out float cz)) avoid.Add(new SiteCandidate(i, cx, cz, 0f, 0f));
            float diagonal = TheaterFrame.Resolve().magnitude;
            CyberPlacement.Pick(cands, diagonal, CampRules.MaxCamps, avoid, diagonal * CyberPlacement.SeparationFraction, picks);
            foreach (int id in picks)
                if (spawner.TryCreateCamp(f.Owner, index, sites[id].Anchor, sites[id].Parent, out Unit made)) return made;
            return null;
        }

        /// <summary>Fills the placement scratch: every land airbase (a carrier is never ground) with ours flagged, and the land airbases the faction holds.</summary>
        private void CollectBases(FactionHQ owner)
        {
            landBases.Clear(); heldAirbases.Clear();
            if (FactionRegistry.airbaseLookup == null) return;
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 128) break;
                if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.center == null) continue;
                bool ours = airbase.CurrentHQ == owner;
                GlobalPosition p = airbase.center.position.ToGlobalPosition();
                landBases.Add(new FrontPlacement.Base(landBases.Count, (float)p.x, (float)p.z, ours, false, true));
                if (ours && heldAirbases.Count < MaximumBases) heldAirbases.Add(airbase);
            }
        }

        /// <summary>Spec 2.1: a camp sits near a friendly airbase but at least 6 km from it. Candidates ring the airbase at 6.5 and 7.5 km.</summary>
        private void Collect(FactionHQ owner, Airbase parent, List<Site> sites, List<SiteCandidate> cands)
        {
            for (int ring = 0; ring < 2; ring++)
                for (int step = 0; step < 12; step++)
                {
                    if (cands.Count >= CyberPlacement.MaxCandidates) return;
                    TryAdd(owner, parent, (step * 5 % 12) * Mathf.PI / 6f, CampRadius + ring * 1000f, sites, cands);
                }
        }

        private const int WideBudget = 96, WideRings = 6, WideBearings = 16;
        private int wideCursor;

        /// <summary>
        /// The 6.5/7.5 km rings held nothing legal (water, steep or crowded ground): fan out to 3..12 km at 16 bearings with the same legality checks. At most <see cref="WideBudget"/> spots
        /// per attempt, resuming where the last attempt stopped, so the 30 s retries cover the whole fan without paying for it all at once.
        /// </summary>
        private void CollectWide(FactionHQ owner, List<Airbase> bases, List<Site> sites, List<SiteCandidate> cands)
        {
            int total = bases.Count * WideRings * WideBearings, tries = Math.Min(total, WideBudget);
            for (int k = 0; k < tries && cands.Count < CyberPlacement.MaxCandidates; k++)
            {
                int i = (wideCursor + k) % total;
                TryAdd(owner, bases[i / (WideRings * WideBearings)], i % WideBearings * 2f * Mathf.PI / WideBearings, 3000f + i / WideBearings % WideRings * 1800f, sites, cands);
            }
            wideCursor = (wideCursor + tries) % total;
        }

        private void TryAdd(FactionHQ owner, Airbase parent, float angle, float radius, List<Site> sites, List<SiteCandidate> cands)
        {
            Vector3 desired = parent.center.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            GlobalPosition anchor = desired.ToGlobalPosition();
            if (!spawner.TryPlanCamp(anchor, parent, out GlobalPosition[] cp, out _)) return;
            float front = SupportTargeting.FrontDistanceOf(owner, desired);
            sites.Add(new Site(parent, anchor));
            cands.Add(new SiteCandidate(sites.Count - 1, (float)cp[0].x, (float)cp[0].z, front, front));
        }
    }
}
