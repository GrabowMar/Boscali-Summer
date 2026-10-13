using System;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Resolves vanilla definitions for support actions. Strikes need a non-nuclear missile
    /// definition from the encyclopedia whose seeker can fly a blind aimpoint release.
    /// </summary>
    internal sealed class VanillaSupportCatalog
    {
        public MissileDefinition Artillery(string key) => Resolve(key, BallisticShell.Is, true);

        /// <summary>
        /// PRSM flies the same blind-aimpoint ballistic shell as the rod.
        /// </summary>
        public MissileDefinition Prsm(string key) => Artillery(key);

        public MissileDefinition Cruise(string key) => Resolve(key, IsCruiseSeeker, false);

        /// <summary>The configured key if it flies the mission, else the first conventional (or, for artillery, the heavy/penetrator) match; a key that cannot fly fails the resolution instead of launching an erratic round.</summary>
        private static MissileDefinition Resolve(string key, Func<MissileDefinition, bool> flies, bool preferHeavy)
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
                if (!flies(definition))
                {
                    if (keyed) return null;
                    continue;
                }

                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                float yield = missile != null ? missile.GetYield() : 0f;
                // Exclude nuclear / apocalyptic warheads
                if (yield > 200f)
                {
                    if (keyed) return null;
                    continue;
                }

                if (keyed) return definition;
                if (fallback == null) fallback = definition;

                string name = definition.jsonKey ?? string.Empty;
                if (preferHeavy && (name.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("penetrator", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    preferredHeavy = definition;
                }
            }

            if (wanted != null) return null;
            return preferredHeavy ?? fallback;
        }

        /// <summary>Only cruise seekers fly the cruise profile; anything else fails closed.</summary>
        internal static bool IsCruiseSeeker(MissileDefinition definition)
        {
            if (definition == null || definition.unitPrefab == null) return false;
            return definition.unitPrefab.GetComponentInChildren<OpticalSeekerCruiseMissile>(true) != null;
        }
    }
}
