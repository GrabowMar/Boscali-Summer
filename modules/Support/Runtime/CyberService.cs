using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Host-owned CYBER / EW domain: real EW trucks and data centers per faction, the node graph built from real revealed enemy units, the
    /// intrusion desk, the HOLD effects and the BURN packages. It mirrors the SPACE service: a 1 Hz host tick, scene reset, one faction
    /// established per mission second. Everything here is host-only; clients read the CYBER state through the mirror. The class is split by
    /// concern: this file is the anchors and the tick; <c>CyberService.Nodes</c> the real-world node refresh and the verbs;
    /// <c>.Effects</c> the HOLD effects; <c>.Packages</c> the BURN packages; <c>.Mirror</c> the faction view.
    /// </summary>
    internal sealed partial class CyberService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8, MaximumBases = 16, MaximumScanUnits = 4096;
        private const float RelocateRetrySeconds = 30f;

        private sealed class AnchorSlot
        {
            public AnchorKind Kind;
            public int Index;
            public Unit Unit;
            public float Baseline;
            public GlobalPosition Spot;
            public Airbase Parent;
            public readonly RebuildBar Bar;
            public float NextRebuildTry, LastFund;
            public AnchorSlot(AnchorKind kind, int index, Unit unit, GlobalPosition spot, Airbase parent)
            {
                Kind = kind; Index = index; Unit = unit; Spot = spot; Parent = parent;
                Baseline = UplinkSpawner.Health(unit);
                Bar = new RebuildBar(AnchorRules.RebuildGoal(kind));
            }
        }

        private sealed partial class FactionCyber
        {
            public FactionHQ Owner;
            public int Key;
            public CyberAnchorSet Anchors;
            public readonly List<AnchorSlot> Slots = new List<AnchorSlot>(4);
            public CyberDesk Desk;
        }

        /// <summary>What the pure desk needs from the live game: the clock, the census, the wallet and the TASKED board.</summary>
        private sealed class Ports : ICyberPorts
        {
            private readonly CyberService service;
            private readonly FactionCyber faction;
            public Ports(CyberService service, FactionCyber faction) { this.service = service; this.faction = faction; }
            public float Now => SupportManager.MissionNow();
            public int Humans => service.manager != null ? service.manager.HumanCount(faction.Owner) : 1;
            public int Owner => faction.Key;
            public CyberOutcome TrySpend(ulong op, int cr, out int detail) => service.manager.CyberSpend(faction.Owner, op, cr, out detail);
            public void Refund(ulong op, int cr) => service.manager.CyberRefund(faction.Owner, op, cr);
            public bool PostPackage(ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort) =>
                service.manager.PostCyberPackage(faction.Owner, op, node, def, exploit, effort);
        }

        private readonly Dictionary<FactionHQ, FactionCyber> factions = new Dictionary<FactionHQ, FactionCyber>();
        private readonly Dictionary<FactionHQ, float> nextAttempt = new Dictionary<FactionHQ, float>();
        private readonly CyberAnchorSpawner spawner = new CyberAnchorSpawner();
        private SupportManager manager;
        private SpaceService space;
        private float nextTick, nextCleanup, nextWarning;

        // Optional hooks: each is implemented by exactly one of the other files (the call is removed when it is not).
        partial void TickNodes(FactionCyber f, float now);
        partial void TickEffects(float now);
        partial void RecordEvent(FactionCyber f, CyberEvent e);
        partial void RevealTraced(FactionCyber f, CyberEvent e);
        partial void ResetEffects();
        partial void ResetPackages();

        internal static CyberService Active { get; private set; }
        internal CyberAnchorSpawner Spawner => spawner;
        internal int Generation { get; private set; } = 1;

        public void Configure(SupportManager support, SpaceService spaceService)
        {
            manager = support;
            space = spaceService;
            Active = this;
        }

        public void ResetForScene()
        {
            foreach (var pair in factions) pair.Value.Desk.Reset();
            ResetEffects();
            ResetPackages();
            Generation = Generation == int.MaxValue ? 1 : Generation + 1;
            factions.Clear();
            nextAttempt.Clear();
            nextTick = 0f;
            spawner.ResetForScene();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            ResetForScene();
        }

        // ---- Reads -------------------------------------------------------------------------------

        internal bool TryDesk(FactionHQ owner, out CyberDesk desk)
        {
            desk = null;
            if (owner == null || !factions.TryGetValue(owner, out FactionCyber f)) return false;
            desk = f.Desk;
            return true;
        }

        /// <summary>True once this faction has at least one standing EW truck.</summary>
        internal bool HasCyber(FactionHQ owner) => owner != null && factions.ContainsKey(owner);

        // ---- Host tick -----------------------------------------------------------------------------

        private void Update()
        {
            if (manager?.Settings == null || !manager.Settings.Enabled.Value || !manager.Settings.CyberEnabled.Value)
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
                if (Time.unscaledTime >= nextWarning) { nextWarning = Time.unscaledTime + 10f; Plugin.Logger?.LogWarning("[Support.Cyber] Tick failed: " + e.Message); }
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
                if (factions.TryGetValue(hq, out FactionCyber f)) { TickFaction(f, now); continue; }
                if (establishedOne || factions.Count >= MaximumFactions || (nextAttempt.TryGetValue(hq, out float retry) && now < retry)) continue;
                if (nextAttempt.Count >= MaximumFactions && !nextAttempt.ContainsKey(hq)) continue;
                nextAttempt[hq] = now + RelocateRetrySeconds;
                establishedOne = true; // initial world sampling is limited to one faction per mission second
                if (TryEstablish(hq, out f)) { factions.Add(hq, f); Plugin.Logger?.LogInfo("[Support.Cyber] " + hq.name + ": " + f.Slots.Count + " native anchor(s), " + CyberAnchorSpawner.Describe() + "."); }
            }
            TickEffects(now);
        }

        private void TickFaction(FactionCyber f, float now)
        {
            foreach (AnchorSlot slot in f.Slots) Measure(f, slot, now);
            Rebuild(f, now);
            TickNodes(f, now);
            f.Desk.Tick();
        }

        private void OnEvent(FactionCyber f, CyberEvent e)
        {
            RecordEvent(f, e);
            RevealTraced(f, e);
        }

        private static void Measure(FactionCyber f, AnchorSlot slot, float now)
        {
            Unit unit = slot.Unit;
            float fraction = slot.Baseline > 0f ? Mathf.Clamp01(UplinkSpawner.Health(unit) / slot.Baseline) : 0f;
            bool down = UplinkSpawner.Down(unit, f.Owner);
            f.Anchors.Set(slot.Kind, slot.Index, fraction, down, now);
            if (!down && unit != null)
            {
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                f.Anchors.SetPosition(slot.Kind, slot.Index, (float)p.x, (float)p.z);
            }
        }

        /// <summary>Spec §1.1 / core §7: a down anchor starts its restore fund after the 120 s grace; when the bar is full it is rebuilt where it stood.</summary>
        private void Rebuild(FactionCyber f, float now)
        {
            foreach (AnchorSlot slot in f.Slots)
            {
                if (f.Anchors.Health(slot.Kind, slot.Index) != AnchorHealth.Down) { slot.Bar.Reset(); slot.LastFund = now; continue; }
                if (!f.Anchors.PastGrace(slot.Kind, slot.Index, now)) { slot.LastFund = now; continue; }
                float dt = Mathf.Clamp(now - slot.LastFund, 0f, 5f);
                slot.LastFund = now;
                bool flat = manager.HumanCount(f.Owner) == 0;
                float took = slot.Bar.AutoFund(dt, manager.CyberTreasury(f.Owner), flat);
                if (took > 0f) manager.CyberTreasurySpend(f.Owner, took);
                if (!slot.Bar.Complete || now < slot.NextRebuildTry) continue;
                slot.NextRebuildTry = now + RelocateRetrySeconds;
                spawner.DiscardGroup(slot.Unit);
                Unit made;
                bool ok = slot.Kind == AnchorKind.EwTruck ? spawner.TryCreateTruck(f.Owner, slot.Index, slot.Spot, slot.Parent, out made)
                    : spawner.TryCreateCenter(f.Owner, slot.Index, slot.Spot, slot.Parent, out made);
                if (!ok) continue;
                slot.Unit = made;
                slot.Baseline = UplinkSpawner.Health(made);
                slot.Bar.Reset();
                GlobalPosition p = made.transform.position.ToGlobalPosition();
                f.Anchors.Restore(slot.Kind, slot.Index, (float)p.x, (float)p.z, now);
                Plugin.Logger?.LogInfo("[Support.Cyber] " + f.Owner.name + " " + slot.Kind + " " + slot.Index + " rebuilt.");
            }
        }

        // ---- Establish ---------------------------------------------------------------------------------

        private sealed class Site
        {
            public Airbase Parent; public GlobalPosition Anchor;
            public Site(Airbase parent, GlobalPosition anchor) { Parent = parent; Anchor = anchor; }
        }

        private bool TryEstablish(FactionHQ owner, out FactionCyber made)
        {
            made = null;
            if (UnitRegistry.allUnits != null && UnitRegistry.allUnits.Count > MaximumScanUnits) return false;
            Vector2 span = TheaterFrame.Resolve();
            var truckSites = new List<Site>(); var truckCands = new List<SiteCandidate>();
            var centerSites = new List<Site>(); var centerCands = new List<SiteCandidate>();
            if (FactionRegistry.airbaseLookup != null)
            {
                int inspected = 0, held = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (++inspected > 128 || held >= MaximumBases) break;
                    if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.CurrentHQ != owner || airbase.center == null) continue;
                    held++;
                    Collect(owner, airbase, span.magnitude, truckSites, truckCands, centerSites, centerCands);
                }
            }
            var picks = new List<int>();
            CyberPlacement.Pick(truckCands, span.magnitude, AnchorRules.MaxTrucks, null, 0f, picks);
            var pickedTrucks = new List<SiteCandidate>();
            var slots = new List<AnchorSlot>(4);
            int created = 0;
            foreach (int id in picks)
            {
                if (!spawner.TryCreateTruck(owner, created, truckSites[id].Anchor, truckSites[id].Parent, out Unit truck)) continue;
                slots.Add(new AnchorSlot(AnchorKind.EwTruck, created++, truck, truckSites[id].Anchor, truckSites[id].Parent));
                pickedTrucks.Add(truckCands[id]);
            }
            if (slots.Count == 0) return false;
            CyberPlacement.Pick(centerCands, span.magnitude, AnchorRules.MaxDataCenters, pickedTrucks, 600f, picks);
            int centers = 0;
            foreach (int id in picks)
            {
                if (!spawner.TryCreateCenter(owner, centers, centerSites[id].Anchor, centerSites[id].Parent, out Unit center)) continue;
                slots.Add(new AnchorSlot(AnchorKind.DataCenter, centers++, center, centerSites[id].Anchor, centerSites[id].Parent));
            }
            var anchors = new CyberAnchorSet(created, centers);
            made = new FactionCyber { Owner = owner, Key = manager.FactionKeyOf(owner), Anchors = anchors };
            made.Slots.AddRange(slots);
            foreach (AnchorSlot slot in slots)
            {
                GlobalPosition p = slot.Unit.transform.position.ToGlobalPosition();
                anchors.SetPosition(slot.Kind, slot.Index, (float)p.x, (float)p.z);
            }
            FactionCyber captured = made;
            made.Desk = new CyberDesk(new Ports(this, made), anchors);
            made.Desk.Happened += e => OnEvent(captured, e);
            return true;
        }

        private void Collect(FactionHQ owner, Airbase parent, float diagonal, List<Site> truckSites, List<SiteCandidate> truckCands,
            List<Site> centerSites, List<SiteCandidate> centerCands)
        {
            float exclusion = Mathf.Clamp(500f * Mathf.Clamp(diagonal / 150000f, 0.4f, 2.5f), 250f, 1000f);
            float radius = parent.GetRadius() + exclusion + 160f;
            for (int ring = 0; ring < 3; ring++)
                for (int step = 0; step < 12; step++)
                {
                    float angle = (step * 5 % 12) * Mathf.PI / 6f;
                    Vector3 desired = parent.center.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (radius + ring * 260f);
                    GlobalPosition anchor = desired.ToGlobalPosition();
                    float front = FrontDistanceOf(owner, desired);
                    if (truckCands.Count < CyberPlacement.MaxCandidates && spawner.TryPlanTruck(anchor, parent, out GlobalPosition tp, out _))
                    {
                        truckSites.Add(new Site(parent, anchor));
                        truckCands.Add(new SiteCandidate(truckSites.Count - 1, (float)tp.x, (float)tp.z, front, front));
                    }
                    if (centerCands.Count < CyberPlacement.MaxCandidates && spawner.TryPlanCenter(anchor, parent, out GlobalPosition[] cp, out _))
                    {
                        centerSites.Add(new Site(parent, anchor));
                        centerCands.Add(new SiteCandidate(centerSites.Count - 1, (float)cp[0].x, (float)cp[0].z, front, front));
                    }
                }
        }

        /// <summary>Distance to the nearest airbase held by someone else: the front proxy (the same fallback SPACE uses without territory data).</summary>
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
