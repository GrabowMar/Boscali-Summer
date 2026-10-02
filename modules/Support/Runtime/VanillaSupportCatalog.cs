using System;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Resolves vanilla definitions for support actions. Strikes need a non-nuclear missile
    /// definition from the encyclopedia whose seeker can fly a blind aimpoint release.
    /// </summary>
    internal sealed class VanillaSupportCatalog
    {
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

                bool keyed = wanted != null && string.Equals(definition.jsonKey, wanted, StringComparison.Ordinal);
                if (!IsBallisticShell(definition))
                {
                    // An explicitly configured shell that cannot fly the mission fails
                    // the resolution instead of launching an erratic round.
                    if (keyed) return null;
                    continue;
                }

                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                float yield = missile != null ? missile.GetYield() : 0f;
                // Exclude nuclear / apocalyptic warheads for standard orbital kinetic rod
                if (yield > 200f)
                {
                    if (keyed) return null;
                    continue;
                }

                if (keyed) return definition;
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
        /// PRSM flies the same blind-aimpoint ballistic shell as the rod.
        /// </summary>
        public MissileDefinition Prsm(string key) => Artillery(key);

        public MissileDefinition Cruise(string key)
        {
            if (Encyclopedia.i == null || Encyclopedia.i.missiles == null)
                return null;
            string wanted = string.IsNullOrEmpty(key) ? null : key.Trim();
            MissileDefinition fallback = null;

            for (int i = 0; i < Encyclopedia.i.missiles.Count; i++)
            {
                MissileDefinition definition = Encyclopedia.i.missiles[i];
                if (definition == null || definition.unitPrefab == null) continue;

                bool keyed = wanted != null && string.Equals(definition.jsonKey, wanted, StringComparison.Ordinal);
                if (!IsCruiseSeeker(definition))
                {
                    // An explicitly configured missile that cannot fly the cruise
                    // profile fails the resolution instead of flying dumb.
                    if (keyed) return null;
                    continue;
                }

                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                float yield = missile != null ? missile.GetYield() : 0f;
                if (yield > 200f)
                {
                    if (keyed) return null;
                    continue;
                }

                if (keyed) return definition;
                if (fallback == null) fallback = definition;
            }

            if (wanted != null) return null;
            return fallback;
        }

        /// <summary>
        /// Only aimpoint ballistic seekers can fly a strike release: a blind release
        /// gives the missile an aimpoint and nothing else, so lock-on seekers (IR, radar,
        /// laser) never acquire and cruise seekers hug the deck instead of diving. Using
        /// one as the EMP or Rod visual is why those strikes appeared on the ground
        /// instead of at release altitude. Anything outside this list fails closed.
        /// </summary>
        /// <summary>Only cruise seekers fly the cruise profile; anything else fails closed.</summary>
        internal static bool IsCruiseSeeker(MissileDefinition definition)
        {
            if (definition == null || definition.unitPrefab == null) return false;
            return definition.unitPrefab.GetComponentInChildren<OpticalSeekerCruiseMissile>(true) != null;
        }

        internal static bool IsBallisticShell(MissileDefinition definition)
        {
            if (definition == null || definition.unitPrefab == null) return false;
            GameObject prefab = definition.unitPrefab;
            return prefab.GetComponentInChildren<BallisticMissileGuidance>(true) != null
                || prefab.GetComponentInChildren<InertialSeekerShell>(true) != null
                || prefab.GetComponentInChildren<OpticalSeekerShell>(true) != null
                || prefab.GetComponentInChildren<OpticalSeekerBomb>(true) != null;
        }
    }
}
