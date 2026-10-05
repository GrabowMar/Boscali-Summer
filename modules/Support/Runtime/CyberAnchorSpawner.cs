using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Real EW trucks and data centers, spawned through the same legal-ground, atomic-group path as the SPACE uplinks
    /// (<see cref="UplinkSpawner"/>). A truck is one radar truck held in place; a data center is one static radar unit with two AAA guards.
    /// Only a complete legal group counts; a refused member removes the whole group.
    /// </summary>
    internal sealed class CyberAnchorSpawner
    {
        private const int MaximumOwned = 32;
        /// <summary>Verified allowed and usable in the world check: HLT-R and Truck2-R (300 HP radar trucks), SPAAG1. RadarContainer1 is disallowed in Free Flight, so it is tried first and skipped when unusable.</summary>
        internal static readonly string[] TruckKeys = { "HLT-R", "Truck2-R" };
        internal static readonly string[] CenterKeys = { "RadarContainer1", "Truck2-R", "HLT-R" };
        /// <summary>
        /// SOF camp: one supply or command vehicle plus two light escorts. UNVERIFIED keys (the world check proved only HLT-R, Truck2-R, SPAAG1 and RadarSAM1 usable):
        /// the first usable key of each list is taken and the log line names what was resolved; the native fixture probes the real encyclopedia (plan Task 8).
        /// </summary>
        internal static readonly string[] CampKeys = { "HLT-L", "Truck2-L", "HLT-R", "Truck2-R" };
        internal static readonly string[] CampGuardKeys = { "Truck2-MRAP", "LightTruck1_AA", "SPAAG1" };
        private readonly List<Unit> owned = new List<Unit>(MaximumOwned);
        private readonly List<Unit> pendingCleanup = new List<Unit>(MaximumOwned);
        private readonly Dictionary<Unit, List<Unit>> groups = new Dictionary<Unit, List<Unit>>();
        private int serial;

        internal int OwnedCount => owned.Count;
        internal bool Owns(Unit unit) => unit != null && owned.Contains(unit);

        /// <summary>The definition keys this mission actually resolved, for the log line and the native fixture.</summary>
        internal static string Describe()
        {
            VehicleDefinition truck = FindFirst(TruckKeys, null), center = FindFirst(CenterKeys, truck);
            return "truck=" + (truck != null ? truck.jsonKey : "none") + " center=" + (center != null ? center.jsonKey : "none") +
                " guard=" + (Find("SPAAG1") != null ? "SPAAG1" : "none");
        }

        /// <summary>What the SOF camp resolved to this mission, for the log line and the native fixture.</summary>
        internal static string DescribeCamp()
        {
            VehicleDefinition camp = FindFirst(CampKeys, null), guard = FindFirst(CampGuardKeys, null);
            return "camp=" + (camp != null ? camp.jsonKey : "none") + " escort=" + (guard != null ? guard.jsonKey : "none");
        }

        internal bool TryPlanCamp(GlobalPosition anchor, Airbase parent, out GlobalPosition[] positions, out Quaternion rotation)
        {
            positions = null;
            rotation = Quaternion.identity;
            VehicleDefinition camp = FindFirst(CampKeys, null), guard = FindFirst(CampGuardKeys, null);
            if (camp == null || guard == null) return false;
            Vector3 origin = anchor.ToLocalPosition();
            var definitions = new[] { camp, guard, guard };
            var offsets = new[] { Vector3.zero, new Vector3(28f, 0f, 22f), new Vector3(-28f, 0f, -22f) };
            var planned = new GlobalPosition[3];
            for (int i = 0; i < planned.Length; i++)
                if (!PlanOne(definitions[i], origin + offsets[i], parent, rotation, out planned[i])) return false;
            positions = planned;
            return true;
        }

        internal bool TryCreateCamp(FactionHQ owner, int ordinal, GlobalPosition anchor, Airbase parent, out Unit camp)
        {
            camp = null;
            if (!TryPlanCamp(anchor, parent, out GlobalPosition[] positions, out Quaternion rotation)) return false;
            VehicleDefinition site = FindFirst(CampKeys, null), guard = FindFirst(CampGuardKeys, null);
            return TryCreateGroup(owner, "Camp", ordinal, new[] { site, guard, guard }, positions, rotation, parent, out camp);
        }

        internal bool TryPlanTruck(GlobalPosition anchor, Airbase parent, out GlobalPosition position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            VehicleDefinition truck = FindFirst(TruckKeys, null);
            if (truck == null) return false;
            return PlanOne(truck, anchor.ToLocalPosition(), parent, rotation, out position);
        }

        internal bool TryPlanCenter(GlobalPosition anchor, Airbase parent, out GlobalPosition[] positions, out Quaternion rotation)
        {
            positions = null;
            rotation = Quaternion.identity;
            VehicleDefinition truck = FindFirst(TruckKeys, null), center = FindFirst(CenterKeys, truck), guard = Find("SPAAG1");
            if (center == null || guard == null) return false;
            Vector3 origin = anchor.ToLocalPosition();
            var definitions = new[] { center, guard, guard };
            var offsets = new[] { Vector3.zero, new Vector3(30f, 0f, 24f), new Vector3(-30f, 0f, -24f) };
            var planned = new GlobalPosition[3];
            for (int i = 0; i < planned.Length; i++)
                if (!PlanOne(definitions[i], origin + offsets[i], parent, rotation, out planned[i])) return false;
            positions = planned;
            return true;
        }

        private static bool PlanOne(VehicleDefinition definition, Vector3 desired, Airbase parent, Quaternion rotation, out GlobalPosition planned)
        {
            planned = default;
            if (!UplinkSpawner.Legal(desired, parent) || !GroundPlacement.TryPlace(definition, desired, rotation, out Vector3 point) || !UplinkSpawner.Legal(point, parent)) return false;
            planned = point.ToGlobalPosition();
            return true;
        }

        internal bool TryCreateTruck(FactionHQ owner, int ordinal, GlobalPosition anchor, Airbase parent, out Unit truck)
        {
            truck = null;
            if (!TryPlanTruck(anchor, parent, out GlobalPosition position, out Quaternion rotation)) return false;
            VehicleDefinition definition = FindFirst(TruckKeys, null);
            return TryCreateGroup(owner, "Truck", ordinal, new[] { definition }, new[] { position }, rotation, parent, out truck);
        }

        internal bool TryCreateCenter(FactionHQ owner, int ordinal, GlobalPosition anchor, Airbase parent, out Unit center)
        {
            center = null;
            if (!TryPlanCenter(anchor, parent, out GlobalPosition[] positions, out Quaternion rotation)) return false;
            VehicleDefinition truck = FindFirst(TruckKeys, null), site = FindFirst(CenterKeys, truck), guard = Find("SPAAG1");
            return TryCreateGroup(owner, "Center", ordinal, new[] { site, guard, guard }, positions, rotation, parent, out center);
        }

        private bool TryCreateGroup(FactionHQ owner, string kind, int ordinal, VehicleDefinition[] definitions, GlobalPosition[] positions,
            Quaternion rotation, Airbase parent, out Unit first)
        {
            first = null;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (owner == null || spawner == null || !spawner.IsServer || owned.Count > MaximumOwned - definitions.Length ||
                (parent != null && (parent.disabled || parent.CurrentHQ != owner))) return false;
            var group = new List<Unit>(definitions.Length);
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    // Recheck immediately before spawn: other native spawners can occupy a planned footprint.
                    Vector3 raw = positions[i].ToLocalPosition() - rotation * definitions[i].spawnOffset;
                    if (!GroundPlacement.TryPlace(definitions[i], raw, rotation, out Vector3 point) || !UplinkSpawner.Legal(point, parent))
                        throw new InvalidOperationException("Cyber " + kind + " footprint became occupied");
                    Unit unit = spawner.SpawnVehicle(definitions[i].unitPrefab, point.ToGlobalPosition(), rotation, Vector3.zero, owner,
                        SupportNaming.Prefix + "Cyber:" + kind + ":" + owner.name + ":" + ordinal + ":" + (++serial), 1f, true, null);
                    if (unit == null) throw new InvalidOperationException("Native cyber group spawn refused");
                    owned.Add(unit);
                    group.Add(unit);
                    if (!unit.IsServer || unit.NetworkHQ != owner || unit.disabled || UplinkSpawner.Health(unit) <= 0f)
                        throw new InvalidOperationException("Native cyber group is not operational");
                    if (unit is GroundVehicle vehicle) vehicle.SetHoldPosition(true);
                    if (i == 0) first = unit;
                }
                groups[first] = group;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("[Support.Cyber] " + kind + " placement refused: " + e.Message);
                for (int i = group.Count - 1; i >= 0; i--) Remove(group[i]);
                first = null;
                return false;
            }
        }

        public void ResetForScene()
        {
            for (int i = owned.Count - 1; i >= 0; i--) Remove(owned[i]);
            groups.Clear();
        }

        internal void RetryCleanup()
        {
            for (int i = pendingCleanup.Count - 1; i >= 0; i--) Remove(pendingCleanup[i]);
        }

        /// <summary>Removes a whole anchor group (the anchor and its guards): a rebuilt anchor's wreck. A unit that heads no group is removed alone.</summary>
        internal void DiscardGroup(Unit first)
        {
            if (first == null) return;
            if (!groups.TryGetValue(first, out List<Unit> group)) { Remove(first); return; }
            groups.Remove(first);
            for (int i = group.Count - 1; i >= 0; i--) Remove(group[i]);
        }

        private void Remove(Unit unit)
        {
            try
            {
                if (unit != null)
                {
                    Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                    try
                    {
                        unit.Networkdisabled = true;
                        unit.gameObject.SetActive(false);
                        if (spawner != null && spawner.IsServer && spawner.ServerObjectManager != null)
                            spawner.ServerObjectManager.Destroy(unit.gameObject);
                    }
                    finally
                    {
                        // Native Destroy silently returns for an unregistered NetId0 identity.
                        if (unit != null) UnityEngine.Object.Destroy(unit.gameObject);
                    }
                }
                for (int i = owned.Count - 1; i >= 0; i--) if (ReferenceEquals(owned[i], unit)) { owned.RemoveAt(i); break; }
                for (int i = pendingCleanup.Count - 1; i >= 0; i--) if (ReferenceEquals(pendingCleanup[i], unit)) { pendingCleanup.RemoveAt(i); break; }
            }
            catch (Exception e)
            {
                if (!pendingCleanup.Contains(unit) && pendingCleanup.Count < MaximumOwned) pendingCleanup.Add(unit);
                Plugin.Logger?.LogWarning("[Support.Cyber] Native anchor cleanup will retry: " + e.Message);
            }
        }

        private static VehicleDefinition FindFirst(string[] keys, VehicleDefinition different)
        {
            VehicleDefinition fallback = null;
            for (int i = 0; i < keys.Length; i++)
            {
                VehicleDefinition d = Find(keys[i]);
                if (d == null) continue;
                if (different == null || d != different) return d;
                fallback = fallback ?? d;
            }
            return fallback;
        }

        private static VehicleDefinition Find(string key)
        {
            List<VehicleDefinition> definitions = Encyclopedia.i?.vehicles;
            if (definitions == null || definitions.Count > 256) return null;
            for (int i = 0; i < definitions.Count; i++)
            {
                VehicleDefinition d = definitions[i];
                if (d != null && d.jsonKey == key && GroundPlacement.Usable(d) && d.unitPrefab.GetComponent<GroundVehicle>() != null) return d;
            }
            return null;
        }
    }
}
