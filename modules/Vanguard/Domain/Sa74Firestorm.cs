using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Affected-zone firestorm rules: how far fires start, how quickly each ignites, and how
    /// the camera-nearest vanilla burn pool settles over time. Pure presentation envelopes.</summary>
    internal static class Sa74Firestorm
    {
        public const int CandidateSites = 72, ActivePool = 24;
        public const float ActiveRange = 45000f;

        public static float Reach(float spanX, float spanY) => Sa74Burn.Reach(spanX, spanY);

        /// <summary>Fires near ground zero burn bigger and taller: 2.6x at the crater, 1x past half the reach.</summary>
        public static float Scale(float distance, float reach)
        {
            if (!Finite(distance) || !Finite(reach) || reach <= 0f) return 1f;
            float near = Math.Max(0f, 1f - 2f * distance / reach);
            return 1f + 1.6f * near * near;
        }

        public static float IgnitionDelay(uint seed, float distance, float reach)
        {
            if (!Finite(distance) || !Finite(reach) || reach <= 0f) return 2f;
            float share = Math.Max(0f, Math.Min(1f, distance / reach));
            return 2f + share * 28f + Hash(seed) * 10f;
        }

        public static float Growth(float age, float start)
        {
            if (!Finite(age) || !Finite(start)) return 0f;
            float t = Math.Max(0f, Math.Min(1f, (age - start) / 45f));
            return t * t * (3f - 2f * t);
        }

        public static float Settle(float age)
        {
            if (!Finite(age)) return 0f;
            float t = Math.Max(0f, Math.Min(1f, (age - 1200f) / 1800f));
            return t * t * (3f - 2f * t);
        }

        public static float Hash(uint value)
        {
            unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16; }
            return (value & 0x00ffffffu) / 16777215f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
