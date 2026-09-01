using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Core;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Effects;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    internal sealed class ImpactFireManager : MonoBehaviour, ISceneService
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
            public float RadiusScale;
        }

        private struct VehicleExplosionEvent
        {
            public GlobalPosition Position;
            public int InstanceId;
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
        }

        public static ImpactFireManager Instance { get; private set; }

        private readonly Queue<ImpactEvent> impacts = new Queue<ImpactEvent>(256);
        private readonly Queue<VehicleExplosionEvent> vehicleExplosions = new Queue<VehicleExplosionEvent>(32);
        private readonly Queue<ScorchMark> scorches = new Queue<ScorchMark>(64);
        private readonly List<FireSite> fires = new List<FireSite>(24);
        private readonly Dictionary<long, float> cellCooldowns = new Dictionary<long, float>();
        private readonly Dictionary<int, float> vehicleCooldowns = new Dictionary<int, float>();
        private readonly List<long> expiredCooldownCells = new List<long>(128);
        private readonly Collider[] colliderBuffer = new Collider[32];
        private readonly RaycastHit[] roofHitBuffer = new RaycastHit[24];
        private readonly ForestIndex forestIndex = new ForestIndex();
        private readonly FireVisualPool visualPool = new FireVisualPool();
        private readonly FuelDepotSmokePool fuelDepotSmokePool = new FuelDepotSmokePool();
        private Coroutine indexRoutine;
        private ServiceRegistry services;
        private float nextTick;
        private int impactSequence;

        private void Awake() => Instance = this;

        internal void Configure(ServiceRegistry serviceRegistry) => services = serviceRegistry;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            visualPool.Clear();
            fuelDepotSmokePool.Clear();
        }

        public void ResetForScene()
        {
            impacts.Clear();
            vehicleExplosions.Clear();
            scorches.Clear();
            cellCooldowns.Clear();
            vehicleCooldowns.Clear();
            for (int i = 0; i < fires.Count; i++)
            {
                visualPool.Release(fires[i].Visual);
                fuelDepotSmokePool.Release(fires[i].BuildingSmoke);
            }
            fires.Clear();
            visualPool.Clear();
            fuelDepotSmokePool.Clear();
            impactSequence = 0;
            if (indexRoutine != null) StopCoroutine(indexRoutine);
            indexRoutine = StartCoroutine(RebuildIndexDelayed());
        }

        public void SubmitImpact(GlobalPosition position, bool explosive, int salt)
        {
            if (!Plugin.Settings.FiresEnabled.Value || !IsServer() || impacts.Count >= 256) return;
            impacts.Enqueue(new ImpactEvent
            {
                Position = position,
                Explosive = explosive,
                Salt = salt
            });
        }

        internal void SubmitVehicleExplosion(GlobalPosition position, int instanceId)
        {
            if (!Plugin.Settings.FiresEnabled.Value || !IsServer() || vehicleExplosions.Count >= 32) return;
            float now = Time.timeSinceLevelLoad;
            if (vehicleCooldowns.TryGetValue(instanceId, out float retryAt) && now < retryAt) return;
            if (vehicleCooldowns.Count >= 128) vehicleCooldowns.Clear();
            vehicleCooldowns[instanceId] = now + 8f;
            vehicleExplosions.Enqueue(new VehicleExplosionEvent { Position = position, InstanceId = instanceId });
        }

        private IEnumerator RebuildIndexDelayed()
        {
            yield return null;
            yield return null;
            yield return forestIndex.Rebuild(Plugin.Settings.ForestCellSize);
        }

        private void Update()
        {
            // The game's detail renderer drains this queue and applies both the persistent
            // blast texture and procedural-tree removal. One call per frame bounds spikes.
            if (scorches.Count > 0 && SceneSingleton<BlastManager>.i != null)
            {
                // Keep the vanilla blast-map path (the same gray ash tint used by nuclear
                // impacts), but make the tree-clear circle less expansive than a full blast.
                ScorchMark scorch = scorches.Dequeue();
                float radius = Mathf.Max(32f,
                    Plugin.Settings.ScorchRadius * Plugin.Settings.ScorchRadiusScale) *
                    scorch.RadiusScale;
                SceneSingleton<BlastManager>.i.AddBlast(scorch.Position, radius);
            }

            int budget = 8;
            while (budget-- > 0 && impacts.Count > 0) ProcessImpact(impacts.Dequeue());

            // Vehicle losses are much rarer than projectile traces, but can arrive in
            // salvos. Drain at most one spatial query per frame to avoid a destruction
            // cascade turning into a physics spike.
            if (vehicleExplosions.Count > 0) ProcessVehicleExplosion(vehicleExplosions.Dequeue());

            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.25f;
            UpdateFires();
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
                ? Plugin.Settings.ExplosiveIgnitionChance
                : Plugin.Settings.BulletIgnitionChance;
            int x = Mathf.RoundToInt(impact.Position.x * 0.25f);
            int y = Mathf.RoundToInt(impact.Position.y * 0.25f);
            int z = Mathf.RoundToInt(impact.Position.z * 0.25f);
            uint hash = Deterministic.Hash(x, y, z,
                impact.Salt ^ (impact.Explosive ? 0x51f15e : 0x18b7) ^ impactSequence++);
            if (Deterministic.UnitFloat(hash) >= chance) return;
            PruneCellCooldowns(now);
            cellCooldowns[cell] = now + Plugin.Settings.FireCellCooldown;
            bool forest = mapBuilding == null && networkBuilding == null;
            GlobalPosition anchor = forest
                ? SnapForestFireToGround(impact.Position)
                : SnapBuildingFireToRoof(impact.Position, networkBuilding, mapBuilding);
            Ignite(anchor, now, forest,
                0, true, networkBuilding, mapBuilding);
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
            if (cellCooldowns.TryGetValue(cell, out float retryAt) && now < retryAt) return;
            uint hash = Deterministic.Hash(
                Mathf.RoundToInt(explosion.Position.x * 0.25f),
                Mathf.RoundToInt(explosion.Position.z * 0.25f),
                explosion.InstanceId, 0x6f2e9a31);
            if (Deterministic.UnitFloat(hash) >= Plugin.Settings.VehicleExplosionIgnitionChance) return;
            PruneCellCooldowns(now);
            cellCooldowns[cell] = now + Plugin.Settings.FireCellCooldown;

            GlobalPosition anchor = forest
                ? SnapForestFireToGround(forestAnchor)
                : SnapBuildingFireToRoof(explosion.Position, nearestNetwork, nearestMap);
            Ignite(anchor, now, forest, 0, true, nearestNetwork, nearestMap);
            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo(forest
                    ? $"Vehicle destruction ignited nearby forest at {anchor}."
                    : $"Vehicle destruction ignited nearby building at {anchor}.");
        }

        private void Ignite(GlobalPosition position, float now, bool forest, int generation = 0,
            bool mergeExisting = true, Building burningBuilding = null, MapBuilding burningMapBuilding = null)
        {
            if (mergeExisting)
            {
                // Forest spread candidates are deliberately wind-biased and can be farther
                // apart than the ordinary impact merge radius. Tie the forest window to the
                // configured spread distance so those candidates feed one expanding front
                // instead of leaving a row of unrelated columns. Cap it to avoid merging
                // separate stands across an entire valley on heavily tuned configs.
                float mergeRadius = forest
                    ? Mathf.Min(240f, Mathf.Max(
                        Plugin.Settings.FireMergeRadius * 1.8f,
                        Plugin.Settings.FireSpreadDistance * 1.36f))
                    : Plugin.Settings.FireMergeRadius;
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
                    if ((fires[i].Position - position).sqrMagnitude <= mergeSq)
                    {
                        fires[i].Expires = Mathf.Max(fires[i].Expires, now + Plugin.Settings.FireLifetime * 0.65f);
                        if (forest)
                        {
                            float sourceDistance = (fires[i].Position - position).magnitude;
                            // Scale from the actual front advance, not merely the number of
                            // merged events. A 70 m downwind spot fire must visibly widen the
                            // connected flame bed or it reads as an unchanged point fire.
                            float distanceScale = Mathf.Clamp(1f + sourceDistance / 62f, 1f, 2.65f);
                            fires[i].ClusterScale = Mathf.Min(3f,
                                Mathf.Max(fires[i].ClusterScale + 0.12f, distanceScale));
                            fires[i].Visual?.SetClusterScale(fires[i].ClusterScale);
                            fires[i].BuildingSmoke?.SetForestClusterScale(fires[i].ClusterScale);
                            QueueForestScorch(position);
                            ModNet.BroadcastFire(
                                fires[i].Position, fires[i].Expires - now, true, fires[i].ClusterScale);
                            if (Plugin.Settings.VerboseLogging.Value)
                                Plugin.Logger.LogInfo($"Merged forest ignition into fire front at {fires[i].Position}; " +
                                    $"source distance={(fires[i].Position - position).magnitude:0.0}m, " +
                                    $"cluster scale={fires[i].ClusterScale:0.00}.");
                        }
                        if (burningBuilding != null) fires[i].BurningBuilding = burningBuilding;
                        if (burningMapBuilding != null)
                        {
                            fires[i].BurningMapBuilding = burningMapBuilding;
                            ApplyBurningBuildingDamage(burningMapBuilding);
                        }
                        if (burningBuilding != null || burningMapBuilding != null)
                        {
                            fires[i].Forest = false;
                            fires[i].Visual?.Configure(false, fires[i].Position);
                        }
                        return;
                    }
                }
            }
            if (fires.Count >= Plugin.Settings.MaxActiveFires) return;

            uint spreadSeed = Deterministic.Hash(
                Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z), generation, 0x2f6e2b1);
            float spreadJitter = 0.82f + Deterministic.UnitFloat(spreadSeed) * 0.36f;

            var site = new FireSite
            {
                Position = position,
                Born = now,
                Expires = now + Plugin.Settings.FireLifetime,
                // Let the first flame phase establish itself before the large plume starts.
                NextSmoke = now + 1.75f,
                NextSpread = now + Plugin.Settings.FireSpreadInterval * spreadJitter,
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
            Vector3 local = position.ToLocalPosition();
            if (site.BurningBuilding == null || site.BurningMapBuilding == null)
            {
                FindBuildings(local, out Building nearbyBuilding, out MapBuilding nearbyMapBuilding);
                if (site.BurningBuilding == null) site.BurningBuilding = nearbyBuilding;
                if (site.BurningMapBuilding == null) site.BurningMapBuilding = nearbyMapBuilding;
            }
            ApplyBurningBuildingDamage(site.BurningMapBuilding);
            fires.Add(site);
            if (forest) QueueForestScorch(position);
            else QueueScorch(position, 1f);
            ModNet.BroadcastFire(position, Plugin.Settings.FireLifetime, forest, 1f);
            if (Plugin.Settings.VerboseLogging.Value) Plugin.Logger.LogInfo("Ignited fire at " + position);
        }

        internal void ReceiveIgnition(
            GlobalPosition position, float remainingLifetime, bool forest, float clusterScale)
        {
            if (IsServer() || remainingLifetime <= 0f) return;
            float now = Time.timeSinceLevelLoad;
            float original = Plugin.Settings.FireLifetime;
            for (int i = 0; i < fires.Count; i++)
            {
                if ((fires[i].Position - position).sqrMagnitude >= 16f) continue;
                fires[i].Expires = Mathf.Max(fires[i].Expires, now + remainingLifetime);
                fires[i].Forest = forest;
                fires[i].ClusterScale = Mathf.Max(fires[i].ClusterScale, clusterScale);
                fires[i].Visual?.Configure(forest, fires[i].Position);
                fires[i].Visual?.SetClusterScale(fires[i].ClusterScale);
                fires[i].BuildingSmoke?.SetForestClusterScale(fires[i].ClusterScale);
                return;
            }
            if (fires.Count >= Plugin.Settings.MaxActiveFires) return;
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
            if (forest) QueueForestScorch(position);
            else QueueScorch(position, 1f);
        }

        internal void SendSnapshot(Mirage.INetworkPlayer player)
        {
            if (!IsServer() || player == null) return;
            float now = Time.timeSinceLevelLoad;
            for (int i = 0; i < fires.Count; i++)
                ModNet.SendFire(player, fires[i].Position, Mathf.Max(0f, fires[i].Expires - now),
                    fires[i].Forest, fires[i].ClusterScale);
        }

        private void UpdateFires()
        {
            float now = Time.timeSinceLevelLoad;
            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null
                ? NetworkSceneSingleton<LevelInfo>.i.GetWind()
                : Vector3.zero;
            Camera camera = Camera.main;
            int nearestA = -1, nearestB = -1, nearestC = -1;
            float distA = float.MaxValue, distB = float.MaxValue, distC = float.MaxValue;
            int smokeAcquireBudget = 1;

            for (int i = fires.Count - 1; i >= 0; i--)
            {
                FireSite site = fires[i];
                if (now >= site.Expires)
                {
                    DemolishBurnedBuilding(site);
                    visualPool.Release(site.Visual);
                    fuelDepotSmokePool.Release(site.BuildingSmoke);
                    fires.RemoveAt(i);
                    continue;
                }
                site.Visual?.SetPosition(site.Position);
                site.Visual?.SetClusterScale(site.ClusterScale);
                site.Visual?.SetPhase(
                    Mathf.Max(0f, now - site.Born),
                    Mathf.Clamp01((site.Expires - now) / Plugin.Settings.FireLifetime),
                    wind);
                TrySpread(site, now, wind);
                if (site.BuildingSmoke != null)
                {
                    site.BuildingSmoke.SetPosition(site.Position);
                    site.BuildingSmoke.SetForestClusterScale(site.ClusterScale);
                    site.BuildingSmoke.SetPhase(
                        Mathf.Max(0f, now - site.Born),
                        Mathf.Clamp01((site.Expires - now) / Plugin.Settings.FireLifetime),
                        wind);
                }

                if (now >= site.NextSmoke)
                {
                    // A network client can receive an ignition before the building's
                    // colliders finish loading. Retry the association lazily so it still
                    // gets the narrow tall building plume instead of the forest profile.
                    if (site.BurningBuilding == null && site.BurningMapBuilding == null)
                    {
                        Vector3 local = site.Position.ToLocalPosition();
                        FindBuildings(local, out site.BurningBuilding, out site.BurningMapBuilding);
                    }
                    bool buildingFire = site.BurningBuilding != null || site.BurningMapBuilding != null;
                    site.Forest = !buildingFire;
                    site.Visual?.Configure(site.Forest, site.Position);
                    if (site.BuildingSmoke == null && !GameManager.IsHeadless && smokeAcquireBudget > 0)
                    {
                        // Both urban and forest sites now use smoke-only copies of the actual
                        // Fuel Depot destruction prefab. Forest fires get a wider, windier
                        // three-source profile rather than the legacy ContactSmoke catalogue.
                        Vector2 halfExtents = buildingFire
                            ? GetBuildingHalfExtents(site.BurningBuilding, site.BurningMapBuilding)
                            : GetForestSmokeHalfExtents(site.Position);
                        site.BuildingSmoke = fuelDepotSmokePool.Acquire(
                            site.Position, halfExtents,
                            site.Forest
                                ? FuelDepotSmokePool.SmokeProfile.Forest
                                : FuelDepotSmokePool.SmokeProfile.Building);
                        if (site.BuildingSmoke != null) smokeAcquireBudget--;
                        site.BuildingSmoke?.SetForestClusterScale(site.ClusterScale);
                    }
                    site.NextSmoke = site.BuildingSmoke == null ? now + 2.4f : float.MaxValue;
                }
                if (camera == null || site.Visual == null) continue;
                float d = (camera.transform.position - site.Position.ToLocalPosition()).sqrMagnitude;
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
        }

        private void TrySpread(FireSite source, float now, Vector3 wind)
        {
            if (!source.Forest || !Plugin.Settings.FireSpreadEnabled || !IsServer()) return;
            if (source.Generation >= Plugin.Settings.FireSpreadGenerations ||
                source.SpreadAttempts >= 2 || now < source.NextSpread) return;

            source.SpreadAttempts++;
            uint seed = Deterministic.Hash(
                Mathf.RoundToInt(source.Position.x), Mathf.RoundToInt(source.Position.z),
                source.Generation, source.SpreadAttempts * 104729);
            float intervalJitter = 0.82f + Deterministic.UnitFloat(seed ^ 0x9e3779b9u) * 0.48f;
            source.NextSpread = now + Plugin.Settings.FireSpreadInterval * intervalJitter;

            Vector3 windDirection = new Vector3(wind.x, 0f, wind.z);
            if (windDirection.sqrMagnitude < 0.25f)
            {
                float calmAngle = Deterministic.UnitFloat(seed ^ 0x85ebca6bu) * Mathf.PI * 2f;
                windDirection = new Vector3(Mathf.Cos(calmAngle), 0f, Mathf.Sin(calmAngle));
            }
            else windDirection.Normalize();
            Vector3 crosswind = new Vector3(-windDirection.z, 0f, windDirection.x);

            float baseDistance = Plugin.Settings.FireSpreadDistance;
            for (int option = 0; option < 6; option++)
            {
                uint optionSeed = Deterministic.Hash((int)seed, option, source.Generation, 0x165667b1);
                float lateral = Deterministic.UnitFloat(optionSeed) * 2f - 1f;
                Vector3 direction = (windDirection + crosswind * lateral * 0.9f).normalized;
                float advance = Mathf.Max(baseDistance, Plugin.Settings.ScorchRadius * 0.9f);
                float distance = advance *
                    (0.9f + Deterministic.UnitFloat(optionSeed ^ 0xc2b2ae35u) * 0.45f);
                GlobalPosition candidate = source.Position + direction * distance;
                if (!forestIndex.Contains(candidate) || !SeparatedFromExisting(candidate, baseDistance * 0.42f)) continue;

                GlobalPosition grounded = SnapForestFireToGround(candidate);
                // Spread must create a new visible section of the fire line. Merging here
                // used the same large radius intended for unrelated impact consolidation,
                // so every 60-90 m child was swallowed back into its parent and only made
                // the original particle system slightly larger. The generation/site caps
                // already bound this to at most seven logical sites per original ignition.
                Ignite(grounded, now, true, source.Generation + 1, false);
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogInfo($"Fire spread generation {source.Generation + 1} to {grounded}");
                return;
            }
        }

        private bool SeparatedFromExisting(GlobalPosition position, float minimumDistance)
        {
            float minimumSq = minimumDistance * minimumDistance;
            for (int i = 0; i < fires.Count; i++)
                if ((fires[i].Position - position).sqrMagnitude < minimumSq) return false;
            return true;
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

        private void QueueScorch(GlobalPosition position, float radiusScale)
        {
            if (scorches.Count >= 64) return;
            scorches.Enqueue(new ScorchMark
            {
                Position = position,
                RadiusScale = radiusScale
            });
        }

        private void QueueForestScorch(GlobalPosition position)
        {
            // Layered, slightly offset vanilla blast-map stamps make a connected irregular
            // ash bed. This deepens the gray center without increasing the tree-clear radius
            // into a perfect oversized circle, and the queue still drains one stamp/frame.
            QueueScorch(position, 1f);
            uint seed = Deterministic.Hash(
                Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z), 0x61a7, impactSequence);
            float angle = Deterministic.UnitFloat(seed) * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) *
                Mathf.Lerp(6f, 11f, Deterministic.UnitFloat(seed ^ 0x9e3779b9u));
            QueueScorch(position + offset, 0.78f);
            QueueScorch(position - offset * 0.62f, 0.66f);
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

        private static GlobalPosition SnapForestFireToGround(GlobalPosition position)
        {
            Vector3 local = position.ToLocalPosition();
            RaycastHit hit;
            if (Physics.Raycast(local + Vector3.up * 80f, Vector3.down, out hit, 260f, PhysicsLayers.StaticsMask))
                return (hit.point + Vector3.up * 0.2f).ToGlobalPosition();
            return position;
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
            Renderer[] renderers = shell.GetComponentsInChildren<Renderer>(false);
            bounds = default(Bounds);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer ||
                    !renderer.gameObject.activeInHierarchy) continue;
                string name = renderer.gameObject.name.ToLowerInvariant();
                if (name.Contains("rubble") || name.Contains("wreck") ||
                    name.Contains("destroyed") || name.Contains("ruin")) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        private static Vector2 GetForestSmokeHalfExtents(GlobalPosition position)
        {
            float x = Mathf.PerlinNoise(position.x * 0.017f, position.z * 0.011f);
            float z = Mathf.PerlinNoise(position.z * 0.019f, position.x * 0.013f);
            // A wildfire plume rises from an area/front rather than a point source. The
            // three pooled vanilla smoke cores start across this broader irregular base and
            // then shear together with the wind as the logical cluster grows.
            return new Vector2(Mathf.Lerp(16f, 24f, x), Mathf.Lerp(14f, 22f, z));
        }

        private static void ApplyBurningBuildingDamage(MapBuilding building)
        {
            if (building == null || !Plugin.Settings.BuildingDamageEnabled.Value) return;
            // A burning city should not retain a pristine facade until the final frame.
            // Reuse the synchronized battered/soot state as soon as fire establishes.
            BuildingDamageVisual.Apply(building);
            ModNet.BroadcastBuildingDamage(building.transform.GlobalPosition());
        }

        private void DemolishBurnedBuilding(FireSite site)
        {
            if (!Plugin.Settings.DemolishUnoccupiedBuildings.Value || !IsServer()) return;

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
                    if (Plugin.Settings.VerboseLogging.Value)
                        Plugin.Logger.LogInfo("Fire burned out in occupied building; preserving " + building.unitName + ".");
                }
                else
                {
                    building.Networkdisabled = true;
                    demolished = true;
                    if (Plugin.Settings.VerboseLogging.Value)
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
                if (Plugin.Settings.VerboseLogging.Value)
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

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }
    }
}
