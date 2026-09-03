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
    }
}
