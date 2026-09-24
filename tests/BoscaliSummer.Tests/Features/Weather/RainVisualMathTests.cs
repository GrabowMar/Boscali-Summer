using System;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class RainVisualMathTests
    {
        public static void Run()
        {
            TestAssert.That(RainVisualMath.BelowCloudFactor(1000f, 2000f) == 1f, "Below deck receives rain");
            TestAssert.That(RainVisualMath.BelowCloudFactor(2400f, 2000f) == 0f, "Above deck must be dry");
            TestAssert.That(RainVisualMath.BelowCloudFactor(2200f, 2000f) == 0.5f, "Cloud exit fades gradually");
            TestAssert.That(RainVisualMath.BelowCloudFactor(200f, 0f) == RainVisualMath.BelowCloudFactor(2200f, 2000f),
                "A common coordinate shift must not change cloud exposure");
            // 1. Emission rate preserves density across speeds, scales with density/gust
            TestAssert.That(RainVisualMath.EmissionRate(100f, 0f, 1f, 1f) == 0f, "Dry rate must be 0");
            TestAssert.That(RainVisualMath.EmissionRate(0f, 1f, 1f, 1f) == 350f, "Hover rate must be 350/s");
            TestAssert.That(RainVisualMath.EmissionRate(250f, 1f, 1f, 1f) == 1600f, "Fast rate must be 1600/s");
            TestAssert.That(RainVisualMath.EmissionRate(500f, 1f, 1f, 1f) == 1600f, "Overspeed must clamp to fast rate");
            TestAssert.That(RainVisualMath.EmissionRate(0f, 1f, 2f, 1f) == 700f, "Density must scale rate");
            TestAssert.That(RainVisualMath.EmissionRate(0f, 1f, 1f, 1.25f) == 437.5f, "Gust must scale rate");
            TestAssert.That(RainVisualMath.EmissionRate(0f, -1f, 1f, 1f) == 0f, "Negative intensity must clamp");

            // 2. Streak alpha: monotonic, bounded under bloom
            TestAssert.That(RainVisualMath.StreakAlpha(0f) == 0f, "Dry alpha must be 0");
            TestAssert.That(Math.Abs(RainVisualMath.StreakAlpha(1f) - 0.38f) < 0.0001f, "Storm alpha must be 0.38");
            float half = RainVisualMath.StreakAlpha(0.5f);
            TestAssert.That(half > RainVisualMath.StreakAlpha(0.25f) && half < RainVisualMath.StreakAlpha(1f),
                "Alpha must rise monotonically");
            TestAssert.That(RainVisualMath.StreakAlpha(2f) == RainVisualMath.StreakAlpha(1f), "Alpha must clamp high");

            // 3. Gust factor: bounded, deterministic, breathing
            for (float t = 0f; t < 30f; t += 0.5f)
            {
                float g = RainVisualMath.GustFactor(t, 1337);
                TestAssert.That(g >= 0.75f && g <= 1.25f, "Gust must stay in [0.75, 1.25]");
            }
            TestAssert.That(RainVisualMath.GustFactor(7.5f, 1337) == RainVisualMath.GustFactor(7.5f, 1337),
                "Gust must be deterministic");
            float min = float.MaxValue;
            float max = float.MinValue;
            for (float t = 0f; t < 20f; t += 0.25f)
            {
                float g = RainVisualMath.GustFactor(t, 1337);
                if (g < min) min = g;
                if (g > max) max = g;
            }
            TestAssert.That(max - min > 0.05f, "Gust must breathe over time");

            // 4. Budget clamp: worst case (hover, max density, peak gust) fits 1000
            float worstRate = RainVisualMath.EmissionRate(0f, 1f, 2f, 1.25f);
            float hoverLifetime = 16f / 6f;
            float clamped = RainVisualMath.ClampRateToBudget(worstRate, hoverLifetime, RainVisualMath.MaxParticles);
            TestAssert.That(RainVisualMath.AliveEstimate(clamped, hoverLifetime) <= RainVisualMath.MaxParticles + 0.01f,
                "Clamped worst case must fit the particle budget");
            TestAssert.That(RainVisualMath.ClampRateToBudget(100f, 1f, 1000) == 100f, "Under-budget rate passes through");
            TestAssert.That(RainVisualMath.ClampRateToBudget(5000f, 0f, 1000) == 5000f, "Degenerate lifetime skips clamp");

            // 5. Cockpit muffle cutoff
            TestAssert.That(RainAudioMath.MuffleCutoff(false, 0f) == 20000f, "External must stay wide open");
            TestAssert.That(RainAudioMath.MuffleCutoff(false, 1f) == 20000f, "External stays open at speed");
            TestAssert.That(RainAudioMath.MuffleCutoff(true, 0f) == 900f, "Slow cockpit must dull to 900 Hz");
            TestAssert.That(RainAudioMath.MuffleCutoff(true, 1f) == 2600f, "Fast cockpit must open to 2600 Hz");
        }
    }
}
