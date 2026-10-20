using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The overcast look that replaces vanilla's greying above conditions 0.6 (spec 8.1): monotone in the cover, continuous across the top of a deck, bounded, identity when dry.</summary>
    internal static class OvercastLookTests
    {
        public static void Run()
        {
            ShadeIsMonotoneAndBounded();
            ShadeIsContinuousAcrossTheDeckTop();
            SunAndTintFollowTheShade();
            RealSkiesShadeAsTheirCoverSays();
            NightHazeGreysOnlyAfterSunset();
        }

        /// <summary>The cloud haze keeps vanilla's fog colour by day (HDR included), turns grey at night (the teal night fog drew a cyan band), and its darkening never stacks on a deck's shade.</summary>
        private static void NightHazeGreysOnlyAfterSunset()
        {
            float[] Haze(float height, float shade, float r, float g, float b)
            {
                OvercastLook.NightHaze(height, shade, ref r, ref g, ref b);
                return new[] { r, g, b };
            }
            // Vanilla's fog colours, live 2026-10-19: 13 km by day (HDR) and 00:30 (blue six times the red).
            float[] day = Haze(0.01f, 0f, 0.776f, 1.325f, 1.6f);
            TestAssert.That(day[0] == 0.776f && day[1] == 1.325f && day[2] == 1.6f, "the sun up leaves the haze alone, HDR unclipped");
            float[] night = Haze(-0.5f, 0f, 0.021f, 0.096f, 0.129f);
            TestAssert.That(Math.Abs(night[0] - night[2]) < 1e-6f && night[1] < 0.096f * (1f - OvercastLook.TintDark) + 1e-3f, "night haze is grey and darkened (" + night[0] + ", " + night[1] + ", " + night[2] + ")");
            float[] shaded = Haze(-0.5f, 1f, 0.05f, 0.05f, 0.05f);
            TestAssert.That(Math.Abs(shaded[0] - 0.05f) < 1e-6f, "under a full shade the fog is not darkened twice");
            float[] bad = Haze(float.NaN, 0f, 0.1f, 0.2f, 0.3f);
            TestAssert.That(bad[2] == 0.3f, "non-finite inputs leave the haze alone");
            float last = 0.129f;
            for (float h = 0.05f; h >= -0.2f; h -= 0.005f)
            {
                float blue = Haze(h, 0f, 0.021f, 0.096f, 0.129f)[2];
                TestAssert.That(blue <= last + 1e-6f && last - blue < 0.01f, "the night haze builds smoothly as the sun sets (height " + h + ")");
                last = blue;
            }
        }

        private static void ShadeIsMonotoneAndBounded()
        {
            float last = -1f;
            for (float cover = 0f; cover <= 1.0001f; cover += 0.01f)
            {
                float s = OvercastLook.Shade(cover, 500f, 2000f);
                TestAssert.That(s >= 0f && s <= 1f && s >= last - 1e-6f, "shade rises with cover and stays in 0-1 (cover " + cover + ")");
                last = s;
            }
            TestAssert.That(OvercastLook.Shade(0.09f, 0f, 3000f) == 0f && OvercastLook.Shade(1f, 0f, 3000f) == 1f, "no shade under cover 0.1, full shade under a full sky");
            TestAssert.That(OvercastLook.Shade(float.NaN, 0f, 3000f) == 0f && OvercastLook.Shade(1f, float.NaN, 3000f) == 0f, "non-finite inputs cast no shade");
            float lastAlt = 2f;
            for (float y = 0f; y <= 6000f; y += 10f)
            {
                float s = OvercastLook.Shade(1f, y, 3000f);
                TestAssert.That(s <= lastAlt + 1e-6f, "climbing never darkens (altitude " + y + ")");
                lastAlt = s;
            }
        }

        private static void ShadeIsContinuousAcrossTheDeckTop()
        {
            float worst = 0f, previous = OvercastLook.Shade(1f, 0f, 2500f);
            for (float y = 10f; y <= 6000f; y += 10f)
            {
                float s = OvercastLook.Shade(1f, y, 2500f);
                worst = Math.Max(worst, Math.Abs(s - previous));
                previous = s;
            }
            TestAssert.That(worst < 0.02f, "the shade changes by less than 0.02 per 10 m of climb through the deck top (worst " + worst + ")");
            TestAssert.That(OvercastLook.Shade(1f, 2501f + OvercastLook.DeckBlend, 2500f) < 0.001f && OvercastLook.Shade(1f, 2499f - OvercastLook.DeckBlend, 2500f) > 0.999f, "full below the top less the blend, none above the top plus it");
        }

        private static void SunAndTintFollowTheShade()
        {
            TestAssert.That(OvercastLook.SunScale(0f) == 1f && Math.Abs(OvercastLook.SunScale(1f) - OvercastLook.SunFloor) < 1e-6f && OvercastLook.SunScale(2f) == OvercastLook.SunScale(1f), "the sun is full in the open and keeps its floor under a full shade");
            float last = 2f;
            for (float k = 0f; k <= 1.0001f; k += 0.05f)
            {
                float s = OvercastLook.SunScale(k);
                TestAssert.That(s <= last + 1e-6f, "the sun dims smoothly with the shade");
                last = s;
            }
            float r = 0.6f, g = 0.7f, b = 0.9f;
            OvercastLook.Tint(0f, ref r, ref g, ref b);
            TestAssert.That(r == 0.6f && g == 0.7f && b == 0.9f, "no shade, no tint");
            OvercastLook.Tint(1f, ref r, ref g, ref b);
            float spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            TestAssert.That(spread < 0.3f * 0.6f && r < 0.6f + 0.1f && b < 0.9f, "a full shade greys and darkens the fog colour (spread " + spread + ")");
        }

        /// <summary>An altostratus opacus sky (33) shades a pilot under it; a cirrostratus veil (4) barely does; above the opaque deck there is no shade.</summary>
        private static void RealSkiesShadeAsTheirCoverSays()
        {
            WeatherPoint Centre(int sky)
            {
                var field = new WeatherField();
                field.Build(new WeatherKey(11u, 0f, false, (byte)SkyLibrary.ClassOf(sky), skyPin: (byte)sky), 600f, 40960f, 40960f);
                return field.Sample(0f, 0f);
            }
            WeatherPoint opaque = Centre(32), veil = Centre(3);
            TestAssert.That(OvercastLook.Shade(opaque.SkyCover, 1000f, opaque.NativeDeckTop) > 0.8f, "under an opaque altostratus the air is shaded");
            TestAssert.That(OvercastLook.Shade(veil.SkyCover, 1000f, veil.NativeDeckTop) < 0.3f, "under a cirrostratus veil the air is hardly shaded");
            TestAssert.That(OvercastLook.Shade(opaque.SkyCover, opaque.NativeDeckTop + 1000f, opaque.NativeDeckTop) < 0.01f, "above the deck the light is back");
        }
    }
}
