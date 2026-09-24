using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Pure response curves for falling-rain visuals: emission density, streak alpha,
    /// and deterministic gust modulation. Engine-agnostic; covered by RainVisualMathTests.
    /// </summary>
    internal static class RainVisualMath
    {
        public const int MaxParticles = 1000;
        public const float BaseEmissionSlow = 350f;
        public const float BaseEmissionFast = 1600f;
        public const float ReferenceSpeed = 250f;

        public static float BelowCloudFactor(float cameraHeight, float cloudHeight)
        {
            float t = Clamp01((cameraHeight - cloudHeight) / 400f);
            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Particles per second preserving spatial density across airspeeds, scaled by
        /// user density and the current gust factor. The emitter's MaxParticles cap is
        /// the final bound; this rate keeps the alive count under it (see tests).
        /// </summary>
        public static float EmissionRate(float apparentSpeed, float intensity, float density, float gust)
        {
            float t = Clamp01(apparentSpeed / ReferenceSpeed);
            float rate = BaseEmissionSlow + (BaseEmissionFast - BaseEmissionSlow) * t;
            return rate * Clamp01(intensity) * Math.Max(0f, density) * Math.Max(0f, gust);
        }

        /// <summary>
        /// Streak alpha: present at low intensity without blooming, denser into storms.
        /// Monotonic in [0, 0.38]; stays under the bloom threshold.
        /// </summary>
        public static float StreakAlpha(float intensity)
        {
            float i = Clamp01(intensity);
            return 0.18f * i + 0.20f * i * i;
        }

        /// <summary>
        /// Deterministic gust multiplier in [0.75, 1.25] from slow beating harmonics,
        /// so squalls breathe instead of spraying uniformly.
        /// </summary>
        public static float GustFactor(float missionTime, int seed)
        {
            double g = 1.0
                + 0.15 * Math.Sin(missionTime * 0.6 + seed * 0.37)
                + 0.10 * Math.Sin(missionTime * 1.9 + seed * 1.31);
            return (float)Math.Max(0.75, Math.Min(1.25, g));
        }

        /// <summary>
        /// Upper bound on alive particles for a rate/lifetime pair (Little's law).
        /// </summary>
        public static float AliveEstimate(float emissionRate, float lifetime) =>
            Math.Max(0f, emissionRate) * Math.Max(0f, lifetime);

        /// <summary>
        /// Clamp an emission rate so the steady-state alive count fits the budget.
        /// </summary>
        public static float ClampRateToBudget(float emissionRate, float lifetime, int maxParticles)
        {
            if (lifetime <= 0.01f || maxParticles <= 0) return Math.Max(0f, emissionRate);
            return Math.Min(Math.Max(0f, emissionRate), maxParticles / lifetime);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
