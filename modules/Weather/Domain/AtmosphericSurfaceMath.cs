using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Cosmetic moisture history; no aircraft icing or water simulation.</summary>
    internal static class AtmosphericSurfaceMath
    {
        internal static float AdvanceWetness(float wetness, float rain, float seconds)
        {
            float from = WeatherMath.Clamp01(wetness), target = WeatherMath.Clamp01(rain);
            float amount = Math.Max(0f, seconds) / (target > from ? 35f : 180f);
            return target > from ? Math.Min(target, from + amount) : Math.Max(target, from - amount);
        }

        internal static float ColdTarget(float temperatureC, float recentLiquid)
            => WeatherMath.Smoothstep(0f, 10f, -temperatureC) * WeatherMath.Clamp01(recentLiquid) * 0.35f;
    }
}
