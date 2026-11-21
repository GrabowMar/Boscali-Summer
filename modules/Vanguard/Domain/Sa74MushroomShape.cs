using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Presentation shape curves for the volumetric mushroom, not a damage model.
    /// Sa74Mushroom.shader mirrors each formula through paired comments; keep the two in step.
    /// The rise is paced for a majestic climb, the standing cloud rides the live wind, and the
    /// fireball rolls into the rising head instead of fading as a shrinking sphere.</summary>
    internal static class Sa74MushroomShape
    {
        /// <summary>Rise time constant: 63 percent of the climb at 110 s, effectively complete by 330 s.</summary>
        public const float GrowthTau = 110f;
        /// <summary>The young head is entrained through the fireball; the towering stem settles by 95 s.</summary>
        public const float StemSettleStart = 18f, StemSettleEnd = 95f;
        /// <summary>The fireball stretches, hollows and rolls into the rising head between 18 and 60 s.</summary>
        public const float FireballMorphStart = 18f, FireballMorphEnd = 60f;
        /// <summary>The base skirt rides 30 percent of the wind; the cap rides all of it.</summary>
        public const float SkirtShear = .30f;
        /// <summary>Full-frame posing for the fast first 30 s, then a 20 Hz re-pose rate.</summary>
        public const float FastPoseSeconds = 30f, ThrottledPoseInterval = .05f;

        private static readonly float[] CalmDispersal = Normalized(14000f, 5000f);

        public static float Growth(float age) => 1f - (float)Math.Exp(-Math.Max(0f, age) / GrowthTau);
        public static float StemFactor(float age) => .50f + .50f * Smooth(StemSettleStart, StemSettleEnd, age);
        /// <summary>Constant-wind drift of the cap: wind * age, so aged snapshots integrate instantly.</summary>
        public static float CapDrift(float age, float windSpeed) => Math.Max(0f, age) * windSpeed;
        public static float WindShear(float heightFraction) =>
            SkirtShear + (1f - SkirtShear) * Math.Max(0f, Math.Min(1f, heightFraction));
        public static void DispersalDirection(float windX, float windZ, out float dirX, out float dirZ)
        {
            float magnitude = (float)Math.Sqrt(windX * windX + windZ * windZ);
            if (magnitude < .001f) { dirX = CalmDispersal[0]; dirZ = CalmDispersal[1]; return; }
            dirX = windX / magnitude; dirZ = windZ / magnitude;
        }
        public static float TorusMorph(float age) => Smooth(FireballMorphStart, FireballMorphEnd, age);
        public static float PoseInterval(float age) => age < FastPoseSeconds ? 0f : ThrottledPoseInterval;

        private static float Smooth(float from, float to, float value)
        {
            float t = Math.Max(0f, Math.Min(1f, (value - from) / (to - from)));
            return t * t * (3f - 2f * t);
        }
        private static float[] Normalized(float x, float z)
        {
            float magnitude = (float)Math.Sqrt(x * x + z * z);
            return magnitude < .001f ? new[] { 1f, 0f } : new[] { x / magnitude, z / magnitude };
        }
    }
}
