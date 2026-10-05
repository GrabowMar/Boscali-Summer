using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Pure storm-lightning curves: strike scheduling, the double-pulse flash envelope,
    /// and thunder delay/gain from distance. Engine-agnostic; covered by LightningMathTests.
    /// </summary>
    internal static class LightningMath
    {
        /// <summary>No bolts below heavy rain.</summary>
        public const float MinRain = 0.6f;

        /// <summary>Mean seconds between strikes at MinRain..full storm.</summary>
        public const float MaxInterval = 25f, MinInterval = 6f;

        public const float SoundSpeed = 343f;

        /// <summary>Seconds until the next strike for a unit random and rain, or infinity.</summary>
        public static float NextDelay(float unit, float rain)
        {
            float r = Clamp01(rain);
            if (r < MinRain) return float.PositiveInfinity;
            float t = (r - MinRain) / (1f - MinRain);
            return (MaxInterval + (MinInterval - MaxInterval) * t) * (0.5f + Clamp01(unit));
        }

        /// <summary>Flash brightness t seconds after the bolt: main stroke plus restrike.</summary>
        public static float FlashEnvelope(float t)
        {
            if (t < 0f) return 0f;
            float flash = (float)Math.Exp(-t / 0.09);
            if (t >= 0.12f) flash += 0.6f * (float)Math.Exp(-(t - 0.12f) / 0.12);
            return flash > 1f ? 1f : flash;
        }

        /// <summary>Physical travel delay. Presentation culls distant events before queueing.</summary>
        public static float ThunderDelay(float distanceM) =>
            Math.Max(0f, distanceM) / SoundSpeed;

        /// <summary>Thunder loudness for a strike distance, scaled by the master volume.</summary>
        public static float ThunderGain(float distanceM, float master) =>
            Clamp(900f / (900f + Math.Max(0f, distanceM)), 0f, 1f) * Math.Max(0f, master);

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
