using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using RoadPathfinding;
using UnityEngine;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>Finite vanilla reinforcement batches. The director owns award eligibility.</summary>
    internal sealed class OperationRewards
    {
        internal const int MaxOwnedUnits = 24;
        internal const int ConvoySize = 6;
        internal const int FortificationSize = 3;
        private const int MaxFactions = 8;
        private const int MaxDefinitions = 256;
        private const int MaxRoadNodes = 512;
        private const int MaxRoadPoints = 8192;
        private const float CooldownSeconds = 120f;
        private readonly List<Unit> owned = new List<Unit>(MaxOwnedUnits);
        private readonly HashSet<Unit> pendingCleanup = new HashSet<Unit>();
        private readonly Dictionary<FactionHQ, float> lastAward = new Dictionary<FactionHQ, float>(MaxFactions);
        private readonly List<Node> route = new List<Node>(MaxRoadNodes);
        private readonly Vector3[] positions = new Vector3[ConvoySize];
        private readonly Quaternion[] rotations = new Quaternion[ConvoySize];
        private readonly UnitDefinition[] definitions = new UnitDefinition[ConvoySize];
        private ManualLogSource log;
        private Encyclopedia catalog;
        private int catalogVehicleCount = -1, catalogBuildingCount = -1;
        private VehicleDefinition escort;
        private VehicleDefinition supply;
        private BuildingDefinition defense;
        private bool executing;

        internal void Configure(ManualLogSource logger) => log = logger;

        internal bool CanOfferConvoy
        {
            get { RefreshCatalog(); return escort != null && supply != null && owned.Count <= MaxOwnedUnits - ConvoySize && ValidRoadNetwork(Roads); }
        }

        internal bool CanOfferFortification
        {
            get { RefreshCatalog(); return defense != null && owned.Count <= MaxOwnedUnits - FortificationSize; }
        }

        internal bool CanOffer(OperationReward reward, FactionHQ hq)
        {
            if (reward == OperationReward.None) return true;
            if (executing || hq == null || !hq.IsServer || !MissionManager.IsRunning) return false;
            if (lastAward.TryGetValue(hq, out float last))
            {
                if (MissionTime < last + CooldownSeconds) return false;
            }
            else if (lastAward.Count >= MaxFactions) return false;
            return reward == OperationReward.Convoy ? CanOfferConvoy :
                reward == OperationReward.Fortification && CanOfferFortification;
        }

        internal string Execute(OperationReward reward, FactionHQ hq, Airbase target, Player requester, int operationId)
        {
            if (reward == OperationReward.None) return string.Empty;
            Prune();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null || !spawner.IsServer || !CanOffer(reward, hq))
                return "Reinforcements unavailable: capacity, capability or faction cooldown.";
            if (requester == null || !requester.IsServer || requester.HQ != hq)
                return "Reinforcements unavailable: no authoritative faction participant.";
            if (target == null || target.disabled || target.AttachedAirbase || target.CurrentHQ != hq || target.center == null)
                return "Reinforcements unavailable: the destination is not a friendly land base.";

            int start = owned.Count;
            executing = true;
            try
            {
                GlobalPosition destination = default;
                int count = reward == OperationReward.Convoy ? ConvoySize : FortificationSize;
                bool ready = reward == OperationReward.Convoy
                    ? PlanConvoy(hq, target, out destination) : PlanFortifications(target);
                if (!ready) return reward == OperationReward.Convoy
                    ? "Convoy unavailable: no clear connected road from another friendly base."
                    : "Fortifications unavailable: no clear dry ground around the objective.";

                // Reentrant offers are closed while the complete batch owns its reserved slots.
                for (int i = 0; i < count; i++)
                {
                    string name = "Boscali_Operation_" + operationId + "_" + hq.GetInstanceID() + "_" + i;
                    Unit unit = reward == OperationReward.Convoy
                        ? (Unit)spawner.SpawnVehicle(definitions[i].unitPrefab, positions[i].ToGlobalPosition(),
                            rotations[i], Vector3.zero, hq, name, 1f, true, null)
                        : spawner.SpawnBuilding(definitions[i].unitPrefab, positions[i].ToGlobalPosition(),
                            rotations[i], hq, target, name, false, null);
                    if (unit == null) throw new InvalidOperationException("Native reward spawn returned no unit.");
                    owned.Add(unit);
                }
                if (reward == OperationReward.Convoy)
                    for (int i = start; i < owned.Count; i++)
                    {
                        GroundVehicle vehicle = (GroundVehicle)owned[i];
                        if (vehicle.UnitCommand == null) throw new InvalidOperationException("Spawned vehicle has no native command component.");
                        // Native hold disables autonomous retasking; SetDestination still unanchors
                        // and drives the vehicle. Set it before spawn so supply AI also stays idle.
                        vehicle.UnitCommand.SetDestination(destination, false);
                    }
                lastAward[hq] = MissionTime;
                return reward == OperationReward.Convoy
                    ? "Six-vehicle convoy launched toward " + target.name + "."
                    : "Three defensive positions established at " + target.name + ".";
            }
            catch (Exception error)
            {
                RemoveOwnedFrom(start);
                log?.LogWarning("[DynamicOperations] Reinforcement batch rolled back: " + error.Message);
                return owned.Count == start ? "Reinforcements failed; spawned batch removed. Money and XP retained."
                    : "Reinforcements failed; cleanup pending. Money and XP retained.";
            }
            finally { executing = false; }
        }

        internal void Prune()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                Unit unit = owned[i];
                if (unit == null || (pendingCleanup.Contains(unit) && TryRemove(unit)))
                {
                    pendingCleanup.Remove(unit);
                    owned.RemoveAt(i);
                }
            }
            RefreshCatalog();
        }

        internal void ResetForScene()
        {
            RemoveOwnedFrom(0);
            lastAward.Clear();
            route.Clear();
            catalog = null;
            catalogVehicleCount = catalogBuildingCount = -1;
            escort = null;
            supply = null;
            defense = null;
            executing = false;
            Array.Clear(definitions, 0, definitions.Length);
        }

        private static float MissionTime => NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? 0f;
        private static RoadNetwork Roads => NetworkSceneSingleton<LevelInfo>.i?.roadNetwork;

        private void RefreshCatalog()
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null) return;
            int vehicleCount = Math.Min(MaxDefinitions, encyclopedia.vehicles?.Count ?? 0);
            int buildingCount = Math.Min(MaxDefinitions, encyclopedia.buildings?.Count ?? 0);
            if (encyclopedia == catalog && vehicleCount == catalogVehicleCount && buildingCount == catalogBuildingCount) return;
            catalog = encyclopedia;
            catalogVehicleCount = vehicleCount;
            catalogBuildingCount = buildingCount;
            escort = supply = null;
            defense = null;
            if (catalog.vehicles != null)
                for (int i = 0; i < vehicleCount; i++)
                {
                    VehicleDefinition candidate = catalog.vehicles[i];
                    if (!GroundPlacement.Usable(candidate) || candidate.unitPrefab.GetComponent<GroundVehicle>()?.UnitCommand == null) continue;
                    if ((candidate.vehicleType == VehicleType.AFV || candidate.vehicleType == VehicleType.MBT) &&
                        (escort == null || candidate.value < escort.value)) escort = candidate;
                    if (candidate.vehicleType == VehicleType.TRUCK && candidate.unitPrefab.GetComponentInChildren<Rearmer>() != null &&
                        (supply == null || candidate.value < supply.value)) supply = candidate;
                }
            if (catalog.buildings != null)
                for (int i = 0; i < buildingCount; i++)
                {
                    BuildingDefinition candidate = catalog.buildings[i];
                    if (GroundPlacement.Usable(candidate) && candidate.buildingType == BuildingType.DEF &&
                        candidate.unitPrefab.GetComponent<Building>() != null &&
                        (defense == null || candidate.value < defense.value)) defense = candidate;
                }
            log?.LogInfo("[DynamicOperations] Reward catalogue: convoy=" + (escort != null && supply != null) + ", fortification=" + (defense != null));
        }

        private bool PlanConvoy(FactionHQ hq, Airbase target, out GlobalPosition destination)
        {
            destination = default;
            RoadNetwork network = Roads;
            if (!ValidRoadNetwork(network)) return false;
            Airbase source = null;
            float best = float.MaxValue;
            int inspected = 0;
            foreach (Airbase candidate in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64) break;
                if (candidate == null || candidate == target || candidate.disabled || candidate.AttachedAirbase || candidate.CurrentHQ != hq || candidate.center == null) continue;
                float distance = (candidate.center.position - target.center.position).sqrMagnitude;
                if (distance < best && distance > 250000f) { best = distance; source = candidate; }
            }
            if (source == null || !NearestRoad(network, source.center.position, out Road road, out Vector3 origin, out Vector3 forward) ||
                !NearestRoad(network, target.center.position, out _, out Vector3 endpoint, out _)) return false;
            if ((origin - source.center.position).sqrMagnitude > 1200f * 1200f ||
                (endpoint - target.center.position).sqrMagnitude > 1200f * 1200f ||
                !GroundPlacement.DryGround(endpoint, out Vector3 destinationGround)) return false;
            destination = destinationGround.ToGlobalPosition();
            // A base often lies at a road endpoint; try either side before rejecting its road.
            for (int layout = 0; layout < 3; layout++)
            {
                float shift = layout == 0 ? 0f : layout == 1 ? 96f : -96f;
                if (PlanConvoyLayout(network, road, origin + forward * shift, forward, destination)) return true;
            }
            return false;
        }

        private bool PlanConvoyLayout(RoadNetwork network, Road road, Vector3 origin, Vector3 forward, GlobalPosition destination)
        {
            // ponytail: three line layouts, native road connectivity and six footprints;
            // use arc-distance placement if curved short access roads prove too restrictive.
            for (int i = 0; i < ConvoySize; i++)
            {
                definitions[i] = i < 4 ? escort : supply;
                Vector3 desired = origin + forward * ((i - 2.5f) * 32f);
                if (!NearestPoint(road, desired, out Vector3 point, out Vector3 heading) ||
                    !GroundPlacement.TryPlace(definitions[i], point, Quaternion.LookRotation(heading), out positions[i])) return false;
                rotations[i] = Quaternion.LookRotation(heading);
                for (int j = 0; j < i; j++)
                    if ((positions[i] - positions[j]).sqrMagnitude < 28f * 28f) return false;
            }
            for (int i = 0; i < ConvoySize; i++)
            {
                route.Clear();
                RoadPathfinder.TryPathfind(network, positions[i].ToGlobalPosition(), destination, route, out RoadPathfinder.PathfindResult result);
                if (result != RoadPathfinder.PathfindResult.Success || route.Count > MaxRoadNodes) return false;
            }
            return true;
        }

        private bool PlanFortifications(Airbase target)
        {
            float radius = Mathf.Clamp(target.GetRadius() * 0.7f, 70f, 240f);
            int found = 0;
            for (int attempt = 0; attempt < 12 && found < FortificationSize; attempt++)
            {
                float angle = attempt * Mathf.PI / 6f;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Quaternion rotation = Quaternion.LookRotation(direction);
                if (!GroundPlacement.TryPlace(defense, target.center.position + direction * radius, rotation, out Vector3 point)) continue;
                definitions[found] = defense;
                positions[found] = point;
                rotations[found++] = rotation;
            }
            return found == FortificationSize;
        }

        private static bool ValidRoadNetwork(RoadNetwork network)
        {
            if (network?.nodes == null || network.roads == null || network.nodes.Count == 0 ||
                network.nodes.Count > MaxRoadNodes || network.roads.Count == 0 || network.roads.Count > 2048) return false;
            int points = 0;
            foreach (Road road in network.roads)
            {
                if (road == null || road.points == null || road.startNode == null || road.endNode == null) return false;
                points += road.points.Count;
                if (points > MaxRoadPoints) return false;
            }
            return true;
        }

        private static bool NearestRoad(RoadNetwork network, Vector3 desired, out Road found, out Vector3 point, out Vector3 direction)
        {
            found = null;
            point = direction = default;
            float best = float.MaxValue;
            foreach (Road road in network.roads)
            {
                if (road.IsBridge() || !NearestPoint(road, desired, out Vector3 candidate, out Vector3 heading)) continue;
                float distance = (candidate - desired).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                found = road;
                point = candidate;
                direction = heading;
            }
            return found != null;
        }

        private static bool NearestPoint(Road road, Vector3 desired, out Vector3 result, out Vector3 direction)
        {
            result = direction = default;
            float best = float.MaxValue;
            for (int i = 1; i < road.points.Count; i++)
            {
                Vector3 a = road.points[i - 1].ToLocalPosition(), b = road.points[i].ToLocalPosition();
                Vector3 delta = b - a;
                if (!Finite(a) || !Finite(b) || delta.sqrMagnitude < 1f) continue;
                Vector3 candidate = a + delta * Mathf.Clamp01(Vector3.Dot(desired - a, delta) / delta.sqrMagnitude);
                float distance = (candidate - desired).sqrMagnitude;
                Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
                if (distance >= best || horizontal.sqrMagnitude < 1f) continue;
                best = distance;
                result = candidate;
                direction = horizontal.normalized;
            }
            return best < float.MaxValue;
        }

        private void RemoveOwnedFrom(int start)
        {
            for (int i = owned.Count - 1; i >= start; i--)
            {
                Unit unit = owned[i];
                if (unit == null || TryRemove(unit))
                {
                    pendingCleanup.Remove(unit);
                    owned.RemoveAt(i);
                }
                else if (pendingCleanup.Add(unit))
                    log?.LogWarning("[DynamicOperations] Reward cleanup deferred; object retains its capacity slot.");
            }
        }

        private static bool TryRemove(Unit unit)
        {
            try
            {
                Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                if (spawner != null && spawner.IsServer) spawner.ServerObjectManager.Destroy(unit.gameObject);
                else if (unit.IsServer) UnityEngine.Object.Destroy(unit.gameObject);
                else return false;
                return true;
            }
            catch { return false; }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
