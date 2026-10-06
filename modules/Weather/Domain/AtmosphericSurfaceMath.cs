using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Cosmetic moisture history; no aircraft icing or water simulation.</summary>
    internal static class AtmosphericSurfaceMath
    {
        internal static float AdvanceWetness(float wetness, float rain, float seconds)
        {
            float from = Scalar.Clamp01(wetness), target = Scalar.Clamp01(rain);
            float amount = Math.Max(0f, seconds) / (target > from ? 35f : 180f);
            return target > from ? Math.Min(target, from + amount) : Math.Max(target, from - amount);
        }

        internal static float ColdTarget(float temperatureC, float recentLiquid)
            => WeatherMath.Smoothstep(0f, 10f, -temperatureC) * Scalar.Clamp01(recentLiquid) * 0.35f;

        // Broad damp tone survives high views; close puddle detail has its own shader LOD.
        internal static float GroundDrawRange(float cameraAltitude)
            => Math.Min(26000f, 1800f + 2f * Math.Max(0f, cameraAltitude));
    }
}
