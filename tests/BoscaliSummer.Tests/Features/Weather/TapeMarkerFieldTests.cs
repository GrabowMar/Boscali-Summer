using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The marker rules through the field (spec 5.3): rain above 1 mm/h needs a closed sky unless a storm cell carries it, and a line is felt as gust and a QNH dip right at it.</summary>
    internal static class TapeMarkerFieldTests
    {
        public static void Run()
        {
            RainNeedsClosedSky();
            MarkerAddsNoCover();
            GustAndQnhFollowTheLine();
        }

        private static WeatherField AtLine(uint seed, byte story, int marker, float dKm, out float x, out float z)
        {
            var probe = new WeatherKey(seed, 0f, false, 1, story: story, headingDeg: 250f);
            WeatherTape tape = WeatherTape.For(probe);
            TapeMarker m = tape.Markers.Items[marker];
            // Put the theater centre on the line (D so that s(0, 0) = s_k + d), so a sample at the origin has distance d.
            float meander = tape.Frame(250f, 0f).Meander(0f);
            var key = new WeatherKey(seed, 0f, false, 1, story: story, headingDeg: 250f, offset: m.S + dKm * 1000f - meander);
            var field = new WeatherField();
            field.Build(key, 0f, 81920f, 40960f);
            x = 0f; z = 0f;
            return field;
        }

        private static int MarkerCount(uint seed, byte story) =>
            WeatherTape.For(new WeatherKey(seed, 0f, false, 1, story: story, headingDeg: 250f)).Markers.Count;

        /// <summary>Every marker of the tape (the 4 mm/h warm line, the 18 mm/h cold line, the 30 mm/h squall, the occluded line), not the first only.</summary>
        private static void RainNeedsClosedSky()
        {
            for (uint seed = 1; seed <= 40; seed++)
                foreach (byte story in new byte[] { 0, 1, 3 })
                    for (int marker = 0, n = MarkerCount(seed, story); marker < n; marker++)
                        for (float d = -14f; d <= 14f; d += 1f)
                        {
                            WeatherField f = AtLine(seed, story, marker, d, out float x, out float z);
                            WeatherPoint p = f.Sample(x, z);
                            if (p.ConvectiveShare > 0.01f) continue;      // storm-cell rain is never gated: the cell supplies its own cover
                            TestAssert.That(p.RainRate <= 1f || p.RainCover >= 0.33f, "stratiform rain above 1 mm/h needs RainCover >= 0.33 (seed " + seed + ", marker " + marker + ", d " + d + " km, rain " + p.RainRate + ", cover " + p.RainCover + ")");
                        }
        }

        /// <summary>A marker adds exactly zero cover (spec 5.3): at the line the background cover is the row's own sheet cover and the patch field, nothing else.</summary>
        private static void MarkerAddsNoCover()
        {
            for (uint seed = 1; seed <= 20; seed++)
                foreach (byte story in new byte[] { 0, 1, 3 })
                    for (int marker = 0, n = MarkerCount(seed, story); marker < n; marker++)
                        for (float d = -6f; d <= 6f; d += 2f)
                        {
                            WeatherField f = AtLine(seed, story, marker, d, out float x, out float z);
                            float sheet = f.RowAt(x, z).SheetCover, patch = f.Patch(x, z);
                            float expected = Math.Max(0f, Math.Min(1f, sheet + (patch - 0.5f) * (0.10f + 0.55f * sheet)));
                            TestAssert.That(Math.Abs(f.Sample(x, z).BackgroundCover - expected) < 1e-5f,
                                "a marker adds no cover: the background is the row's sheet and the patch field (seed " + seed + ", marker " + marker + ", d " + d + " km)");
                        }
        }

        private static void GustAndQnhFollowTheLine()
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                WeatherField at = AtLine(seed, 1, 0, 0f, out float x, out float z), far = AtLine(seed, 1, 0, 40f, out _, out _);
                WeatherTape tape = WeatherTape.For(new WeatherKey(seed, 0f, false, 1, story: 1, headingDeg: 250f));
                TapeMarker m = tape.Markers.Items[0];
                float peak = FrontLine.PeakGust(m.Kind) * m.Strength;
                TestAssert.That(at.Sample(x, z).Gust >= far.Sample(x, z).Gust + 0.4f * peak, "the line raises the gust where it stands");
                float qDip = FrontLine.QnhDipHpa * m.Strength;
                TestAssert.That(far.Sample(x, z).Qnh - at.Sample(x, z).Qnh > 0.3f * qDip - 1.1f, "and dips the pressure (patch noise is under 1 hPa)");
            }
        }
    }
}
