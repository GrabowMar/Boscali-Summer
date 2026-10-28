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

        public static float Wetness(float current, float rain, float speedNorm, float dt)
        {
            current = Scalar.Clamp01(current);
            float target = Scalar.Clamp01(rain);
            float rate = target > current ? 0.5f : 0.025f + Scalar.Clamp01(speedNorm) * 0.12f;
            float step = Math.Max(0f, dt) * rate;
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }

        // Saturated, cold air retains moisture; warm air with a dewpoint deficit clears it.
        public static float Evaporation(float temperatureC, float dewpointC)
        {
            float deficit = Scalar.Clamp01((temperatureC - dewpointC) / 15f);
            float warmth = Scalar.Clamp01((temperatureC + 10f) / 40f);
            return 0.2f + deficit * 0.8f + warmth * 0.4f;
        }
    }
}
