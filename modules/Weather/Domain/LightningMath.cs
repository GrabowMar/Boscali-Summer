using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Pure storm-lightning curves: strike scheduling, the double-pulse flash envelope,
    /// and thunder delay/gain from distance. Engine-agnostic; covered by LightningMathTests.
    /// </summary>
    internal static class LightningMath
    {

        public const float SoundSpeed = 343f;

        /// <summary>Flash brightness t seconds after the bolt: main stroke plus restrike.</summary>
        public static float FlashEnvelope(float t)
        {
            if (t < 0f) return 0f;
            float flash = (float)Math.Exp(-t / 0.09);
            if (t >= 0.12f) flash += 0.6f * (float)Math.Exp(-(t - 0.12f) / 0.12);
            // A third return stroke: real flashes flicker two to four times (2026-10-18).
            if (t >= 0.38f) flash += 0.45f * (float)Math.Exp(-(t - 0.38f) / 0.10);
            return flash > 1f ? 1f : flash;
        }

        /// <summary>Physical travel delay. Presentation culls distant events before queueing.</summary>
        public static float ThunderDelay(float distanceM) =>
            Math.Max(0f, distanceM) / SoundSpeed;

        /// <summary>Thunder loudness for a strike distance, scaled by the master volume.</summary>
        public static float ThunderGain(float distanceM, float master) =>
            Scalar.Clamp(2500f / (2500f + Math.Max(0f, distanceM)), 0f, 1f) * Math.Max(0f, master);
    }
}
