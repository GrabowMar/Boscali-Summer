using System;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class RainSkyMathTests
    {
        public static void Run()
        {
            // 1. Fog density multiplier: identity when dry, monotonic, bounded.
            TestAssert.That(RainSkyMath.FogMultiplier(0f) == 1f, "Dry fog multiplier must be identity");
            TestAssert.That(RainSkyMath.FogMultiplier(-1f) == 1f, "Negative rain must clamp to identity");
            float light = RainSkyMath.FogMultiplier(0.4f);
            float heavy = RainSkyMath.FogMultiplier(1f);
            TestAssert.That(light > 1f && heavy > light, "Fog must thicken monotonically with rain");
            TestAssert.That(heavy <= RainSkyMath.MaxFogMultiplier + 0.0001f, "Storm fog must respect the ceiling");
            TestAssert.That(RainSkyMath.FogMultiplier(2f) == heavy, "Rain above 1 must clamp");

            // 2. Fog tint: pulls toward its own grey, never brightens, identity when dry.
            float r = 0.60f, g = 0.70f, b = 0.90f;
            RainSkyMath.FogTint(0f, ref r, ref g, ref b);
            TestAssert.That(r == 0.60f && g == 0.70f && b == 0.90f, "Dry tint must leave fog colour untouched");
            RainSkyMath.FogTint(1f, ref r, ref g, ref b);
            float lumBefore = 0.2126f * 0.60f + 0.7152f * 0.70f + 0.0722f * 0.90f;
            float lumAfter = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            TestAssert.That(lumAfter < lumBefore, "Storm fog must be darker than clear haze");
            TestAssert.That(lumAfter > lumBefore * 0.6f, "Storm fog must not black out the horizon");
            TestAssert.That((b - r) < (0.90f - 0.60f), "Storm fog must be less saturated than clear haze");
            TestAssert.That(r >= 0f && g >= 0f && b >= 0f && r <= 1f && g <= 1f && b <= 1f, "Tint must stay in range");

            // 3. Ambient dimming: bounded so storms never go pitch black.
            TestAssert.That(RainSkyMath.AmbientMultiplier(0f) == 1f, "Dry ambient must be identity");
            float dim = RainSkyMath.AmbientMultiplier(1f);
            TestAssert.That(dim < 1f && dim >= RainSkyMath.MinAmbientMultiplier - 0.0001f, "Storm ambient dims within bounds");
            TestAssert.That(RainSkyMath.AmbientMultiplier(0.5f) > dim, "Ambient dimming must be monotonic");

            // 4. Change detection: our own write is recognised, a foreign write is not.
            TestAssert.That(RainSkyMath.IsOwnWrite(0.00042f, 0.00042f), "Exact re-read of our value is our own write");
            TestAssert.That(RainSkyMath.IsOwnWrite(0.00042f, 0.00042f + 1e-9f), "Float noise must not look like a vanilla write");
            TestAssert.That(!RainSkyMath.IsOwnWrite(0.00042f, 0.00050f), "A vanilla rewrite must be detected");
            TestAssert.That(!RainSkyMath.IsOwnWrite(0.00042f, 0.05f), "Underwater fog must be detected as foreign");

            // 5. Streak tint: blends fog colour toward pale water, scaled by light level.
            RainSkyMath.StreakColor(0.5f, 0.5f, 0.5f, 1f, out float sr, out float sg, out float sb);
            TestAssert.That(sr > 0.5f && sb >= sr, "Streaks must be paler than the fog and cool");
            RainSkyMath.StreakColor(0.5f, 0.5f, 0.5f, 0f, out float nr, out float ng, out float nb);
            TestAssert.That(nr == 0f && ng == 0f && nb == 0f, "No light means invisible streaks");
            RainSkyMath.StreakColor(0.5f, 0.5f, 0.5f, 5f, out float cr, out float cg, out float cb);
            TestAssert.That(cr <= 1f && cg <= 1f && cb <= 1f, "Light level must clamp");
        }
    }
}
