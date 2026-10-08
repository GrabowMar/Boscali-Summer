using System;
using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.FireAndDestruction.Configuration;
using BoscaliSummer.Modules.FireAndDestruction.Domain;
using BoscaliSummer.Modules.FireAndDestruction.Networking;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Fire
{
    internal sealed class ImpactFireManager : MonoBehaviour, ISceneService, IFireSuppressionService, IFoliageCover
    {
        private struct ImpactEvent
        {
            public GlobalPosition Position;
            public bool Explosive;
            public int Salt;
        }

        private struct ScorchMark
        {
            public GlobalPosition Position;
            public float MarkRadius;
            public float TreeClearBlastRadius;
            public float ScarDiameter;
            public FireFrontCell Cell;
        }

        private struct VehicleExplosionEvent
        {
            public GlobalPosition Position;
            public int InstanceId;
            public int Generation;
        }

        private struct ScheduledCookoff
        {
            public GlobalPosition Position;
            public int InstanceId;
            public int Generation;
            public float DueAt;
        }

        private sealed class FireSite
        {
            public GlobalPosition Position;
            public float Born;
            public float Expires;
            public float NextSmoke;
            public float NextSpread;
            public int Generation;
            public int SpreadAttempts;
            public bool Forest;
            public float ClusterScale;
            public Building BurningBuilding;
            public MapBuilding BurningMapBuilding;
            public FireVisualPool.Visual Visual;
            public FuelDepotSmokePool.Visual BuildingSmoke;
            public FireFrontCell Cell;
            public int TriedEdges;
            public int JoinedEdges;
            public bool SmokeOwner = true;
            public GlobalPosition SmokePosition;
            public Vector2 SmokeExtents;
        }

        public static ImpactFireManager Instance { get; private set; }

        private const int MaximumScorchQueue = 128;
        private const int MaximumImpacts = 256;
        // Bound on candidate probes per 4 Hz simulation tick: with 32 sites due at once the
        // unbudgeted scan ran six forest-index lookups and snaps each in a single frame.
        private const int MaximumSpreadProbesPerTick = 24;

        private readonly Queue<ImpactEvent> impacts = new Queue<ImpactEvent>(MaximumImpacts);
        private readonly Queue<VehicleExplosionEvent> vehicleExplosions = new Queue<VehicleExplosionEvent>(32);
        private readonly List<ScheduledCookoff> scheduledCookoffs = new List<ScheduledCookoff>(CookoffPolicy.MaxScheduled);
        private readonly WreckNotice[] wrecks = new WreckNotice[CookoffPolicy.MaxWreckNotices];
        private int wreckCount;
        private int wreckHead;
        private readonly Queue<ScorchMark> scorches = new Queue<ScorchMark>(MaximumScorchQueue);
        private readonly List<FireSite> fires = new List<FireSite>(32);
        private readonly Dictionary<long, float> cellCooldowns = new Dictionary<long, float>();
        private readonly Dictionary<int, float> vehicleCooldowns = new Dictionary<int, float>();
        private readonly List<long> expiredCooldownCells = new List<long>(128);
        private readonly Collider[] colliderBuffer = new Collider[32];
        private readonly RaycastHit[] roofHitBuffer = new RaycastHit[24];
        private static readonly List<Renderer> visibleRendererBuffer = new List<Renderer>(32);
        private readonly ForestIndex forestIndex = new ForestIndex();
        private readonly FireVisualPool visualPool = new FireVisualPool();
        private readonly FuelDepotSmokePool fuelDepotSmokePool = new FuelDepotSmokePool(32);
        private readonly BurnScarPool burnScars = new BurnScarPool();
        private Coroutine indexRoutine;
        private CommandBuffer burnMarkCommands;
        private ServiceRegistry services;
        private float nextTick;
        private int impactSequence;
        private int spreadBudget;
        private int spreadCursor;
        private bool topologyDirty;
        private readonly int[] componentRoots = new int[FireAndDestructionSettings.MaximumFireSites];
        private readonly Dictionary<long, int> activeForestCells = new Dictionary<long, int>(FireAndDestructionSettings.MaximumFireSites);
        private readonly Dictionary<long, float> forestCooldowns = new Dictionary<long, float>(256);
        private const int MaximumForestHistory = 4096;

        private static FireAndDestructionSettings Fire => Plugin.Settings.FireAndDestruction;
        private static DiagnosticSettings Diagnostics => Plugin.Settings.Diagnostics;

        private void Awake() => Instance = this;

        internal void Configure(ServiceRegistry serviceRegistry) => services = serviceRegistry;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            visualPool.Clear();
            fuelDepotSmokePool.Clear();
            burnScars.Clear();
            if (burnMarkCommands != null)
            {
                burnMarkCommands.Dispose();
                burnMarkCommands = null;
            }
        }

        public void ResetForScene()
        {
            impacts.Clear();
            vehicleExplosions.Clear();
            scheduledCookoffs.Clear();
            wreckCount = 0;
            wreckHead = 0;
            scorches.Clear();
            cellCooldowns.Clear();
            vehicleCooldowns.Clear();
            forestCooldowns.Clear();
            activeForestCells.Clear();
            spreadCursor = 0; topologyDirty = false;
            for (int i = 0; i < fires.Count; i++)
            {
                visualPool.Release(fires[i].Visual);
                fuelDepotSmokePool.Release(fires[i].BuildingSmoke);
            }
            fires.Clear();
            visibleRendererBuffer.Clear();
            visualPool.Clear();
            fuelDepotSmokePool.Clear();
            burnScars.Clear();
            TerrainProbeCache.Clear();
            impactSequence = 0;
            if (indexRoutine != null) StopCoroutine(indexRoutine);
            indexRoutine = StartCoroutine(RebuildIndexDelayed());
        }

        bool IFoliageCover.Ready => forestIndex.Ready;
        bool IFoliageCover.Contains(float x, float z) => forestIndex.Contains(x, z);

        public int ActiveFireCount => fires.Count;

        public int CopyWrecks(WreckNotice[] dest)
        {
            if (dest == null || dest.Length == 0) return 0;
            int n = dest.Length < wreckCount ? dest.Length : wreckCount;
            for (int i = 0; i < n; i++)
                dest[i] = wrecks[(wreckHead + i) % CookoffPolicy.MaxWreckNotices];
            return n;
        }

        internal void RecordWreck(GlobalPosition position, int instanceId)
        {
            Vector3 local = position.ToLocalPosition();
            float now = Time.timeSinceLevelLoad;
            var notice = new WreckNotice(local.x, local.y, local.z, instanceId, now);
            if (wreckCount < CookoffPolicy.MaxWreckNotices)
            {
                wrecks[(wreckHead + wreckCount) % CookoffPolicy.MaxWreckNotices] = notice;
                wreckCount++;
            }
            else
            {
                wrecks[wreckHead] = notice;
                wreckHead = (wreckHead + 1) % CookoffPolicy.MaxWreckNotices;
            }
        }

        public void SubmitImpact(GlobalPosition position, bool explosive, int salt)
        {
            if (!Fire.FiresEnabled.Value || !GameAccess.IsServer() || impacts.Count >= MaximumImpacts) return;
            impacts.Enqueue(new ImpactEvent
            {
                Position = position,
                Explosive = explosive,
                Salt = salt
            });
        }

        internal void SubmitVehicleExplosion(GlobalPosition position, int instanceId)
        {
            if (!Fire.FiresEnabled.Value || !GameAccess.IsServer() || vehicleExplosions.Count >= 32) return;
            float now = Time.timeSinceLevelLoad;
            if (vehicleCooldowns.TryGetValue(instanceId, out float retryAt) && now < retryAt) return;
            if (vehicleCooldowns.Count >= 128) vehicleCooldowns.Clear();
            vehicleCooldowns[instanceId] = now + 8f;
            vehicleExplosions.Enqueue(new VehicleExplosionEvent { Position = position, InstanceId = instanceId, Generation = 0 });
            ScheduleFollowUps(position, instanceId, now);
        }

        private void ScheduleFollowUps(GlobalPosition position, int instanceId, float born)
        {
            uint hash = CookoffPolicy.Hash(
                instanceId,
                Mathf.RoundToInt(position.x * 0.25f),
                Mathf.RoundToInt(position.z * 0.25f));
            int n = CookoffPolicy.FollowUps(hash);
            for (int g = 1; g <= n && scheduledCookoffs.Count < CookoffPolicy.MaxScheduled; g++)
            {
                scheduledCookoffs.Add(new ScheduledCookoff
                {
                    Position = position,
                    InstanceId = instanceId,
                    Generation = g,
                    DueAt = CookoffPolicy.DueAt(born, hash, g)
                });
            }
        }

        private IEnumerator RebuildIndexDelayed()
        {
            yield return null;
            yield return null;
            yield return forestIndex.Rebuild(Fire.ForestCellSize);
        }

        private void Update()
        {
            // Burn sites are the expensive path: each one paints the vanilla blast map,
            // queues at most one small tree-clearing blast and stamps a pooled soot decal.
            // One or two per frame bounds spikes from a spreading front. Every burn mark
            // drawn this frame shares one command buffer execution.
            int scorchBudget = scorches.Count > 8 ? 2 : 1;
            bool burnMarks = false;
            while (scorchBudget-- > 0 && scorches.Count > 0)
            {
                ScorchMark scorch = scorches.Dequeue();
                burnMarks |= QueueBurnMark(scorch);
                if (scorch.Cell != null) burnScars.StampForest(scorch.Position, scorch.Cell);
                else if (scorch.ScarDiameter > 0f)
                    burnScars.Stamp(scorch.Position, scorch.ScarDiameter);
            }
            if (burnMarks) FlushBurnMarks();

            int budget = 8;
            while (budget-- > 0 && impacts.Count > 0) ProcessImpact(impacts.Dequeue());

            // Vehicle losses are much rarer than projectile traces, but can arrive in
            // salvos. Drain at most one spatial query per frame to avoid a destruction
            // cascade turning into a physics spike.
            if (vehicleExplosions.Count > 0) ProcessVehicleExplosion(vehicleExplosions.Dequeue());
            else DrainScheduledCookoff();

            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.25f;
            spreadBudget = MaximumSpreadProbesPerTick;
            UpdateFires();
        }

        /// <summary>Records the burn mark into the shared buffer. Returns true when something was drawn.</summary>
        private bool QueueBurnMark(ScorchMark scorch)
        {
            // The gray ash bed is drawn straight into the vanilla blast map. DrawBlast paints
            // the persistent texture only; it never touches procedural trees. Tree removal
            // is a separate, deliberately small AddBlast, so a campfire no longer flattens a
            // 90 m stand around every stamp.
            BlastManager blast = SceneSingleton<BlastManager>.i;
            if (GameManager.IsHeadless || blast == null || blast.Texture == null) return false;
            float radius = Mathf.Max(scorch.MarkRadius, blast.worldSizeToResolution * 0.5f);
            if (burnMarkCommands == null)
                burnMarkCommands = new CommandBuffer { name = "BoscaliSummer.BurnMark" };
            blast.DrawBlast(burnMarkCommands, new BlastManager.DetailBlast(scorch.Position, radius));
            if (scorch.TreeClearBlastRadius > 0f)
                blast.AddBlast(scorch.Position, scorch.TreeClearBlastRadius);
            return true;
        }

        private void FlushBurnMarks()
        {
            if (burnMarkCommands == null) return;
            Graphics.ExecuteCommandBuffer(burnMarkCommands);
            burnMarkCommands.Clear();
        }

        private void ProcessImpact(ImpactEvent impact)
        {
            Vector3 local = impact.Position.ToLocalPosition();
            if (local.y < Datum.LocalSeaY + 0.5f) return;

            FindBuildings(local, out Building networkBuilding, out MapBuilding mapBuilding);
            bool eligible = mapBuilding != null || networkBuilding != null || forestIndex.Contains(impact.Position);
            if (!eligible) return;
            long cell = Deterministic.CellKey(impact.Position.x, impact.Position.z, 24f);
            float now = Time.timeSinceLevelLoad;
            if (cellCooldowns.TryGetValue(cell, out float retryAt) && now < retryAt) return;

            float chance = impact.Explosive
                ? Fire.ExplosiveIgnitionChance
                : Fire.BulletIgnitionChance;
            int x = Mathf.RoundToInt(impact.Position.x * 0.25f);
            int y = Mathf.RoundToInt(impact.Position.y * 0.25f);
            int z = Mathf.RoundToInt(impact.Position.z * 0.25f);
            uint hash = Deterministic.Hash(x, y, z,
                impact.Salt ^ (impact.Explosive ? 0x51f15e : 0x18b7) ^ impactSequence++);
            if (Deterministic.UnitFloat(hash) >= chance) return;
            // An aircraft hit, an air-to-air interception or a proximity airburst reports its
            // impact point in the sky. The forest test is two-dimensional, so without a ground
            // anchor check the flame column and its plume spawn mid-air.
            bool forest = mapBuilding == null && networkBuilding == null;
            GlobalPosition anchor;
            if (forest)
            {
                if (!TrySnapForestFireToGround(impact.Position, ImpactGroundSnapDrop, out anchor)) return;
            }
            else
            {
                anchor = SnapBuildingFireToRoof(impact.Position, networkBuilding, mapBuilding);
            }
            PruneCellCooldowns(now);
            cellCooldowns[cell] = now + Fire.FireCellCooldown;
            Ignite(anchor, now, forest,
                0, true, networkBuilding, mapBuilding);
        }

        private void DrainScheduledCookoff()
        {
            float now = Time.timeSinceLevelLoad;
            int pick = -1;
            for (int i = 0; i < scheduledCookoffs.Count; i++)
            {
                if (scheduledCookoffs[i].DueAt > now) continue;
                pick = i;
                break;
            }
            if (pick < 0) return;
            ScheduledCookoff due = scheduledCookoffs[pick];
            scheduledCookoffs.RemoveAt(pick);
            ProcessVehicleExplosion(new VehicleExplosionEvent
            {
                Position = due.Position,
                InstanceId = due.InstanceId,
                Generation = due.Generation
            });
        }

        private void ProcessVehicleExplosion(VehicleExplosionEvent explosion)
        {
            Vector3 local = explosion.Position.ToLocalPosition();
            if (local.y < Datum.LocalSeaY + 0.5f) return;

            const float radius = 78f;
            int count = Physics.OverlapSphereNonAlloc(local, radius, colliderBuffer);
            Building nearestNetwork = null;
            MapBuilding nearestMap = null;
            float nearestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider collider = colliderBuffer[i];
                if (collider == null) continue;
                Building network = collider.GetComponentInParent<Building>();
                if (network != null && !network.disabled)
                {
                    BuildingDefinition definition = network.definition as BuildingDefinition;
                    if (definition != null && definition.buildingType == BuildingType.CIV)
                    {
                        float d = (collider.ClosestPoint(local) - local).sqrMagnitude;
                        if (d < nearestSq) { nearestSq = d; nearestNetwork = network; nearestMap = null; }
                        continue;
                    }
                }
                MapBuilding map = collider.GetComponentInParent<MapBuilding>();
                if (map != null)
                {
                    float d = (collider.ClosestPoint(local) - local).sqrMagnitude;
                    if (d < nearestSq) { nearestSq = d; nearestMap = map; nearestNetwork = null; }
                }
            }
            bool forest = false;
            GlobalPosition forestAnchor = explosion.Position;
            if (nearestNetwork == null && nearestMap == null)
            {
                // A vehicle can burn out just outside the tree renderer's 18 m hit
                // radius. Probe a small deterministic ring so roadside/tree-line losses
                // still start a fire without doing another physics query.
                uint forestSeed = Deterministic.Hash(
                    Mathf.RoundToInt(explosion.Position.x * 0.25f),
                    Mathf.RoundToInt(explosion.Position.z * 0.25f),
                    explosion.InstanceId, 0x4f7a2c11);
                if (forestIndex.Contains(explosion.Position)) forestAnchor = explosion.Position;
                else
                {
                    float startAngle = Deterministic.UnitFloat(forestSeed) * Mathf.PI * 2f;
                    for (int probe = 0; probe < 8; probe++)
                    {
                        float angle = startAngle + probe * (Mathf.PI * 2f / 8f);
                        float distance = probe < 4 ? 28f : 52f;
                        GlobalPosition candidate = explosion.Position +
                            new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                        if (!forestIndex.Contains(candidate)) continue;
                        forestAnchor = candidate;
                        break;
                    }
                }
                forest = forestIndex.Contains(forestAnchor);
                if (!forest) return;
            }

            long cell = Deterministic.CellKey(explosion.Position.x, explosion.Position.z, 24f);
            float now = Time.timeSinceLevelLoad;
            bool followUp = explosion.Generation > 0;
            if (!followUp && cellCooldowns.TryGetValue(cell, out float retryAt) && now < retryAt) return;
            uint hash = Deterministic.Hash(
                Mathf.RoundToInt(explosion.Position.x * 0.25f),
                Mathf.RoundToInt(explosion.Position.z * 0.25f),
                explosion.InstanceId, 0x6f2e9a31);
            if (!followUp && Deterministic.UnitFloat(hash) >= Fire.VehicleExplosionIgnitionChance) return;

            GlobalPosition anchor;
            if (forest)
            {
                if (!TrySnapForestFireToGround(forestAnchor, ImpactGroundSnapDrop, out anchor)) return;
            }
            else
            {
                anchor = SnapBuildingFireToRoof(explosion.Position, nearestNetwork, nearestMap);
            }
            PruneCellCooldowns(now);
            cellCooldowns[cell] = now + Fire.FireCellCooldown;
            Ignite(anchor, now, forest, 0, true, nearestNetwork, nearestMap);
            if (Diagnostics.VerboseLogging.Value)
                Plugin.Logger.LogInfo(forest
                    ? $"Vehicle destruction ignited nearby forest at {anchor}."
                    : $"Vehicle destruction ignited nearby building at {anchor}.");
        }

        private void Ignite(GlobalPosition position, float now, bool forest, int generation = 0,
            bool mergeExisting = true, Building burningBuilding = null, MapBuilding burningMapBuilding = null)
        {
            long forestKey = 0;
            if (forest)
            {
                forestKey = FireFrontCell.Locate(position.x, position.z);
                FireFrontCell.Point seed = FireFrontCell.Seed((int)(forestKey >> 32), (int)forestKey);
                var candidate = new GlobalPosition(seed.X, position.y, seed.Z);
                if (!forestIndex.Contains(candidate) ||
                    !TrySnapForestFireToGround(candidate, ImpactGroundSnapDrop + FireFrontCell.Spacing, out position)) return;
            }
            if (mergeExisting || forest)
            {
                float mergeRadius = Fire.FireMergeRadius;
                float mergeSq = mergeRadius * mergeRadius;
                for (int i = 0; i < fires.Count; i++)
                {
                    if (fires[i].Forest != forest) continue;
                    // Keep distinct buildings as distinct sites so each one receives its
                    // own burnout/ruin transition instead of being lost in a merged fire.
                    if (burningBuilding != null && fires[i].BurningBuilding != null &&
                        fires[i].BurningBuilding != burningBuilding) continue;
                    if (burningMapBuilding != null && fires[i].BurningMapBuilding != null &&
                        fires[i].BurningMapBuilding != burningMapBuilding) continue;
                    if (forest ? fires[i].Cell?.Key == forestKey : (fires[i].Position - position).sqrMagnitude <= mergeSq)
                    {
                        fires[i].Expires = Mathf.Max(fires[i].Expires, now + Fire.FireLifetime * 0.65f);
                        if (forest)
                        {
                            // Refresh this cell only: impacts in adjacent cells start a real
                            // new section instead of inflating a distant point emitter.
                            forestCooldowns[forestKey] = fires[i].Expires + Fire.FireLifetime;
                            ModNet.BroadcastFire(
                                fires[i].Position, fires[i].Expires - now, true, fires[i].ClusterScale);
                            if (Diagnostics.VerboseLogging.Value)
                                Plugin.Logger.LogInfo($"Merged forest ignition into fire front at {fires[i].Position}; " +
                                    $"cell={forestKey}.");
                        }
                        if (burningBuilding != null) fires[i].BurningBuilding = burningBuilding;
                        if (burningMapBuilding != null)
                            fires[i].BurningMapBuilding = burningMapBuilding;
                        if (burningBuilding != null || burningMapBuilding != null)
                        {
                            fires[i].Forest = false;
                            fires[i].Visual?.Configure(false, fires[i].Position);
                        }
                        return;
                    }
                }
            }
            if (fires.Count >= Fire.MaxActiveFires) return;
            if (forest && forestCooldowns.TryGetValue(forestKey, out float burnedUntil) && now < burnedUntil) return;
            if (forest && forestCooldowns.Count >= MaximumForestHistory)
            {
                expiredCooldownCells.Clear();
                foreach (var entry in forestCooldowns) if (entry.Value <= now) expiredCooldownCells.Add(entry.Key);
                for (int i = 0; i < expiredCooldownCells.Count; i++) forestCooldowns.Remove(expiredCooldownCells[i]);
                if (forestCooldowns.Count >= MaximumForestHistory) return;
            }

            uint spreadSeed = Deterministic.Hash(
                Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z), generation, 0x2f6e2b1);
            float spreadJitter = 0.82f + Deterministic.UnitFloat(spreadSeed) * 0.36f;

            var site = new FireSite
            {
                Position = position,
                Born = now,
                Expires = now + Fire.FireLifetime,
                // Let the first flame phase establish itself before the large plume starts.
                NextSmoke = now + 1.75f,
                NextSpread = now + Fire.FireSpreadInterval * spreadJitter,
                Generation = generation,
                SpreadAttempts = 0,
                Forest = forest,
                ClusterScale = 1f,
                BurningBuilding = burningBuilding,
                BurningMapBuilding = burningMapBuilding,
                Visual = GameManager.IsHeadless ? null : visualPool.Acquire(position, forest),
                BuildingSmoke = null
            };
            // Late-join clients receive only position/lifetime. Resolve the local shell so
            // their plume uses the same narrow building profile as the host. Do not replace
            // a server-side target already supplied by ProcessImpact: overlap queries can
            // miss a shell when its colliders are still settling after a scene load.
            // Forest sites are not buildings, so they skip the query entirely — spread
            // creates most sites and every one of them used to run a wasted overlap.
            if (!forest && (site.BurningBuilding == null || site.BurningMapBuilding == null))
            {
                Vector3 local = position.ToLocalPosition();
                FindBuildings(local, out Building nearbyBuilding, out MapBuilding nearbyMapBuilding);
                if (site.BurningBuilding == null) site.BurningBuilding = nearbyBuilding;
                if (site.BurningMapBuilding == null) site.BurningMapBuilding = nearbyMapBuilding;
            }
            fires.Add(site);
            if (forest)
            {
                SetupForestCell(site);
                forestCooldowns[forestKey] = site.Expires + Fire.FireLifetime;
                QueueForestScorch(site);
            }
            else QueueScorch(position, 1f);
            ModNet.BroadcastFire(position, Fire.FireLifetime, forest, 1f);
            if (Diagnostics.VerboseLogging.Value) Plugin.Logger.LogInfo("Ignited fire at " + position);
        }

        private void SetupForestCell(FireSite site)
        {
            site.Cell = FireFrontCell.FromKey(FireFrontCell.Locate(site.Position.x, site.Position.z));
            TerrainProbeCache.TryProbe(site.Position, out _, out Vector3 normal);
            site.Visual?.SetForestCell(site.Cell, normal);
            site.SmokePosition = site.Position;
            site.SmokeExtents = Vector2.one * FireFrontCell.Spacing * 0.5f;
            topologyDirty = true;
        }

        internal void ReceiveIgnition(
            GlobalPosition position, float remainingLifetime, bool forest, float clusterScale)
        {
            if (GameAccess.IsServer() || remainingLifetime <= 0f) return;
            float now = Time.timeSinceLevelLoad;
            float original = Fire.FireLifetime;
            for (int i = 0; i < fires.Count; i++)
            {
                if (fires[i].Forest != forest || (forest
                    ? fires[i].Cell?.Key != FireFrontCell.Locate(position.x, position.z)
                    : (fires[i].Position - position).sqrMagnitude >= 16f)) continue;
                fires[i].Expires = Mathf.Max(fires[i].Expires, now + remainingLifetime);
                fires[i].Forest = forest;
                fires[i].ClusterScale = Mathf.Max(fires[i].ClusterScale, clusterScale);
                fires[i].Visual?.SetClusterScale(fires[i].ClusterScale);
                fires[i].BuildingSmoke?.SetForestClusterScale(fires[i].ClusterScale);
                return;
            }
            // A late join can receive fire snapshots before the host setting handshake.
            // Accept every site the host can legally own, even if this client's saved cap is lower.
            if (fires.Count >= FireAndDestructionSettings.MaximumFireSites) return;
            var site = new FireSite
            {
                Position = position,
                Born = now - Mathf.Clamp(original - remainingLifetime, 0f, original),
                Expires = now + Mathf.Min(remainingLifetime, original),
                NextSmoke = now,
                NextSpread = float.MaxValue,
                Generation = 0,
                SpreadAttempts = 0,
                Forest = forest,
                ClusterScale = Mathf.Clamp(clusterScale, 1f, 3f),
                BurningBuilding = null,
                BurningMapBuilding = null,
                Visual = GameManager.IsHeadless ? null : visualPool.Acquire(position, forest),
                BuildingSmoke = null
            };
            site.Visual?.SetClusterScale(site.ClusterScale);
            fires.Add(site);
            if (forest) { SetupForestCell(site); QueueForestScorch(site); }
            else QueueScorch(position, 1f);
        }

        internal void SendSnapshot(Mirage.INetworkPlayer player)
        {
            if (!GameAccess.IsServer() || player == null) return;
            float now = Time.timeSinceLevelLoad;
            for (int i = 0; i < fires.Count; i++)
                ModNet.SendFire(player, fires[i].Position, Mathf.Max(0f, fires[i].Expires - now),
                    fires[i].Forest, fires[i].ClusterScale);
        }

        private void UpdateFires()
        {
            Camera camera = GameManager.IsHeadless ? null : SceneSingleton<CameraStateManager>.i?.mainCamera ?? Camera.main;
            burnScars.UpdateCulling(camera);
            if (fires.Count == 0) return;
            if (topologyDirty) RefreshForestTopology();
            float now = Time.timeSinceLevelLoad;
            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null
                ? NetworkSceneSingleton<LevelInfo>.i.GetWind()
                : Vector3.zero;
            Vector3 camPos = camera != null ? camera.transform.position : Vector3.zero;
            int nearestA = -1, nearestB = -1, nearestC = -1;
            float distA = float.MaxValue, distB = float.MaxValue, distC = float.MaxValue;
            int smokeAcquireBudget = 1;

            const float FireCullDistanceSq = 15000f * 15000f;
            const float FireWakeDistanceSq = 14000f * 14000f;

            for (int i = fires.Count - 1; i >= 0; i--)
            {
                FireSite site = fires[i];
                if (now >= site.Expires)
                {
                    DemolishBurnedBuilding(site);
                    visualPool.Release(site.Visual);
                    fuelDepotSmokePool.Release(site.BuildingSmoke);
                    fires.RemoveAt(i);
                    topologyDirty = true;
                    continue;
                }

                float distToCamSq = camera != null
                    ? (camPos - site.Position.ToLocalPosition()).sqrMagnitude
                    : 0f;
                bool isSleeping = site.Visual?.Sleeping == true;
                bool shouldSleep = camera != null && distToCamSq > (isSleeping ? FireWakeDistanceSq : FireCullDistanceSq);

                site.Visual?.SetSleeping(shouldSleep);
                site.BuildingSmoke?.SetSleeping(shouldSleep);

                if (!shouldSleep)
                {
                    site.Visual?.SetPosition(site.Position);
                    site.Visual?.SetClusterScale(site.ClusterScale);
                    site.Visual?.SetPhase(
                        Mathf.Max(0f, now - site.Born),
                        Mathf.Clamp01((site.Expires - now) / Fire.FireLifetime),
                        wind);
                    if (site.BuildingSmoke != null)
                    {
                        site.BuildingSmoke.SetPosition(site.Forest ? site.SmokePosition : site.Position);
                        site.BuildingSmoke.SetForestClusterScale(site.ClusterScale);
                        site.BuildingSmoke.SetPhase(
                            Mathf.Max(0f, now - site.Born),
                            Mathf.Clamp01((site.Expires - now) / Fire.FireLifetime),
                            wind);
                    }
                }
                if (site.SmokeOwner && now >= site.NextSmoke)
                {
                    // A network client can receive an ignition before the building's
                    // colliders finish loading. Retry the association lazily so it still
                    // gets the narrow tall building plume instead of the forest profile.
                    if (!site.Forest && site.BurningBuilding == null && site.BurningMapBuilding == null)
                    {
                        Vector3 local = site.Position.ToLocalPosition();
                        FindBuildings(local, out site.BurningBuilding, out site.BurningMapBuilding);
                    }
                    bool buildingFire = site.BurningBuilding != null || site.BurningMapBuilding != null;
                    if (site.BuildingSmoke == null && !GameManager.IsHeadless && smokeAcquireBudget > 0 && !shouldSleep)
                    {
                        // Both urban and forest sites now use smoke-only copies of the actual
                        // Fuel Depot destruction prefab. Forest fires get a wider, windier
                        // three-source profile rather than the legacy ContactSmoke catalogue.
                        Vector2 halfExtents = buildingFire
                            ? GetBuildingHalfExtents(site.BurningBuilding, site.BurningMapBuilding)
                            : site.SmokeExtents;
                        site.BuildingSmoke = fuelDepotSmokePool.Acquire(
                            site.Forest ? site.SmokePosition : site.Position, halfExtents,
                            site.Forest
                                ? FuelDepotSmokePool.SmokeProfile.Forest
                                : FuelDepotSmokePool.SmokeProfile.Building);
                        if (site.BuildingSmoke != null) smokeAcquireBudget--;
                        site.BuildingSmoke?.SetForestClusterScale(site.ClusterScale);
                    }
                    site.NextSmoke = site.BuildingSmoke == null ? now + 2.4f : float.MaxValue;
                }
                if (camera == null || site.Visual == null || shouldSleep) continue;
                float d = distToCamSq;
                if (d < distA)
                {
                    distC = distB; nearestC = nearestB;
                    distB = distA; nearestB = nearestA;
                    distA = d; nearestA = i;
                }
                else if (d < distB)
                {
                    distC = distB; nearestC = nearestB;
                    distB = d; nearestB = i;
                }
                else if (d < distC) { distC = d; nearestC = i; }
            }

            for (int i = 0; i < fires.Count; i++)
                fires[i].Visual?.SetLight(i == nearestA || i == nearestB || i == nearestC);
            // Round-robin keeps newly added/high-index sites from monopolising the probe
            // budget. Only sites present at tick start may spread in this tick.
            int count = fires.Count;
            if (count > 0)
            {
                int start = spreadCursor % count, processed = 0;
                for (; processed < count && spreadBudget > 0; processed++)
                    TrySpread(fires[(start + processed) % count], now, wind);
                spreadCursor = (start + Mathf.Max(1, processed)) % count;
            }
            if (topologyDirty) RefreshForestTopology();
        }

        private void RefreshForestTopology()
        {
            topologyDirty = false;
            activeForestCells.Clear();
            for (int i = 0; i < fires.Count; i++)
            {
                componentRoots[i] = i;
                if (fires[i].Cell != null) activeForestCells[fires[i].Cell.Key] = i;
            }
            for (int i = 0; i < fires.Count; i++)
            {
                FireSite site = fires[i];
                if (site.Cell == null) continue;
                int mask = 0;
                for (int edge = 0; edge < site.Cell.Count; edge++)
                {
                    if (!activeForestCells.TryGetValue(site.Cell.Neighbours[edge], out int j)) continue;
                    mask |= 1 << edge;
                    int a = ComponentRoot(i), b = ComponentRoot(j);
                    if (a != b) componentRoots[Mathf.Max(a, b)] = Mathf.Min(a, b);
                }
                // A burnt-out neighbour leaves consumed ground, not a new fuel boundary.
                // Remember observed joins so the fire does not turn back into its ashes.
                site.JoinedEdges |= mask;
                site.Visual?.SetFrontEdges(site.JoinedEdges);
            }
            for (int i = 0; i < fires.Count; i++)
            {
                FireSite site = fires[i];
                if (site.Cell == null) continue;
                site.SmokeOwner = ComponentRoot(i) == i;
                if (!site.SmokeOwner)
                {
                    fuelDepotSmokePool.Release(site.BuildingSmoke); site.BuildingSmoke = null;
                    continue;
                }
                Vector3 sum = Vector3.zero;
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                int members = 0;
                for (int j = 0; j < fires.Count; j++)
                {
                    if (ComponentRoot(j) != i || fires[j].Cell == null) continue;
                    FireSite member = fires[j]; sum += member.Position.ToLocalPosition(); members++;
                    for (int v = 0; v < member.Cell.Count; v++)
                    {
                        float x = member.Position.x + member.Cell.Vertices[v].X;
                        float z = member.Position.z + member.Cell.Vertices[v].Z;
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                        minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
                    }
                }
                site.SmokePosition = (sum / Mathf.Max(1, members)).ToGlobalPosition();
                site.SmokeExtents = new Vector2((maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f);
                site.ClusterScale = Mathf.Clamp(1f + Mathf.Sqrt(members - 1) * 0.35f, 1f, 3f);
                site.BuildingSmoke?.SetForestClusterScale(site.ClusterScale);
                site.BuildingSmoke?.SetForestFootprint(site.SmokeExtents);
                if (site.BuildingSmoke == null) site.NextSmoke = Time.timeSinceLevelLoad;
            }
        }

        private int ComponentRoot(int index)
        {
            while (componentRoots[index] != index) index = componentRoots[index];
            return index;
        }

        private void TrySpread(FireSite source, float now, Vector3 wind)
        {
            if (source.Cell == null || !Fire.FireSpreadEnabled || !GameAccess.IsServer() ||
                fires.Count >= Fire.MaxActiveFires || source.Generation >= Fire.FireSpreadGenerations ||
                source.SpreadAttempts >= 3 || now < source.NextSpread || spreadBudget <= 0) return;

            // Exhaust invalid/burnt edges without spending a successful advance. The mask
            // bounds every cell to one probe per edge; no six-point retry spiral per tick.
            for (int option = 0; option < source.Cell.Count && spreadBudget > 0; option++)
            {
                int best = -1; float score = float.MinValue;
                for (int edge = 0; edge < source.Cell.Count; edge++)
                {
                    if ((source.TriedEdges & (1 << edge)) != 0) continue;
                    float value = source.Cell.EdgeScore(edge, wind.x, wind.z, source.SpreadAttempts);
                    if (value > score) { best = edge; score = value; }
                }
                if (best < 0) { source.NextSpread = float.MaxValue; return; }
                source.TriedEdges |= 1 << best;
                long key = source.Cell.Neighbours[best];
                if (forestCooldowns.TryGetValue(key, out float burnedUntil) && now < burnedUntil) continue;
                spreadBudget--;
                FireFrontCell.Point seed = FireFrontCell.Seed((int)(key >> 32), (int)key);
                FireFrontCell.Point a = source.Cell.Vertices[best], b = source.Cell.Vertices[(best + 1) % source.Cell.Count];
                float edgeX = source.Position.x + (a.X + b.X) * 0.5f;
                float edgeZ = source.Position.z + (a.Z + b.Z) * 0.5f;
                // Require fuel on the crossing as well as in the destination. This makes
                // gaps, shorelines and cleared strips stop the front instead of being jumped.
                if (!forestIndex.ContainsWithin(edgeX, edgeZ, 8f) ||
                    !forestIndex.ContainsWithin((edgeX + seed.X) * 0.5f, (edgeZ + seed.Z) * 0.5f, 12f) ||
                    !forestIndex.Contains(seed.X, seed.Z)) continue;
                var candidate = new GlobalPosition(seed.X, source.Position.y, seed.Z);
                if (!TrySnapForestFireToGround(candidate, FireFrontCell.Spacing * 1.5f, out GlobalPosition grounded)) continue;
                float slope = (grounded.y - source.Position.y) / FireFrontCell.Spacing;
                if (Mathf.Abs(slope) > 1.2f) continue;
                int before = fires.Count;
                Ignite(grounded, now, true, source.Generation + 1, false);
                if (fires.Count == before) return;
                source.SpreadAttempts++;
                float uphill = Mathf.Clamp(slope, -0.6f, 0.6f);
                source.NextSpread = now + Fire.FireSpreadInterval * Mathf.Clamp(1f - score * 0.22f - uphill * 0.3f, 0.55f, 1.5f);
                return;
            }
            // If budget ran out, preserve the still-untried edges for the next fair tick.
            if (spreadBudget > 0) source.NextSpread = now + Fire.FireSpreadInterval;
        }

        private void PruneCellCooldowns(float now)
        {
            // Successful ignitions are sparse, so prune only when the dictionary becomes
            // material. This bounds memory in multi-hour missions without adding a timer or
            // a per-frame dictionary walk.
            if (cellCooldowns.Count < 256) return;
            expiredCooldownCells.Clear();
            foreach (KeyValuePair<long, float> entry in cellCooldowns)
                if (entry.Value <= now) expiredCooldownCells.Add(entry.Key);
            for (int i = 0; i < expiredCooldownCells.Count; i++)
                cellCooldowns.Remove(expiredCooldownCells[i]);
        }

        private void QueueScorch(GlobalPosition position, float clusterScale)
        {
            if (scorches.Count >= MaximumScorchQueue) return;
            float scale = Mathf.Clamp(clusterScale, 1f, 3f);
            scorches.Enqueue(new ScorchMark
            {
                Position = position,
                MarkRadius = FireScorchPolicy.BurnMarkRadius(scale),
                TreeClearBlastRadius = FireScorchPolicy.TreeClearBlastRadius(scale),
                ScarDiameter = FireScorchPolicy.ScarDiameter(scale)
            });
        }

        private void QueueForestScorch(FireSite site)
        {
            if (scorches.Count >= MaximumScorchQueue || site.Cell == null) return;
            // One shared-edge polygon, one small tree-clear and one coarse native ash stamp.
            // No lobes on ignition/merge/burnout and no repeated persistent decals.
            scorches.Enqueue(new ScorchMark
            {
                Position = site.Position, Cell = site.Cell,
                MarkRadius = FireScorchPolicy.BurnMarkRadius(1f),
                TreeClearBlastRadius = FireScorchPolicy.TreeClearBlastRadius(1f)
            });
        }

        private void FindBuildings(
            Vector3 position, out Building networkBuilding, out MapBuilding mapBuilding)
        {
            networkBuilding = null;
            mapBuilding = null;
            int count = Physics.OverlapSphereNonAlloc(position, 5f, colliderBuffer);
            for (int i = 0; i < count; i++)
            {
                Collider collider = colliderBuffer[i];
                if (collider == null) continue;
                if (mapBuilding == null)
                    mapBuilding = collider.GetComponentInParent<MapBuilding>();
                if (networkBuilding == null)
                {
                    Building candidate = collider.GetComponentInParent<Building>();
                    if (candidate != null && !candidate.disabled)
                    {
                        BuildingDefinition definition = candidate.definition as BuildingDefinition;
                        if (definition != null && definition.buildingType == BuildingType.CIV)
                            networkBuilding = candidate;
                    }
                }
                if (networkBuilding != null && mapBuilding != null) return;
            }
        }

        // A reported impact on a slope, canopy or vehicle sits within a couple of metres of
        // the surface. Anything farther below is an air burst that must simply not start a
        // ground fire. Forest spread reuses the same probe with one spread step of slack, so
        // the front can still walk down a steep valley side.
        private const float ImpactGroundSnapDrop = 30f;

        private static bool TrySnapForestFireToGround(
            GlobalPosition position, float maxDrop, out GlobalPosition grounded)
        {
            grounded = position;
            Vector3 local = position.ToLocalPosition();
            // Probe from well above the reported point: the fixed 260 m ray used to miss the
            // ground under high air bursts and returned the air position unchanged, which is
            // how a flame column ended up hanging in the sky. Failing closed is the fix.
            // The shared cache means repeated candidates and decal stamps reuse one cast.
            if (!TerrainProbeCache.TryProbe(position, out GlobalPosition point, out _))
                return false;
            if (!FireGroundSnapPolicy.CanAnchor(
                    point.ToLocalPosition().y, local.y, Datum.LocalSeaY, maxDrop)) return false;
            grounded = point + Vector3.up * 0.2f;
            return true;
        }

        private GlobalPosition SnapBuildingFireToRoof(
            GlobalPosition position, Building networkBuilding, MapBuilding mapBuilding)
        {
            GameObject shell = networkBuilding != null
                ? networkBuilding.gameObject
                : mapBuilding != null ? mapBuilding.gameObject : null;
            if (shell == null) return position;

            Bounds bounds;
            if (!TryGetVisibleBuildingBounds(shell, out bounds)) return position;

            Vector3 local = position.ToLocalPosition();
            float insetX = Mathf.Min(3f, bounds.extents.x * 0.28f);
            float insetZ = Mathf.Min(3f, bounds.extents.z * 0.28f);
            local.x = Mathf.Clamp(local.x, bounds.min.x + insetX, bounds.max.x - insetX);
            local.z = Mathf.Clamp(local.z, bounds.min.z + insetZ, bounds.max.z - insetZ);
            // Renderer bounds often include tall hidden LOD or destruction geometry. Cast
            // through the selected x/z and accept only colliders belonging to this building,
            // so a fire on a lower annex sits on that annex instead of floating at the
            // tallest aggregate bound.
            float roofY = float.NegativeInfinity;
            Vector3 origin = new Vector3(local.x, bounds.max.y + 8f, local.z);
            int hitCount = Physics.RaycastNonAlloc(
                origin, Vector3.down, roofHitBuffer, bounds.size.y + 24f,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = roofHitBuffer[i].collider;
                if (collider == null) continue;
                Transform hitTransform = collider.transform;
                if (hitTransform != shell.transform && !hitTransform.IsChildOf(shell.transform)) continue;
                if (roofHitBuffer[i].point.y > roofY) roofY = roofHitBuffer[i].point.y;
            }
            local.y = (float.IsNegativeInfinity(roofY) ? bounds.max.y : roofY) + 0.06f;
            return local.ToGlobalPosition();
        }

        private static Vector2 GetBuildingHalfExtents(
            Building networkBuilding, MapBuilding mapBuilding)
        {
            GameObject shell = networkBuilding != null
                ? networkBuilding.gameObject
                : mapBuilding != null ? mapBuilding.gameObject : null;
            if (shell == null) return new Vector2(8f, 8f);

            Bounds bounds;
            bool found = TryGetVisibleBuildingBounds(shell, out bounds);
            return found
                ? new Vector2(Mathf.Max(3f, bounds.extents.x), Mathf.Max(3f, bounds.extents.z))
                : new Vector2(8f, 8f);
        }

        private static bool TryGetVisibleBuildingBounds(GameObject shell, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (shell == null) return false;
            shell.GetComponentsInChildren(false, visibleRendererBuffer);
            bool found = false;
            for (int i = 0; i < visibleRendererBuffer.Count; i++)
            {
                Renderer renderer = visibleRendererBuffer[i];
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer ||
                    !renderer.gameObject.activeInHierarchy) continue;
                string name = renderer.gameObject.name;
                if (name.IndexOf("rubble", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("wreck", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("destroyed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ruin", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            visibleRendererBuffer.Clear();
            return found;
        }

        private void DemolishBurnedBuilding(FireSite site)
        {
            if (!Fire.DemolishUnoccupiedBuildings.Value || !GameAccess.IsServer()) return;

            Vector2 ruinExtents = GetBuildingHalfExtents(
                site.BurningBuilding, site.BurningMapBuilding);
            GlobalPosition ruinAnchor = GetRuinAnchor(
                site.Position, site.BurningBuilding, site.BurningMapBuilding);
            bool demolished = false;

            Building building = site.BurningBuilding;
            if (building != null && !building.disabled)
            {
                // NetworkHQ is the game's occupancy/ownership state. A building with a
                // faction owner is left standing so an enemy-held structure is not silently
                // converted into a ruin while its garrison is still active.
                if (building.NetworkHQ != null)
                {
                    if (Diagnostics.VerboseLogging.Value)
                        Plugin.Logger.LogInfo("Fire burned out in occupied building; preserving " + building.unitName + ".");
                }
                else
                {
                    building.Networkdisabled = true;
                    demolished = true;
                    if (Diagnostics.VerboseLogging.Value)
                        Plugin.Logger.LogInfo("Fire burned out; demolished unoccupied building " + building.unitName + ".");
                }
            }

            MapBuilding mapBuilding = site.BurningMapBuilding;
            bool occupied = mapBuilding != null && mapBuilding.gameObject &&
                services != null &&
                services.TryGet(out IBuildingOccupancy occupancy) &&
                occupancy.IsOccupied(mapBuilding.gameObject);
            if (mapBuilding != null && mapBuilding.gameObject && !occupied)
            {
                // MapBuildingSet.DestroyBuilding is reached through TakeDamage, which keeps
                // the vanilla synchronized ruin path instead of destroying only the host copy.
                mapBuilding.TakeDamage(0f, 0f, 0f, 0f, 100000f, PersistentID.None);
                demolished = true;
                if (Diagnostics.VerboseLogging.Value)
                    Plugin.Logger.LogInfo("Fire burned out; demolished unoccupied map building " + mapBuilding.name + ".");
            }

            if (demolished)
                RuinAftermathManager.Instance?.RegisterRuin(
                    ruinAnchor, ruinExtents, 0f, true, true);
        }

        private static GlobalPosition GetRuinAnchor(
            GlobalPosition fallback, Building networkBuilding, MapBuilding mapBuilding)
        {
            GameObject shell = networkBuilding != null
                ? networkBuilding.gameObject
                : mapBuilding != null ? mapBuilding.gameObject : null;
            if (shell == null) return fallback;
            Bounds bounds;
            if (!TryGetVisibleBuildingBounds(shell, out bounds)) return fallback;
            Vector3 local = fallback.ToLocalPosition();
            local.x = Mathf.Clamp(local.x, bounds.min.x, bounds.max.x);
            local.z = Mathf.Clamp(local.z, bounds.min.z, bounds.max.z);
            local.y = bounds.min.y + 0.5f;
            return local.ToGlobalPosition();
        }
    }
}
