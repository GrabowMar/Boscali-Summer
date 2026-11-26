using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>The thermal flash and firestorm char only the affected land around ground zero;
    /// strength ramps up over the first minute.</summary>
    internal static class Sa74Burn
    {
        public const float StartSeconds = 2f, FullSeconds = 60f, MaxRadius = 22000f;
        public const int Steps = 10;
        private const float MapShare = .35f;

        public static float Reach(float spanX, float spanY)
        {
            if (!Finite(spanX) || !Finite(spanY)) return 0f;
            float span = Math.Max(Math.Abs(spanX), Math.Abs(spanY));
            return span <= 0f ? 0f : Math.Min(MaxRadius, span * MapShare);
        }

        public static float EffectiveRadius(float maxReach, float frontRadius, float groundClearance)
        {
            if (!Finite(maxReach) || !Finite(frontRadius) || !Finite(groundClearance) || maxReach <= 0f || frontRadius <= 0f)
                return 0f;
            float r = Math.Min(maxReach, frontRadius);
            float c = Math.Max(0f, groundClearance);
            return c >= r ? 0f : (float)Math.Sqrt(r * r - c * c);
        }

        public static float Strength(float age)
        {
            if (!Finite(age) || age <= StartSeconds) return 0f;
            float t = Math.Min(1f, (age - StartSeconds) / (FullSeconds - StartSeconds));
            return t * t * (3f - 2f * t);
        }

        public static int Step(float age) => (int)Math.Round(Strength(age) * Steps);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
