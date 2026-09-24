using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Pure response curves driving the canopy droplet/flow shader uniforms.
    /// Engine-agnostic; covered by CanopyScoringTests.
    /// </summary>
    internal static class CanopyShaderParams
    {
        /// <summary>Glass-UV scroll rate per second; frozen when dry.</summary>
        public static float FlowPhaseRate(float intensity, float speedNorm)
        {
            float i = Clamp01(intensity);
            if (i <= 0.001f) return 0f;
            return 0.15f + i * (0.5f + 2.0f * Clamp01(speedNorm));
        }

        public static float Wetness(float current, float rain, float speedNorm, float dt)
        {
            current = Clamp01(current);
            float target = Clamp01(rain);
            float rate = target > current ? 0.5f : 0.025f + Clamp01(speedNorm) * 0.12f;
            float step = Math.Max(0f, dt) * rate;
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
