using System;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure, engine-free siege math: urban tiers from cached shell counts, capture-defense
    /// multipliers, the strongpoint drain floor, and the armor assault bonus. Vanilla capture
    /// ticks at 1 Hz with rate (defStr - atkStr) / (baseDef + unitDef), so every knob here is
    /// relative to whatever the mission set: cities take longer and need mass + armor, while
    /// hamlets and open ground stay exactly vanilla.
    /// </summary>
    internal static class SiegeMath
    {
        public const int MaxTier = 3;

        private static readonly float[] TierDefense = { 1f, 2.5f, 4f, 6.5f };
        private static readonly float[] TierFloors = { 0f, 0.25f, 0.45f, 0.6f };
        private static readonly int[] TierNests = { 0, 1, 2, 3 };

        /// <summary>Extra capture defense per intact rooftop nest, in multiples of the base.</summary>
        public const float DefensePerNest = 0.75f;

        /// <summary>Armor assault bonus per urban tier, in multiples of vehicle strength.</summary>
        public const float ArmorPerTier = 0.5f;

        /// <summary>Hamlet/town/city/metro from the shells already counted for a zone.</summary>
        public static int ComputeTier(int shellCount)
        {
            if (shellCount >= 60) return 3;
            if (shellCount >= 30) return 2;
            if (shellCount >= 10) return 1;
            return 0;
        }

        /// <summary>Extra rooftop nests above the configured count for an urban zone.</summary>
        public static int GarrisonBonus(int tier) => TierNests[ClampTier(tier)];

        /// <summary>
        /// Capture-defense multiplier for a zone. A zero scale (or vanilla hamlet with no
        /// nests) returns exactly 1, leaving mission pacing untouched.
        /// </summary>
        public static float DefenseMultiplier(int tier, int intactNests, float scale)
        {
            if (scale <= 0f) return 1f;
            float bonus = (TierDefense[ClampTier(tier)] - 1f) + Math.Max(0, intactNests) * DefensePerNest;
            return 1f + bonus * scale;
        }

        public static float ApplyDefense(float vanilla, int tier, int intactNests, float scale)
        {
            if (vanilla <= 0f) return vanilla;
            return vanilla * DefenseMultiplier(tier, intactNests, scale);
        }

        /// <summary>
        /// Drain floor: while defender strongpoints stand, control cannot drop below this,
        /// so attackers must reduce the nests before the zone can go neutral.
        /// </summary>
        public static float SiegeFloor(int tier, int intactNests, float scale)
        {
            if (scale <= 0f || intactNests < 1) return 0f;
            return Math.Min(0.9f, TierFloors[ClampTier(tier)] * scale);
        }

        /// <summary>Ground-vehicle capture-strength multiplier inside an urban zone.</summary>
        public static float ArmorMultiplier(int tier, float scale)
        {
            if (scale <= 0f) return 1f;
            return 1f + ClampTier(tier) * ArmorPerTier * scale;
        }

        /// <summary>
        /// Effective nest count: configured garrisons plus the urban bonus, capped. Zero stays
        /// zero (undefended zones), and the caller passes the zone ceiling so this file stays
        /// engine-free for the test runner.
        /// </summary>
        public static int EffectiveGarrisons(int configured, int tier, int max)
        {
            if (configured <= 0) return 0;
            return Math.Max(0, Math.Min(Math.Max(0, max), configured + GarrisonBonus(tier)));
        }

        private static int ClampTier(int tier) => Math.Max(0, Math.Min(MaxTier, tier));
    }
}
