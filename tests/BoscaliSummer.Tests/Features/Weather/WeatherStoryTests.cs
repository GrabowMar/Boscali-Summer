using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Roles and stories (spec 5.1) and the front markers (5.3): every pool sky fits its segment's role and class, every class has a story, and Line(kind, d) is a thin symmetric overlay.</summary>
    internal static class WeatherStoryTests
    {
        public static void Run()
        {
            StoryTableMatchesTheWire();
            EverySegmentSkyFitsItsRoleAndClass();
            EveryClassHasAStory();
            LineIsASymmetricThinOverlay();
            BeadingShape();
        }

        private static void StoryTableMatchesTheWire()
        {
            TestAssert.Eq(WeatherKey.StoryCount, WeatherStories.Count, "the wire's story table is the real table (Stationary and Marine join after the bench review)");
            TestAssert.Eq(WeatherStories.Count, Enum.GetValues(typeof(StoryKind)).Length, "one StoryKind per story");
        }

        private static void EverySegmentSkyFitsItsRoleAndClass()
        {
            for (int s = 0; s < WeatherStories.Count; s++)
                foreach (SegmentDef seg in WeatherStories.Get(s).Segments)
                {
                    TestAssert.That(seg.MinKm <= seg.MaxKm && seg.Pool.Length > 0, WeatherStories.Name(s) + " " + seg.Label + " has a length range and a pool");
                    foreach (int number in seg.Pool)
                    {
                        SkyDef sky = SkyLibrary.Get(number - 1);
                        TestAssert.That((sky.Roles & seg.Role) != 0, WeatherStories.Name(s) + " " + seg.Label + ": sky " + number + " does not list the role " + seg.Role);
                        TestAssert.That((RoleRules.AllowedClasses(seg.Role) & (1 << (int)sky.Class)) != 0, "sky " + number + " class " + sky.Class + " is outside " + seg.Role);
                    }
                }
        }

        private static void EveryClassHasAStory()
        {
            foreach (WeatherRegimeType cls in (WeatherRegimeType[])Enum.GetValues(typeof(WeatherRegimeType)))
            {
                float best = 0f;
                for (int s = 0; s < WeatherStories.Count; s++) best = Math.Max(best, WeatherStories.Share(s, cls));
                TestAssert.That(best >= 0.15f, "class " + cls + " has a story with a share of at least 0.15 (best " + best.ToString("0.00") + ")");
            }
        }

        private static void LineIsASymmetricThinOverlay()
        {
            foreach (MarkerKind kind in (MarkerKind[])Enum.GetValues(typeof(MarkerKind)))
            {
                float peak = FrontLine.PeakRain(kind);
                float worstSlope = 0f, previous = FrontLine.Line(kind, -20000f).Rain;
                for (float d = -19950f; d <= 20000f; d += 50f)
                {
                    LineEffect e = FrontLine.Line(kind, d), mirror = FrontLine.Line(kind, -d);
                    TestAssert.That(Math.Abs(e.Rain - mirror.Rain) < 1e-4f * peak, kind + ": the rain core is symmetric");
                    worstSlope = Math.Max(worstSlope, Math.Abs(e.Rain - previous) / 50f * 45f);
                    previous = e.Rain;
                }
                TestAssert.That(worstSlope <= 0.3f, kind + ": the core's own slope at 45 m/s stays under 0.3 mm/h/s (got " + worstSlope.ToString("0.000") + ")");
                LineEffect far = FrontLine.Line(kind, 20000f), mid = FrontLine.Line(kind, 0f);
                TestAssert.That(far.Rain <= 0.15f * mid.Rain && far.Gust <= 0.15f * mid.Gust && far.VeerDegrees <= 0.15f * mid.VeerDegrees, kind + ": 20 km away at most 15% remains");
                TestAssert.That(FrontLine.Line(kind, FrontLine.Reach).Rain == 0f, kind + ": nothing beyond the reach");
            }
            TestAssert.That(FrontLine.PeakRain(MarkerKind.Squall) == 30f && FrontLine.Sigma(MarkerKind.Cold) == 2400f && FrontLine.PeakVeer(MarkerKind.Squall) == 50f, "the table of 5.3");
        }

        private static void BeadingShape()
        {
            TestAssert.That(FrontLine.BeadShape(-10f) == 0f && FrontLine.BeadShape(-30f) == 0f && FrontLine.BeadShape(30f) == 0f && FrontLine.BeadShape(60f) == 0f, "0 by -10 km and from +30 km");
            TestAssert.That(Math.Abs(FrontLine.BeadShape(0f) - 1f) < 1e-6f && Math.Abs(FrontLine.BeadShape(15f) - 1f) < 1e-6f && Math.Abs(FrontLine.BeadShape(7f) - 1f) < 1e-6f, "1 on [0, 15 km]");
            float worst = 0f;
            for (float d = -12f; d <= 32f; d += 0.01f) worst = Math.Max(worst, Math.Abs(FrontLine.BeadShape(d + 0.01f) - FrontLine.BeadShape(d)) / 0.01f);
            TestAssert.That(worst <= 0.1501f, "slope at most 0.15 per km (got " + worst.ToString("0.0000") + ")");
            TestAssert.That(Math.Abs(FrontLine.Bias(MarkerKind.Squall, 1000f) - 0.9f) < 1e-5f && FrontLine.Bias(MarkerKind.Warm, 1000f) == 0f, "bias = convectiveBias * shape(d)");
        }
    }
}
