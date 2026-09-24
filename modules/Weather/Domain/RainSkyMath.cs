using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Pure response curves for the rain atmosphere layer: how much local rain thickens
    /// and greys the vanilla haze, dims ambient light, tints falling streaks, and how the
    /// runtime tells its own RenderSettings writes apart from vanilla's 1 Hz rewrite.
    /// Engine-agnostic; covered by RainSkyMathTests.
    /// </summary>
    internal static class RainSkyMath
    {
        /// <summary>Storm fog density relative to vanilla's; about half the visibility.</summary>
        public const float MaxFogMultiplier = 1.9f;

        /// <summary>Floor for ambient dimming so a storm never turns the cockpit black.</summary>
        public const float MinAmbientMultiplier = 0.78f;

        public static float FogMultiplier(float rain) => 1f + (MaxFogMultiplier - 1f) * Clamp01(rain);

        /// <summary>
        /// Pull the fog colour toward its own luminance (less saturated) and darken it a
        /// little, both scaled by rain. Identity when dry.
        /// </summary>
        public static void FogTint(float rain, ref float r, ref float g, ref float b)
        {
            float k = Clamp01(rain);
            if (k <= 0f) return;
            float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float grey = 0.35f * k;
            float dark = 1f - 0.14f * k;
            r = Clamp01((r + (lum - r) * grey) * dark);
            g = Clamp01((g + (lum - g) * grey) * dark);
            b = Clamp01((b + (lum - b) * grey) * dark);
        }

        public static float AmbientMultiplier(float rain) => 1f - (1f - MinAmbientMultiplier) * Clamp01(rain);

        /// <summary>
        /// True when a value read back equals the one we last wrote (float re-read noise
        /// tolerated); false means vanilla or someone else rewrote it since.
        /// </summary>
        public static bool IsOwnWrite(float written, float now)
        {
            float tolerance = Math.Max(1e-7f, Math.Abs(written) * 1e-4f);
            return Math.Abs(now - written) <= tolerance;
        }

        /// <summary>
        /// Falling-streak colour: the fog colour pulled toward pale water and scaled by
        /// light level, so streaks sit inside the haze instead of glowing white.
        /// </summary>
        public static void StreakColor(float fogR, float fogG, float fogB, float lightLevel,
            out float r, out float g, out float b)
        {
            float light = Clamp01(lightLevel);
            const float blend = 0.45f;
            r = (fogR + (0.80f - fogR) * blend) * light;
            g = (fogG + (0.86f - fogG) * blend) * light;
            b = (fogB + (0.95f - fogB) * blend) * light;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
