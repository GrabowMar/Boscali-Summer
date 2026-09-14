using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.Trenches.Visuals;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>Where the next trench sector is expected to be dug, for map symbology.</summary>
    internal readonly struct PlannedEntrenchment
    {
        public readonly FactionHQ Owner;
        public readonly Vector3 Position;
        public readonly Vector3 Threat;
        public readonly float HalfSpan;
        public readonly float Pressure;

        public PlannedEntrenchment(FactionHQ owner, Vector3 position, Vector3 threat, float halfSpan, float pressure)
        {
            Owner = owner;
            Position = position;
            Threat = threat;
            HalfSpan = halfSpan;
            Pressure = pressure;
        }
    }

    /// <summary>
    /// Central manager for the dynamic trench system.
    /// Manages network lifecycles, server-authoritative growth ticks, 3D visual chunks,
    /// and ensures strict bounded ceilings and scene cleanup.
    /// </summary>
    internal sealed class TrenchManager : MonoBehaviour, ISceneService
    {
        public const int MaximumActiveNetworks = 16;
        private const float SeedSlotSpacing = 380f;
        private const float SameOwnerSpacing = 360f;
        private const float OtherOwnerSpacing = 250f;
        private const float ClearedSiteSpacing = 300f;

        private TrenchesSettings settings;
        private ManualLogSource logger;

        private readonly List<TrenchNetwork> networks = new List<TrenchNetwork>(MaximumActiveNetworks);
        private readonly Dictionary<int, TrenchVisualChunk> visualChunks = new Dictionary<int, TrenchVisualChunk>(MaximumActiveNetworks);

        private float nextSimulationTick;
        private readonly Dictionary<int, TrenchGarrison> garrisons = new Dictionary<int, TrenchGarrison>(MaximumActiveNetworks);
        private readonly Dictionary<int, TrenchWorks> works = new Dictionary<int, TrenchWorks>(MaximumActiveNetworks);
        private int nextNetworkId = 1;
        private float nextLinkAttempt;
        private ITerritoryIngress territory;
        private readonly FrontlineSite[] sites = new FrontlineSite[256];
        private readonly List<(FactionHQ owner, Vector3 position, Vector3 threat, float flankLimit, int rank, float pressure)> candidates =
            new List<(FactionHQ, Vector3, Vector3, float, int, float)>(2048);
        private readonly List<PlannedEntrenchment> plannedSites = new List<PlannedEntrenchment>(2048);
        private int candidateIndex;
        private int rejected;
        private float nextGarrisonWarning;
        private float nextGrowthWarning;
        private float nextPlannedRefresh;
        private readonly List<Vector3> clearedSites = new List<Vector3>(64);

        public event Action OnNetworksChanged;

        public IReadOnlyList<TrenchNetwork> Networks => networks;

        /// <summary>Contested frontline sectors where a new trench belt may be dug.</summary>
        public IReadOnlyList<PlannedEntrenchment> PlannedSites => plannedSites;

        public void Configure(TrenchesSettings config, ManualLogSource log, ITerritoryIngress control)
        {
            settings = config;
            logger = log;
            territory = control;
        }

        public void ResetForScene()
        {
            foreach (var work in works.Values) work.Remove();
            works.Clear();
            foreach (var garrison in garrisons.Values) garrison.Remove();
            garrisons.Clear();
            foreach (var chunk in visualChunks.Values)
            {
                if (chunk != null)
                {
                    Destroy(chunk.gameObject);
                }
            }
            visualChunks.Clear();
            networks.Clear();

            TrenchMaterialResolver.ResetForScene();

            nextNetworkId = 1;
            nextGarrisonWarning = 0;
            nextGrowthWarning = 0;
            nextSimulationTick = 0f;
            nextPlannedRefresh = 0f;
            candidates.Clear();
            plannedSites.Clear();
            clearedSites.Clear();
            candidateIndex = rejected = 0;
            nextSeedAttemptAt = Time.unscaledTime + 2.5f;
        }

        private void OnDestroy()
        {
            ResetForScene();
        }

        private float nextSeedAttemptAt;

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value || !GameAccess.IsServer() || Datum.origin == null) return;

            if (candidates.Count > 0) TrySeedInitialNetworks();
            else if (clearedSites.Count < 64 && networks.Count < Math.Min(MaximumActiveNetworks, settings.MaxTrenchNetworks.Value) &&
                Time.unscaledTime >= nextSeedAttemptAt) RefreshCandidates();

            float now = Time.time;
            if (now >= nextSimulationTick)
            {
                nextSimulationTick = now + 0.5f;
                TickSimulation();
            }
        }

        private void RefreshCandidates()
        {
            nextSeedAttemptAt = Time.unscaledTime + 30f;
            nextPlannedRefresh = Time.unscaledTime + 5f;
            ScanPlacement();
            logger?.LogInfo($"[TRENCHES] Placement scan: {candidates.Count} contested frontline slots, {networks.Count} active networks.");
        }

        /// <summary>
        /// Re-reads the frontline for map display (host and clients). Returns true when the
        /// projected trace changed. Never interrupts an in-progress seeding walk.
        /// </summary>
        public bool RefreshPlannedSites()
        {
            if (settings == null || !settings.Enabled.Value || territory == null || candidates.Count > 0) return false;
            if (Time.unscaledTime < nextPlannedRefresh) return false;
            nextPlannedRefresh = Time.unscaledTime + 5f;
            ScanPlacement();
            return true;
        }

        // One border cell side becomes a chain of sector slots spaced along the border.
        // Only borders where both sides actually hold ground are fortifiable, and the
        // hottest (most balanced) sectors sort first, so trenches erupt where troops meet.
        private void ScanPlacement()
        {
            candidates.Clear();
            plannedSites.Clear();
            int factions = 0;
            foreach (FactionHQ owner in FactionRegistry.GetAllHQs())
            {
                if (owner == null) continue;
                if (++factions > 8) break;
                int count = territory.CopyFrontlineSites(owner.GetInstanceID(), sites);
                for (int i = 0; i < count; i++) ExpandSite(owner, sites[i], i);
            }
            candidates.Sort((a, b) =>
            {
                int order = b.pressure.CompareTo(a.pressure);
                if (order != 0) return order;
                order = a.rank.CompareTo(b.rank);
                return order != 0 ? order : a.owner.GetInstanceID().CompareTo(b.owner.GetInstanceID());
            });
            candidateIndex = rejected = 0;
        }

        private void ExpandSite(FactionHQ owner, FrontlineSite site, int rank)
        {
            if (candidates.Count >= 2048 || plannedSites.Count >= 2048 || site.Pressure <= 0f) return;
            float flankLimit = TrenchTacticalMath.CapFlankLimit(site.HalfLength);
            Vector3 center = new Vector3(site.X, 0, site.Z);
            Vector3 tangent = new Vector3(-site.ThreatZ, 0, site.ThreatX);
            Vector3 threat = new Vector3(site.ThreatX, 0, site.ThreatZ);
            int slots = Mathf.Clamp(Mathf.FloorToInt((site.HalfLength * 2f - 120f) / SeedSlotSpacing), 1, 8);
            for (int i = 0; i < slots; i++)
            {
                float offset = (i - (slots - 1) * 0.5f) * SeedSlotSpacing;
                Vector3 position = center + tangent * offset;
                candidates.Add((owner, position, threat, flankLimit, rank, site.Pressure));
                plannedSites.Add(new PlannedEntrenchment(owner, position, threat, flankLimit, site.Pressure));
            }
        }

        private void TrySeedInitialNetworks()
        {
            if (clearedSites.Count >= 64) { candidates.Clear(); return; }
            // One bounded corridor reserve per frame, always working outward from the frontline.
            if (candidateIndex >= candidates.Count)
            {
                logger?.LogInfo($"[TRENCHES] Placement finished: {networks.Count} networks; {rejected} slots rejected (terrain, ownership, or spacing).");
                candidates.Clear();
                return;
            }
            var candidate = candidates[candidateIndex++];
            if (candidate.owner == null) return;
            Vector3 center = candidate.position;
            foreach (var cleared in clearedSites)
            {
                Vector3 delta = cleared - center; delta.y = 0;
                if (delta.sqrMagnitude < ClearedSiteSpacing * ClearedSiteSpacing) return;
            }
            foreach (var existing in networks)
            {
                float spacing = existing.OwnerHq == candidate.owner ? SameOwnerSpacing : OtherOwnerSpacing;
                Vector3 delta = existing.SeedCenter - center;
                delta.y = 0;
                if (delta.sqrMagnitude < spacing * spacing) return;
            }
            var network = CreateTrenchNetwork(center, candidate.threat, candidate.flankLimit,
                candidate.owner, candidate.owner.name + "_Sector");
            if (network == null) { rejected++; return; }
            logger?.LogInfo($"[TRENCHES] Seeded '{network.Name}' at global {network.Center}, flank half-width {candidate.flankLimit:0}m.");
            if (networks.Count >= Math.Min(MaximumActiveNetworks, settings.MaxTrenchNetworks.Value))
            {
                candidates.Clear();
            }
        }

        public TrenchNetwork CreateTrenchNetwork(Vector3 center, Vector3 threatDir, float flankLimit, FactionHQ owner, string name)
        {
            if (!GameAccess.IsServer() || owner == null || territory == null ||
                !TrenchPlacement.ValidateCorridor(center, threatDir, flankLimit, owner.GetInstanceID(),
                    territory, out Vector3 groundCenter)) return null;
            if (networks.Count >= Math.Min(MaximumActiveNetworks, settings?.MaxTrenchNetworks?.Value ?? MaximumActiveNetworks))
            {
                logger?.LogWarning("[TRENCHES] Cannot create network: maximum ceiling reached.");
                return null;
            }

            int id = nextNetworkId++;
            var net = new TrenchNetwork(id, name, owner, groundCenter, threatDir, flankLimit);
            net.PlacementValidator = p => net.Contains(p) &&
                territory.OwnsPosition(owner.GetInstanceID(), p.x, p.z) && TrenchPlacement.TryGround(p, out _);

            if (!TrenchGrowthSimulator.Seed(net, SnapToGround)) return null;
            var garrison = new TrenchGarrison(net);
            TrenchWorks netWorks = null;
            try
            {
                if (!garrison.Establish())
                {
                    if (Time.unscaledTime >= nextGarrisonWarning)
                    {
                        nextGarrisonWarning = Time.unscaledTime + 30f;
                        logger?.LogWarning("[TRENCHES] Site rejected: native MG definition, clear footprint or spawn unavailable. No empty cosmetic position was created.");
                    }
                    return null;
                }
                CreateVisualChunk(net);
                netWorks = new TrenchWorks(net);
                works.Add(net.Id, netWorks);
                netWorks.Deploy(net.Stage);
            }
            catch (Exception ex)
            {
                netWorks?.Remove();
                works.Remove(net.Id);
                garrison.Remove();
                logger?.LogWarning("[TRENCHES] Site creation rolled back: " + ex.Message);
                return null;
            }
            garrisons.Add(net.Id, garrison);
            net.DefenderCount = garrison.Alive;
            net.NextGrowthAt = Time.time + Math.Max(15f, settings.GrowthIntervalSeconds.Value);

            networks.Add(net);

            OnNetworksChanged?.Invoke();
            return net;
        }

        private void CreateVisualChunk(TrenchNetwork net)
        {
            var go = new GameObject($"TrenchVisualChunk_{net.Id}");
            var chunk = go.AddComponent<TrenchVisualChunk>();
            chunk.Lod0Distance = settings != null ? settings.LODDistanceNear.Value : 250f;
            chunk.Lod1Distance = 1200f;
            chunk.Lod2Distance = settings != null ? settings.LODDistanceFar.Value : 3500f;

            try { chunk.Initialize(net); }
            catch { Destroy(go); throw; }
            visualChunks[net.Id] = chunk;
            logger?.LogInfo($"[TRENCHES] World chunk {net.Id}: {chunk.MeshCount} meshes, local center={chunk.WorldCenter}, camera distance={chunk.CameraDistance:0}m, material={chunk.EarthMaterial}.");
        }

        private void TickSimulation()
        {
            bool anyChanged = false;
            bool advancedOne = false;

            for (int i = networks.Count - 1; i >= 0; i--)
            {
                TrenchNetwork net = networks[i];
                TrenchGarrison garrison = garrisons[net.Id];
                bool changed = garrison.Poll(Time.time);
                if (works.TryGetValue(net.Id, out TrenchWorks netWorks)) netWorks.Deploy(net.Stage);
                bool rebuild = false;
                net.DefenderCount = garrison.Alive;
                bool suppressed = Time.time < garrison.SuppressedUntil;
                changed |= net.Suppressed != suppressed;
                net.Suppressed = suppressed;
                if (!net.Overrun && (garrison.Overrun || !StillOwned(net)))
                {
                    net.Overrun = true;
                    net.RetireAt = Time.time + 300f;
                    if (clearedSites.Count < 64) clearedSites.Add(net.SeedCenter);
                    if (!garrison.Overrun) garrison.Remove();
                    net.DefenderCount = 0;
                    changed = true;
                    rebuild = true;
                    logger?.LogInfo($"[TRENCHES] '{net.Name}' neutralized or abandoned; growth stopped, no defender respawns.");
                }
                if (net.Overrun && Time.time >= net.RetireAt)
                {
                    if (visualChunks.TryGetValue(net.Id, out var obsolete) && obsolete != null) Destroy(obsolete.gameObject);
                    visualChunks.Remove(net.Id);
                    if (works.TryGetValue(net.Id, out TrenchWorks obsoleteWorks)) obsoleteWorks.Remove();
                    works.Remove(net.Id);
                    networks.RemoveAt(i);
                    garrison.Remove();
                    garrisons.Remove(net.Id);
                    anyChanged = true;
                    continue;
                }
                if (suppressed) net.NextGrowthAt = Math.Max(net.NextGrowthAt, garrison.SuppressedUntil);
                if (!advancedOne && TrenchTacticalMath.CanConstruct(net.Overrun, Time.time, garrison.SuppressedUntil, net.NextGrowthAt))
                {
                    advancedOne = true;
                    net.NextGrowthAt = Time.time + Math.Max(15f, settings.GrowthIntervalSeconds.Value);
                    rebuild = TrenchGrowthSimulator.AdvanceSimulation(net, SnapToGround);
                    if (!rebuild && net.Stage != TrenchStage.Stage5_Redoubt && net.Stage != TrenchStage.Stage6_Saps &&
                        Time.unscaledTime >= nextGrowthWarning)
                    {
                        nextGrowthWarning = Time.unscaledTime + 60f;
                        logger?.LogWarning($"[TRENCHES] '{net.Name}' growth held at {net.Stage}: {TrenchGrowthSimulator.LastFailure}");
                    }
                    changed |= rebuild;
                    garrison.Reinforce();
                    changed |= garrison.Poll(Time.time);
                    net.DefenderCount = garrison.Alive;
                }
                if (changed)
                {
                    anyChanged = true;
                    logger?.LogInfo($"[TRENCHES] '{net.Name}': {net.Stage}, {net.DefenderCount} defenders, {net.NodeCount} nodes/{net.EdgeCount} edges, suppressed={net.Suppressed}, overrun={net.Overrun}.");
                    if (rebuild && visualChunks.TryGetValue(net.Id, out TrenchVisualChunk chunk) && chunk != null)
                    {
                        chunk.Rebuild();
                    }
                }
            }

            if (Time.time >= nextLinkAttempt)
            {
                nextLinkAttempt = Time.time + 5f;
                anyChanged |= TryLinkNeighbors();
            }

            if (anyChanged)
            {
                OnNetworksChanged?.Invoke();
            }
        }

        /// <summary>
        /// Joins the fire-trench ends of adjacent same-faction sectors with a short junction
        /// trench, so a chain of sectors becomes one continuous front line.
        /// </summary>
        private bool TryLinkNeighbors()
        {
            bool linked = false;
            for (int i = 0; i < networks.Count; i++)
            {
                TrenchNetwork a = networks[i];
                if (a.Stage < TrenchStage.Stage5_Redoubt) continue;
                for (int j = i + 1; j < networks.Count; j++)
                {
                    TrenchNetwork b = networks[j];
                    if (b.OwnerHq != a.OwnerHq || b.Stage < TrenchStage.Stage5_Redoubt) continue;
                    TrenchNode endA = a.NearestFrontNode(b.SeedCenter, out _);
                    if (endA == null) continue;
                    TrenchNode endB = b.NearestFrontNode(endA.Position, out _);
                    if (endB == null) continue;
                    float gap = Vector3.Distance(endA.Position, endB.Position);
                    if (!TrenchTacticalMath.IsSappingEligible(gap)) continue;
                    int before = a.EdgeCount;
                    a.AddEdge(endA.Id, endB.Id, TrenchEdgeType.ZigzagFireTrench, TrenchStage.Stage5_Redoubt, SnapToGround);
                    if (a.EdgeCount == before) continue;
                    if (visualChunks.TryGetValue(a.Id, out TrenchVisualChunk chunkA) && chunkA != null) chunkA.Rebuild();
                    if (visualChunks.TryGetValue(b.Id, out TrenchVisualChunk chunkB) && chunkB != null) chunkB.Rebuild();
                    logger?.LogInfo($"[TRENCHES] Junction trench joined '{a.Name}' to '{b.Name}' ({gap:0}m).");
                    linked = true;
                }
            }
            return linked;
        }

        public static Vector3 SnapToGround(Vector3 position)
        {
            return TrenchPlacement.TryGround(position, out Vector3 ground) ? ground : position;
        }

        private bool StillOwned(TrenchNetwork net)
        {
            // Frontline proximity gates new sites. Existing defenders must not erase
            // their own position when their troop pressure advances the border.
            return net.OwnerHq != null && territory.OwnsPosition(net.OwnerHq.GetInstanceID(), net.SeedCenter.x, net.SeedCenter.z);
        }
    }
}
