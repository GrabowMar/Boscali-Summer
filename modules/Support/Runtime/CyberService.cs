using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Host-owned CYBER / EW domain: real EW trucks and data centers per faction, the node graph built from real revealed enemy units, the
    /// intrusion desk, the HOLD effects and the BURN packages. It mirrors the SPACE service: a 1 Hz host tick and scene reset. Every faction's
    /// desk opens empty on its first host second; data centers and trucks exist only once the CYBER front builds them, and a destroyed one stays
    /// down until a new build takes its slot. Everything here is host-only; clients read the CYBER state through the mirror. The class is split by
    /// concern: this file is the anchors and the tick; <c>CyberService.Nodes</c> the real-world node refresh and the verbs;
    /// <c>.Effects</c> the HOLD effects; <c>.Packages</c> the BURN packages; <c>.Mirror</c> the faction view.
    /// </summary>
    internal sealed partial class CyberService : MonoBehaviour, ISceneService
    {
        private const int MaximumFactions = 8, MaximumBases = 16, MaximumScanUnits = 4096;
        /// <summary>An automatic truck deploy (no picked point) stands on a ring this far round the first standing data center.</summary>
        private const float TruckRingMetres = 3000f;

        private sealed class AnchorSlot
        {
            public AnchorKind Kind;
            public int Index;
            /// <summary>The standing unit, its wreck, or null (a truck packing up, or a slot whose spawn failed).</summary>
            public Unit Unit;
            public float Baseline;
            /// <summary>The airbase a data center was raised at (the ASAT launcher's parent); null for a truck.</summary>
            public Airbase Parent;
            /// <summary>A truck packing up (RELOCATE): offline until this mission second, then raised at (PackX, PackZ). 0 when not packing.</summary>
            public float PackUntil, PackX, PackZ;
        }

        private sealed partial class FactionCyber
        {
            public FactionHQ Owner;
            public int Key;
            public CyberAnchorSet Anchors;
            /// <summary>Kept in (kind, index) order: the mirror writes rows in this order and the client numbers trucks by row.</summary>
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
            public CyberOutcome PostPackage(ulong op, in CyberNode node, in PackageDef def, bool exploit, float effort) =>
                service.manager.PostCyberPackage(faction.Owner, op, node, def, effort);
        }

        private readonly Dictionary<FactionHQ, FactionCyber> factions = new Dictionary<FactionHQ, FactionCyber>();
        private readonly CyberAnchorSpawner spawner = new CyberAnchorSpawner();
        /// <summary>Placement scratch: the held land airbases (and the Airbase each stands for), the standing data centers.</summary>
        private readonly List<FrontPlacement.Base> heldBases = new List<FrontPlacement.Base>(MaximumBases);
        private readonly List<Airbase> heldAirbases = new List<Airbase>(MaximumBases);
        private readonly List<(float X, float Z)> centers = new List<(float X, float Z)>(4);
        private SupportManager manager;
        private SpaceService space;
        private PhasedGate tickGate;
        private float nextCleanup, nextWarning;

        // Optional hooks: each is implemented by exactly one of the other files (the call is removed when it is not).
        partial void TickNodes(FactionCyber f, float now);
        partial void TickEffects(float now);
        partial void TickDefence(float now);
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
            tickGate.Reset();
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

        /// <summary>True once any faction has a CYBER desk (the cheap gate of the detection scope patch).</summary>
        internal bool HasFactions => factions.Count > 0;

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
            if (!tickGate.Due(now, 1f, 0.25f)) return;   // 1 Hz slot .25 (table in HighCommandManager); a slot of the mission clock, so it holds however late the service starts
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
            foreach (FactionHQ hq in hqs)
            {
                if (hq == null) continue;
                if (!factions.TryGetValue(hq, out FactionCyber f))
                {
                    if (factions.Count >= MaximumFactions) continue;
                    factions.Add(hq, f = Open(hq));
                    Plugin.Logger?.LogInfo("[Support.Cyber] " + hq.name + ": desk open, no anchors until the front builds them (" + CyberAnchorSpawner.Describe() + ").");
                }
                TickFaction(f, now);
            }
            TickEffects(now);
            TickDefence(now);
        }

        private void TickFaction(FactionCyber f, float now)
        {
            for (int i = 0; i < f.Slots.Count; i++) if (f.Slots[i].PackUntil > 0f && now >= f.Slots[i].PackUntil) Unpack(f, f.Slots[i]);
            foreach (AnchorSlot slot in f.Slots) Measure(f, slot, now);
            TickNodes(f, now);
            f.Desk.Tick();
        }

        private void OnEvent(FactionCyber f, CyberEvent e)
        {
            RecordEvent(f, e);
            RevealTraced(f, e);
            try { Observed?.Invoke(f.Owner, e, VictimOf(f, e)); }
            catch (Exception ex) { Plugin.Logger?.LogWarning("[Support.Cyber] An observer failed: " + ex.Message); }
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

        // ---- Building (OPS FRONTS programme effects, host) -------------------------------------------------

        /// <summary>The faction's CYBER record and desk, empty: no data center or truck until the front builds one (native radar trucks stay plain enemy RADAR nodes).</summary>
        private FactionCyber Open(FactionHQ owner)
        {
            var f = new FactionCyber { Owner = owner, Key = manager.FactionKeyOf(owner), Anchors = new CyberAnchorSet(0, 0) };
            f.Desk = new CyberDesk(new Ports(this, f), f.Anchors);
            f.Desk.EnemyTraceFactor = _ => TraceFactorOf(f.Owner); // an enemy holding our DATA CENTER node: x1.3 (hold effects live in the holder's desk)
            f.Desk.Happened += e => OnEvent(f, e);
            return f;
        }

        /// <summary>
        /// Host: can a data center go up (at the picked point when <paramref name="hasPoint"/>)? A slot must be free (fewer than two, or one down); a picked point must lie within
        /// <see cref="FrontRules.PadPickMetres"/> of a held land airbase (ruling R6: never a carrier); without a point any held land airbase will do. Spawns nothing.
        /// </summary>
        internal FrontOutcome CheckDataCenter(FactionHQ owner, bool hasPoint, float x, float z)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionCyber f)) return FrontOutcome.Unavailable;
            if (FreeIndex(f, AnchorKind.DataCenter) < 0) return FrontOutcome.DataCentersMax;
            CollectHeld(owner);
            if (hasPoint) return FrontPlacement.NearestHeld(heldBases, x, z, FrontRules.PadPickMetres, allowCarrier: false) >= 0 ? FrontOutcome.None : FrontOutcome.NotHeld;
            return heldBases.Count > 0 ? FrontOutcome.None : FrontOutcome.NoSite;
        }

        /// <summary>
        /// DATA CENTER finished: a real center and its two guards on legal ground just outside the picked airbase (the first ring spot that takes), or without a point on the best
        /// rear spot round the held land airbases. It takes the first down slot (the spawner removes that wreck and its guards first) else a new one. False with the refusal in <paramref name="why"/>.
        /// </summary>
        // ponytail: one synchronous ring search (up to 16 bases x 36 points) at programme completion; step it across frames if a hitch shows.
        internal bool TryBuildDataCenter(FactionHQ owner, bool hasPoint, float x, float z, out FrontOutcome why)
        {
            why = CheckDataCenter(owner, hasPoint, x, z);
            if (why != FrontOutcome.None) return false;
            FactionCyber f = factions[owner];
            int index = FreeIndex(f, AnchorKind.DataCenter);
            Discard(f, AnchorKind.DataCenter, index); // the 32-unit spawner cap is shared: the old group goes before the new one spawns
            float diagonal = TheaterFrame.Resolve().magnitude;
            var sites = new List<Site>();
            var cands = new List<SiteCandidate>();
            var order = new List<int>();
            if (hasPoint)
            {
                Collect(owner, heldAirbases[FrontPlacement.NearestHeld(heldBases, x, z, FrontRules.PadPickMetres, allowCarrier: false)], diagonal, sites, cands);
                for (int i = 0; i < sites.Count; i++) order.Add(i);
            }
            else
            {
                foreach (Airbase airbase in heldAirbases) Collect(owner, airbase, diagonal, sites, cands);
                CyberPlacement.Pick(cands, diagonal, 1, null, 0f, order);
            }
            foreach (int id in order)
            {
                if (!spawner.TryCreateCenter(owner, index, sites[id].Anchor, sites[id].Parent, out Unit made)) continue;
                if (!Place(f, AnchorKind.DataCenter, index, made, sites[id].Parent)) break;
                Plugin.Logger?.LogInfo("[Support.Cyber] " + owner.name + " DATA CENTER " + (index + 1) + " built by " + sites[id].Parent.name + ".");
                return true;
            }
            why = FrontOutcome.NoSpot;
            return false;
        }

        /// <summary>
        /// Host: can an EW truck deploy (at the picked point when <paramref name="hasPoint"/>)? Each standing data center, or one queued ahead (<paramref name="queuedCenters"/>), runs one
        /// truck (a truck packing up is not live, so it holds none); a picked point must lie within <see cref="FrontRules.TruckReachMetres"/> of one of them. Spawns nothing.
        /// </summary>
        internal FrontOutcome CheckTruck(FactionHQ owner, bool hasPoint, float x, float z, IReadOnlyList<(float X, float Z)> queuedCenters)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionCyber f)) return FrontOutcome.Unavailable;
            int running = f.Anchors.LiveCount(AnchorKind.DataCenter) + (queuedCenters != null ? queuedCenters.Count : 0);
            if (running <= 0) return FrontOutcome.NoDataCenter;
            if (f.Anchors.LiveCount(AnchorKind.EwTruck) + 1 > running) return FrontOutcome.DataCenterFull;
            if (FreeIndex(f, AnchorKind.EwTruck) < 0) return FrontOutcome.TrucksMax; // one truck stands and the other slot is packing up
            if (!hasPoint) return FrontOutcome.None;
            CopyCenters(f);
            if (queuedCenters != null) centers.AddRange(queuedCenters);
            return FrontPlacement.WithinReach(centers, x, z, FrontRules.TruckReachMetres) ? FrontOutcome.None : FrontOutcome.TooFar;
        }

        /// <summary>
        /// EW TRUCK finished: a real truck at the picked point (nudged up to 120 m round it), or without a point on a ring 3 km round the first standing data center. It needs a free
        /// standing data center now and takes the first down truck slot else a new index (indices stay stable: an intrusion names its truck by index). False with the refusal in <paramref name="why"/>.
        /// </summary>
        internal bool TryDeployTruck(FactionHQ owner, bool hasPoint, float x, float z, out FrontOutcome why)
        {
            why = CheckTruck(owner, hasPoint, x, z, null);
            if (why != FrontOutcome.None) return false;
            FactionCyber f = factions[owner];
            int index = FreeIndex(f, AnchorKind.EwTruck);
            Discard(f, AnchorKind.EwTruck, index); // shared spawner cap: the wreck goes first
            Unit made = null;
            if (hasPoint) TryTruckAt(owner, index, x, z, out made);
            else
            {
                CopyCenters(f);
                // ponytail: up to 12 bearings x 17 footprint tests, once per finished programme; a bearing can fall in an airbase keep-out or on water, so the ring is walked.
                for (int step = 0; step < 12 && made == null && centers.Count > 0; step++)
                {
                    float angle = (step * 5 % 12) * Mathf.PI / 6f;
                    TryTruckAt(owner, index, centers[0].X + Mathf.Sin(angle) * TruckRingMetres, centers[0].Z + Mathf.Cos(angle) * TruckRingMetres, out made);
                }
            }
            if (made == null || !Place(f, AnchorKind.EwTruck, index, made, null)) { why = FrontOutcome.NoSpot; return false; }
            Plugin.Logger?.LogInfo("[Support.Cyber] " + owner.name + " EW TRUCK " + (index + 1) + " deployed.");
            return true;
        }

        /// <summary>
        /// RELOCATE (host): a standing truck packs up for <see cref="FrontRules.TruckPackSeconds"/>, then redeploys at the picked point, which must lie within
        /// <see cref="FrontRules.TruckReachMetres"/> of a standing data center. Packing, the truck is gone and offline: the desk ends the intrusions it sources (SOURCE DOWN) on its
        /// next second, no hop starts from it, and the mirror shows it packing.
        /// </summary>
        internal FrontOutcome MoveTruck(FactionHQ owner, int truck, float x, float z)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionCyber f)) return FrontOutcome.Unavailable;
            AnchorSlot slot = Find(f, AnchorKind.EwTruck, truck);
            if (slot != null && slot.PackUntil > 0f) return FrontOutcome.TruckBusy;
            if (slot == null || f.Anchors.Health(AnchorKind.EwTruck, truck) == AnchorHealth.Down) return FrontOutcome.NoSite;
            CopyCenters(f);
            if (!FrontPlacement.WithinReach(centers, x, z, FrontRules.TruckReachMetres)) return FrontOutcome.TooFar;
            float now = SupportManager.MissionNow();
            if (spawner.Owns(slot.Unit)) spawner.DiscardGroup(slot.Unit);
            slot.Unit = null;
            f.Anchors.Set(AnchorKind.EwTruck, truck, 0f, true, now);
            slot.PackUntil = now + FrontRules.TruckPackSeconds; slot.PackX = x; slot.PackZ = z;
            f.BaseRound = 0;
            Plugin.Logger?.LogInfo("[Support.Cyber] " + f.Owner.name + " EW TRUCK " + (truck + 1) + " packing up.");
            return FrontOutcome.Ok;
        }

        /// <summary>A packed truck is due: it deploys at its picked point, else back where it stood, else its slot stays down (the next EW TRUCK takes it).</summary>
        private void Unpack(FactionCyber f, AnchorSlot slot)
        {
            slot.PackUntil = 0f;
            bool hadSpot = f.Anchors.TryPosition(AnchorKind.EwTruck, slot.Index, out float oldX, out float oldZ);
            if ((TryTruckAt(f.Owner, slot.Index, slot.PackX, slot.PackZ, out Unit made) || (hadSpot && TryTruckAt(f.Owner, slot.Index, oldX, oldZ, out made))) &&
                Place(f, AnchorKind.EwTruck, slot.Index, made, null))
                Plugin.Logger?.LogInfo("[Support.Cyber] " + f.Owner.name + " EW TRUCK " + (slot.Index + 1) + " redeployed.");
            else Plugin.Logger?.LogWarning("[Support.Cyber] " + f.Owner.name + " EW TRUCK " + (slot.Index + 1) + " found no ground to redeploy on: it stays down.");
        }

        /// <summary>A truck on the ground under (x, z), nudged round it when the exact point does not fit.</summary>
        private bool TryTruckAt(FactionHQ owner, int index, float x, float z, out Unit made)
        {
            SupportTargeting.TryMapPoint(new GlobalPosition(x, 0f, z), out Vector3 ground);
            return spawner.TryCreateTruckNear(owner, index, ground.ToGlobalPosition(), null, out made);
        }

        /// <summary>The slot a new anchor of this kind takes: the first down one (never a truck packing up), else the next new index; -1 at the cap.</summary>
        private static int FreeIndex(FactionCyber f, AnchorKind kind)
        {
            foreach (AnchorSlot s in f.Slots)
                if (s.Kind == kind && s.PackUntil <= 0f && f.Anchors.Health(kind, s.Index) == AnchorHealth.Down) return s.Index;
            int next = f.Anchors.Count(kind);
            return next < (kind == AnchorKind.EwTruck ? AnchorRules.MaxTrucks : AnchorRules.MaxDataCenters) ? next : -1;
        }

        private static AnchorSlot Find(FactionCyber f, AnchorKind kind, int index)
        {
            foreach (AnchorSlot s in f.Slots) if (s.Kind == kind && s.Index == index) return s;
            return null;
        }

        /// <summary>A slot about to take a new unit drops its old one: the spawner removes the wreck and any surviving guard (only a group it raised).</summary>
        private void Discard(FactionCyber f, AnchorKind kind, int index)
        {
            AnchorSlot slot = Find(f, kind, index);
            if (slot == null) return;
            if (spawner.Owns(slot.Unit)) spawner.DiscardGroup(slot.Unit);
            slot.Unit = null;
        }

        /// <summary>A freshly spawned anchor takes its slot (the reused one, or a new one kept in (kind, index) order) and stands there as Live.</summary>
        private bool Place(FactionCyber f, AnchorKind kind, int index, Unit unit, Airbase parent)
        {
            GlobalPosition p = unit.transform.position.ToGlobalPosition();
            if (index >= f.Anchors.Count(kind) && f.Anchors.Add(kind, (float)p.x, (float)p.z) != index) { spawner.DiscardGroup(unit); return false; }
            AnchorSlot slot = Find(f, kind, index);
            if (slot == null)
            {
                f.Slots.Add(slot = new AnchorSlot { Kind = kind, Index = index });
                f.Slots.Sort((a, b) => a.Kind != b.Kind ? ((int)a.Kind).CompareTo((int)b.Kind) : a.Index.CompareTo(b.Index));
            }
            slot.Unit = unit; slot.Baseline = UplinkSpawner.Health(unit); slot.Parent = parent; slot.PackUntil = 0f;
            f.Anchors.Restore(kind, index, (float)p.x, (float)p.z, SupportManager.MissionNow());
            f.BaseRound = 0;
            return true;
        }

        /// <summary>The standing data centers' positions into the <see cref="centers"/> scratch.</summary>
        private void CopyCenters(FactionCyber f)
        {
            centers.Clear();
            foreach (AnchorSlot s in f.Slots)
                if (s.Kind == AnchorKind.DataCenter && f.Anchors.Health(s.Kind, s.Index) != AnchorHealth.Down && f.Anchors.TryPosition(s.Kind, s.Index, out float cx, out float cz))
                    centers.Add((cx, cz));
        }

        private sealed class Site
        {
            public Airbase Parent; public GlobalPosition Anchor;
            public Site(Airbase parent, GlobalPosition anchor) { Parent = parent; Anchor = anchor; }
        }

        /// <summary>Fills the held-base scratch: the land airbases the faction holds (a carrier never hosts a data center).</summary>
        private void CollectHeld(FactionHQ owner)
        {
            heldBases.Clear(); heldAirbases.Clear();
            if (FactionRegistry.airbaseLookup == null) return;
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 128 || heldAirbases.Count >= MaximumBases) break;
                if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.CurrentHQ != owner || airbase.center == null) continue;
                GlobalPosition p = airbase.center.position.ToGlobalPosition();
                heldBases.Add(new FrontPlacement.Base(heldAirbases.Count, (float)p.x, (float)p.z, true, false, true));
                heldAirbases.Add(airbase);
            }
        }

        /// <summary>Data center spots on 3 rings of 12 just outside the airbase's keep-out (the spawner refuses ground inside an airbase radius).</summary>
        private void Collect(FactionHQ owner, Airbase parent, float diagonal, List<Site> sites, List<SiteCandidate> cands)
        {
            float exclusion = Mathf.Clamp(500f * Mathf.Clamp(diagonal / 150000f, 0.4f, 2.5f), 250f, 1000f);
            float radius = parent.GetRadius() + exclusion + 160f;
            for (int ring = 0; ring < 3; ring++)
                for (int step = 0; step < 12; step++)
                {
                    if (cands.Count >= CyberPlacement.MaxCandidates) return;
                    float angle = (step * 5 % 12) * Mathf.PI / 6f;
                    Vector3 desired = parent.center.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (radius + ring * 260f);
                    GlobalPosition anchor = desired.ToGlobalPosition();
                    if (!spawner.TryPlanCenter(anchor, parent, out GlobalPosition[] cp, out _)) continue;
                    float front = SupportTargeting.FrontDistanceOf(owner, desired);
                    sites.Add(new Site(parent, anchor));
                    cands.Add(new SiteCandidate(sites.Count - 1, (float)cp[0].x, (float)cp[0].z, front, front));
                }
        }
    }
}
