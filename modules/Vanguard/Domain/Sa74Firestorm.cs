using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Map-scale firestorm rules: how far fires start, how quickly each ignites, and how smoke,
    /// embers and the high soot deck persist. Pure presentation envelopes, not a damage model.</summary>
    internal static class Sa74Firestorm
    {
        public const int Plumes = 520, Puffs = 14, FlameQuads = 8, Embers = 700, DeckPuffs = 180, PillarPuffs = 28;
        public const float Period = 130f, PillarPeriod = 240f, DeckRadius = 60000f;
        private const float ReachFactor = .78f;

        public static float Reach(float spanX, float spanY)
        {
            float span = Math.Max(Finite(spanX) ? Math.Abs(spanX) : 0f, Finite(spanY) ? Math.Abs(spanY) : 0f);
            return span * ReachFactor;
        }

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

        public static float SmokeFade(float phase)
        {
            if (!Finite(phase)) return 0f;
            phase = Math.Max(0f, Math.Min(1f, phase));
            float appear = Math.Min(1f, phase / .10f);
            return appear * (float)Math.Pow(1f - phase, 1.15);
        }

        public static float Flicker(float age, uint seed)
        {
            if (!Finite(age)) return .78f;
            return .78f + .22f * (float)Math.Sin(age * 6.3f + Hash(seed) * 6.2831853f);
        }

        public static float Hash(uint value)
        {
            unchecked { value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15; value *= 0x846ca68bu; value ^= value >> 16; }
            return (value & 0x00ffffffu) / 16777215f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
