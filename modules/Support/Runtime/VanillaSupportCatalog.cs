using System;
using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Resolves vanilla definitions for support actions. Recon needs the private
    /// <c>FactionHQ.SetTrackingState</c> seam (probed once); strikes need a non-nuclear
    /// missile definition from the encyclopedia.
    /// </summary>
    internal sealed class VanillaSupportCatalog
    {
        internal static readonly MethodInfo SetTrackingState =
            AccessTools.Method(typeof(FactionHQ), "SetTrackingState",
                new[] { typeof(PersistentID), typeof(GlobalPosition), typeof(float) });

        public static bool ReconAvailable => true;

        public MissileDefinition Artillery(string key)
        {
            if (Encyclopedia.i == null || Encyclopedia.i.missiles == null)
                return null;
            string wanted = string.IsNullOrEmpty(key) ? null : key.Trim();
            MissileDefinition fallback = null;
            MissileDefinition preferredHeavy = null;

            for (int i = 0; i < Encyclopedia.i.missiles.Count; i++)
            {
                MissileDefinition definition = Encyclopedia.i.missiles[i];
                if (definition == null || definition.unitPrefab == null) continue;

                if (wanted != null && string.Equals(definition.jsonKey, wanted, StringComparison.Ordinal))
                    return definition;

                if (IsTerrainFollowing(definition)) continue;

                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                float yield = missile != null ? missile.GetYield() : 0f;
                // Exclude nuclear / apocalyptic warheads for standard orbital kinetic rod
                if (yield > 200f) continue;

                if (fallback == null) fallback = definition;

                string name = definition.jsonKey ?? string.Empty;
                if (name.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("penetrator", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    preferredHeavy = definition;
                }
            }

            if (wanted != null) return null;
            return preferredHeavy ?? fallback;
        }

        /// <summary>
        /// Cruise / optical-terrain seekers hug the deck the moment they spawn. Using one
        /// as the EMP or Rod visual is why those strikes appeared on the ground instead of
        /// at release altitude.
        /// </summary>
        internal static bool IsTerrainFollowing(MissileDefinition definition)
        {
            if (definition == null || definition.unitPrefab == null) return false;
            string name = definition.jsonKey ?? string.Empty;
            if (name.IndexOf("Cruise", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return definition.unitPrefab.GetComponentInChildren<OpticalSeekerCruiseMissile>(true) != null;
        }

        /// <summary>
        /// Cheapest vanilla mobile truck — the EW truck's prefab. <c>VehicleType.RDR</c>
        /// turned out to be a static radar container in this game, not a driveable vehicle
        /// (confirmed live: it never moved when commanded), so the mobile phase uses a plain
        /// <c>VehicleType.TRUCK</c> instead — the exact same selection rule
        /// <c>HighCommandManager</c> already uses for its convoy vehicles (cheapest valid
        /// TRUCK with a working <c>GroundVehicle.UnitCommand</c>), since that path is proven
        /// to spawn a correctly-initialised vehicle. A name-based "radar" preference was tried
        /// and picked a broken/incomplete prefab, so it is deliberately not used.
        /// </summary>
        public VehicleDefinition EwTruck()
        {
            if (Encyclopedia.i == null || Encyclopedia.i.vehicles == null) return null;
            VehicleDefinition best = null;
            for (int i = 0; i < Encyclopedia.i.vehicles.Count; i++)
            {
                VehicleDefinition candidate = Encyclopedia.i.vehicles[i];
                if (candidate == null || candidate.unitPrefab == null) continue;
                if (candidate.vehicleType != VehicleType.TRUCK) continue;
                GroundVehicle vehicle = candidate.unitPrefab.GetComponent<GroundVehicle>();
                if (vehicle == null || vehicle.UnitCommand == null) continue;
                if (best == null || candidate.value < best.value) best = candidate;
            }
            return best;
        }
    }
}
