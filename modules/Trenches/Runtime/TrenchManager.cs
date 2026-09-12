using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.Trenches.Visuals;
using BoscaliSummer.Framework.Lifecycle;
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
        private bool initialized;
        private int nextNetworkId = 1;

        public event Action OnNetworksChanged;

        public IReadOnlyList<TrenchNetwork> Networks => networks;

        public void Configure(TrenchesSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
        }

        public void ResetForScene()
        {
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
            nextSimulationTick = 0f;
            initialized = false;
            nextSeedAttemptAt = Time.unscaledTime + 2.5f;
        }

        private void OnDestroy()
        {
            ResetForScene();
        }

        private float nextSeedAttemptAt;

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value) return;

            if (!initialized)
            {
                if (Time.unscaledTime >= nextSeedAttemptAt)
                {
                    nextSeedAttemptAt = Time.unscaledTime + 2f;
                    TrySeedInitialNetworks();
                }
                return;
            }

            float now = Time.time;
            if (now >= nextSimulationTick)
            {
                float interval = Math.Max(15f, settings.GrowthIntervalSeconds.Value);
                nextSimulationTick = now + interval;
                TickSimulation();
            }
        }

        private void TrySeedInitialNetworks()
        {
            IEnumerable<Airbase> airbases = (FactionRegistry.airbaseLookup != null && FactionRegistry.airbaseLookup.Count > 0)
                ? (IEnumerable<Airbase>)FactionRegistry.airbaseLookup.Values
                : FindObjectsOfType<Airbase>();

            if (airbases == null) return;

            int seeded = 0;
            int maxToSeed = Math.Min(4, settings?.MaxTrenchNetworks?.Value ?? 4);

            foreach (Airbase ab in airbases)
            {
                if (ab == null || !ab.gameObject.scene.IsValid() || ab.AttachedAirbase) continue;

                FactionHQ owner = ab.CurrentHQ;
                if (owner == null) continue;

                // Position defensive strongpoints 350m forward from airbase perimeter
                Vector3 basePos = ab.center != null ? ab.center.position : ab.transform.position;
                Vector3 forward = ab.transform.forward;

                Vector3 strongpointPos = SnapToGround(basePos + forward * 350f);
                if (strongpointPos.y <= Datum.LocalSeaY + 2f) continue;

                Vector3 threatDir = forward;
                string baseName = !string.IsNullOrEmpty(ab.name) ? ab.name : "Airbase";
                CreateTrenchNetwork(strongpointPos, threatDir, owner, $"{baseName}_OuterDefense");
                seeded++;

                if (seeded >= maxToSeed) break;
            }

            if (seeded > 0)
            {
                initialized = true;
                logger?.LogInfo($"[TRENCHES] Dynamic trench manager initialized. Seeded {seeded} perimeter strongpoints.");
            }
        }

        public TrenchNetwork CreateTrenchNetwork(Vector3 center, Vector3 threatDir, FactionHQ owner, string name)
        {
            if (networks.Count >= Math.Min(MaximumActiveNetworks, settings?.MaxTrenchNetworks?.Value ?? MaximumActiveNetworks))
            {
                logger?.LogWarning("[TRENCHES] Cannot create network: maximum ceiling reached.");
                return null;
            }

            int id = nextNetworkId++;
            Vector3 groundCenter = SnapToGround(center);
            var net = new TrenchNetwork(id, name, owner, groundCenter, threatDir);

            // Seed Stage 0: 3 initial foxhole fighting positions in a wedge/line
            Vector3 side = Vector3.Cross(Vector3.up, threatDir.normalized).normalized;
            if (side.sqrMagnitude < 0.001f) side = Vector3.right;

            Vector3 pos0 = SnapToGround(groundCenter);
            Vector3 pos1 = SnapToGround(groundCenter - side * 18f + threatDir.normalized * 4f);
            Vector3 pos2 = SnapToGround(groundCenter + side * 18f + threatDir.normalized * 4f);

            net.AddNode(pos0, TrenchNodeType.Foxhole, TrenchStage.Stage0_Scrape);
            net.AddNode(pos1, TrenchNodeType.Foxhole, TrenchStage.Stage0_Scrape);
            net.AddNode(pos2, TrenchNodeType.Foxhole, TrenchStage.Stage0_Scrape);

            networks.Add(net);

            // Spawn visual chunk
            CreateVisualChunk(net);

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

            chunk.Initialize(net);
            visualChunks[net.Id] = chunk;
        }

        private void TickSimulation()
        {
            bool anyChanged = false;

            for (int i = 0; i < networks.Count; i++)
            {
                TrenchNetwork net = networks[i];
                bool changed = TrenchGrowthSimulator.AdvanceSimulation(net, SnapToGround);
                if (changed)
                {
                    anyChanged = true;
                    logger?.LogInfo($"[TRENCHES] Network '{net.Name}' advanced: {net.Stage} ({net.NodeCount} nodes, {net.EdgeCount} edges).");
                    if (visualChunks.TryGetValue(net.Id, out TrenchVisualChunk chunk) && chunk != null)
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
            if (Physics.Raycast(position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f,
                (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }
            return position;
        }
    }
}
