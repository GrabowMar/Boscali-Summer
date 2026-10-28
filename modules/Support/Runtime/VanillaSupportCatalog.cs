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
        public MissileDefinition Artillery(string key) => Resolve(key, BallisticShell.Is, HeavyWords, 200f);

        /// <summary>Vanilla CruiseMissile1 carries 450 (nuclear warheads start at 1.5 M), so the shells' 200 cap would leave no cruise missile at all.</summary>
        public MissileDefinition Cruise(string key) => Resolve(key, IsCruiseSeeker, CruiseWords, 10000f);

        private static readonly string[] HeavyWords = { "heavy", "penetrator" };
        private static readonly string[] CruiseWords = { "cruise" }; // land attack, not the AShM that lists first

        /// <summary>The configured key if it flies the mission, else the first conventional (or, for artillery, the heavy/penetrator) match; a key that cannot fly fails the resolution instead of launching an erratic round.</summary>
        private static MissileDefinition Resolve(string key, Func<MissileDefinition, bool> flies, string[] prefer, float maxYield)
        {
            if (Encyclopedia.i == null || Encyclopedia.i.missiles == null)
                return null;
            string wanted = string.IsNullOrEmpty(key) ? null : key.Trim();
            MissileDefinition fallback = null;
            MissileDefinition preferred = null;

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
                if (yield > maxYield)
                {
                    if (keyed) return null;
                    continue;
                }

                if (keyed) return definition;
                string name = definition.jsonKey ?? string.Empty;
                // The auto-pick is vanilla only: the Vanguard pack's own rounds (the AEGIS dart rides a cruise-seeker prefab) register first and won the pick.
                if (name.StartsWith("VG_", StringComparison.Ordinal)) continue;
                if (fallback == null) fallback = definition;

                foreach (string word in prefer)
                    if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) preferred = definition;
            }

            if (wanted != null) return null;
            return preferred ?? fallback;
        }

        /// <summary>Only cruise seekers fly the cruise profile; anything else fails closed.</summary>
        internal static bool IsCruiseSeeker(MissileDefinition definition)
        {
            if (definition == null || definition.unitPrefab == null) return false;
            return definition.unitPrefab.GetComponentInChildren<OpticalSeekerCruiseMissile>(true) != null;
        }
    }
}
