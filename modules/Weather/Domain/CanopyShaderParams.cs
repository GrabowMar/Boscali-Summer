using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
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
            float i = Scalar.Clamp01(intensity);
            if (i <= 0.001f) return 0f;
            return 0.15f + i * (0.5f + 2.0f * Scalar.Clamp01(speedNorm));
        }

        public static float Wetness(float current, float rain, float speedNorm, float dt)
        {
            current = Scalar.Clamp01(current);
            float target = Scalar.Clamp01(rain);
            float rate = target > current ? 0.5f : 0.025f + Scalar.Clamp01(speedNorm) * 0.12f;
            float step = Math.Max(0f, dt) * rate;
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }
    }
}
