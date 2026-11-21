using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>The thermal flash and firestorm scorch the land itself, not a 60 km disc. The reach
    /// passes the map corners so every terrain chunk receives soot; strength follows the first minute.</summary>
    internal static class Sa74Burn
    {
        public const float StartSeconds = 2f, FullSeconds = 60f;
        public const int Steps = 10;
        private const float ReachFactor = 1.4f;

        public static float Reach(float spanX, float spanY)
        {
            float span = Math.Max(Finite(spanX) ? Math.Abs(spanX) : 0f, Finite(spanY) ? Math.Abs(spanY) : 0f);
            return span * ReachFactor;
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
