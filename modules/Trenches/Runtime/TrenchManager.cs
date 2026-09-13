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
    /// <summary>
    /// Central manager for the dynamic trench system.
    /// Manages network lifecycles, server-authoritative growth ticks, 3D visual chunks,
    /// and ensures strict bounded ceilings and scene cleanup.
    /// </summary>
    internal sealed class TrenchManager : MonoBehaviour, ISceneService
    {
        public const int MaximumActiveNetworks = 16;

        private TrenchesSettings settings;
        private ManualLogSource logger;

        private readonly List<TrenchNetwork> networks = new List<TrenchNetwork>(MaximumActiveNetworks);
        private readonly Dictionary<int, TrenchVisualChunk> visualChunks = new Dictionary<int, TrenchVisualChunk>(MaximumActiveNetworks);

        private float nextSimulationTick;
        private readonly Dictionary<int, TrenchGarrison> garrisons = new Dictionary<int, TrenchGarrison>(MaximumActiveNetworks);
        private int nextNetworkId = 1;
        private ITerritoryIngress territory;
        private readonly TrenchPlacement placement = new TrenchPlacement();
        private readonly FrontlineSite[] sites = new FrontlineSite[256];
        private readonly List<(FactionHQ owner, FrontlineSite site, int rank)> candidates = new List<(FactionHQ, FrontlineSite, int)>(2048);
        private int candidateIndex;
        private bool roadPass;
        private int rejected;
        private float nextGarrisonWarning;
        private readonly List<Vector3> clearedSites = new List<Vector3>(64);

        public event Action OnNetworksChanged;

        public IReadOnlyList<TrenchNetwork> Networks => networks;

        public void Configure(TrenchesSettings config, ManualLogSource log, ITerritoryIngress control)
        {
            settings = config;
            logger = log;
            territory = control;
        }

        public void ResetForScene()
        {
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
            nextSimulationTick = 0f;
            candidates.Clear();
            clearedSites.Clear();
            placement.Reset();
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
            candidates.Clear();
            placement.ReadRoads();
            int factions = 0;
            foreach (FactionHQ owner in FactionRegistry.GetAllHQs())
            {
                if (owner == null) continue;
                if (++factions > 8) break;
                int count = territory.CopyFrontlineSites(owner.GetInstanceID(), sites);
                for (int i = 0; i < count; i++) candidates.Add((owner, sites[i], i));
            }
            // Round-robin factions so a small configured ceiling does not all go to the first HQ.
            candidates.Sort((a, b) =>
            {
                int order = a.rank.CompareTo(b.rank);
                return order != 0 ? order : a.owner.GetInstanceID().CompareTo(b.owner.GetInstanceID());
            });
            candidateIndex = rejected = 0;
            roadPass = true;
            logger?.LogInfo($"[TRENCHES] Placement scan: {candidates.Count} owned frontline candidates, {networks.Count} active networks.");
        }

        private void TrySeedInitialNetworks()
        {
            if (clearedSites.Count >= 64) { candidates.Clear(); return; }
            // One bounded terrain reserve/road search per frame, roads before fallback terrain.
            if (candidateIndex >= candidates.Count)
            {
                if (roadPass) { roadPass = false; candidateIndex = 0; }
                else
                {
                    logger?.LogInfo($"[TRENCHES] Placement finished: {networks.Count} networks; {rejected} candidates rejected (terrain, ownership, or spacing).");
                    candidates.Clear();
                    return;
                }
            }
            var candidate = candidates[candidateIndex++];
            if (candidate.owner == null) return;
            Vector3 center = new Vector3(candidate.site.X, 0, candidate.site.Z);
            if (roadPass && !placement.TryRoadSite(candidate.site, out center)) return;
            foreach (var cleared in clearedSites)
            {
                Vector3 delta = cleared - center; delta.y = 0;
                if (delta.sqrMagnitude < 1200f * 1200f) return;
            }
            foreach (var existing in networks)
            {
                float spacing = existing.OwnerHq == candidate.owner ? 1200f : 180f;
                Vector3 delta = existing.SeedCenter - center;
                delta.y = 0;
                if (delta.sqrMagnitude < spacing * spacing) return;
            }
            var network = CreateTrenchNetwork(center, new Vector3(candidate.site.ThreatX, 0, candidate.site.ThreatZ),
                candidate.owner, candidate.owner.name + "_Frontline");
            if (network == null) { rejected++; return; }
            logger?.LogInfo($"[TRENCHES] Seeded '{network.Name}' at global {network.Center}; road-preferred={roadPass}.");
            if (networks.Count >= Math.Min(MaximumActiveNetworks, settings.MaxTrenchNetworks.Value))
            {
                candidates.Clear();
            }
        }

        public TrenchNetwork CreateTrenchNetwork(Vector3 center, Vector3 threatDir, FactionHQ owner, string name)
        {
            if (!GameAccess.IsServer() || owner == null || territory == null ||
                !TrenchPlacement.ValidateReserve(center, owner.GetInstanceID(), territory, out Vector3 groundCenter)) return null;
            if (networks.Count >= Math.Min(MaximumActiveNetworks, settings?.MaxTrenchNetworks?.Value ?? MaximumActiveNetworks))
            {
                logger?.LogWarning("[TRENCHES] Cannot create network: maximum ceiling reached.");
                return null;
            }

            int id = nextNetworkId++;
            var net = new TrenchNetwork(id, name, owner, groundCenter, threatDir);
            net.PlacementValidator = p => Math.Abs(p.x - net.SeedCenter.x) <= 60f &&
                Math.Abs(p.z - net.SeedCenter.z) <= 60f &&
                territory.OwnsPosition(owner.GetInstanceID(), p.x, p.z) && TrenchPlacement.TryGround(p, out _);

            if (!TrenchGrowthSimulator.Seed(net, SnapToGround)) return null;
            var garrison = new TrenchGarrison(net);
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
            }
            catch (Exception ex)
            {
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

            if (anyChanged)
            {
                OnNetworksChanged?.Invoke();
            }
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
