using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    // Field/map contracts over the theater, rather than only hand-picked cell portraits.
    // Geometry diagnostics deliberately do not pretend to establish rendered appearance.
    internal static class CloudMapCoverageTests
    {
        public static void Run()
        {
            var rows = new List<object>();
            foreach (var extent in new[] { (25000f, 25000f), (60000f, 40000f), (120000f, 120000f) })
            for (uint seed = 1; seed <= 12; seed++)
            for (byte state = 0; state <= (byte)WeatherRegimeType.Storm; state++)
            {
                var key = new WeatherKey(seed, 0f, false, state);
                var field = new WeatherField();
                field.Build(key, 900f, extent.Item1, extent.Item2);
                var later = new WeatherField();
                later.Build(key, 1800f, extent.Item1, extent.Item2);
                float nearHalf = Math.Max(80000f, Math.Max(extent.Item1, extent.Item2) + 45000f);
                float farHalf = Math.Max(nearHalf * 3f, 240000f);
                float minimumCover = 1f, maximumCover = 0f, sumCover = 0f;
                float maximumRain = 0f, maximumTop = 0f, minimumMapWeight = 1f;
                int cloudy = 0, rainy = 0, n = 0;
                for (int iz = 0; iz <= 16; iz++)
                for (int ix = 0; ix <= 16; ix++)
                {
                    float x = (ix / 8f - 1f) * extent.Item1;
                    float z = (iz / 8f - 1f) * extent.Item2;
                    WeatherPoint p = field.Sample(x, z), q = later.Sample(x, z);
                    CloudBodies.MapWeights(x, z, nearHalf, farHalf, out float nearWeight, out float farFade);
                    TestAssert.That(nearWeight == 1f && farFade == 1f,
                        "All theater positions retain full near-map weight, including corners");
                    TestAssert.That(float.IsFinite(p.Cover) && p.Cover >= 0f && p.Cover <= 1f &&
                        float.IsFinite(p.RainRate) && p.RainRate >= 0f && p.RainRate <= WeatherField.MaxRainRate &&
                        float.IsFinite(p.CloudTop) && p.CloudTop >= p.CloudBase && p.LowTop >= p.CloudBase &&
                        p.CloudTop >= p.LowTop,
                        "Every sampled map column has finite physical coverage, precipitation and ordered ceilings");
                    TestAssert.That(p.Cover == q.Cover && p.CellShape == q.CellShape && p.LowTop == q.LowTop &&
                        p.FrontBase == q.FrontBase && p.FrontTop == q.FrontTop && p.RainRate == q.RainRate,
                        "A settled held state retains the same cloud geometry and rain across mission time");
                    minimumCover = Math.Min(minimumCover, p.Cover);
                    maximumCover = Math.Max(maximumCover, p.Cover);
                    sumCover += p.Cover;
                    maximumRain = Math.Max(maximumRain, p.RainRate);
                    maximumTop = Math.Max(maximumTop, p.CloudTop);
                    minimumMapWeight = Math.Min(minimumMapWeight, nearWeight);
                    if (p.Cover > 0.5f) cloudy++;
                    if (p.RainRate > 1f) rainy++;
                    n++;
                }
                float farthestHeroCenter = 0f;
                float farthestCameraX = 0f, farthestCameraZ = 0f, farthestHeroX = 0f, farthestHeroZ = 0f;
                string farthestHeroKind = "None";
                int heroCenterViewsPast220km = 0;
                for (int hero = 0; hero < field.SuperstructureCount; hero++)
                {
                    Superstructure s = field.SuperstructureAt(hero);
                    foreach (float x in new[] { -extent.Item1, extent.Item1 })
                    foreach (float z in new[] { -extent.Item2, extent.Item2 })
                    {
                        float distance = MathF.Sqrt((s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z));
                        if (distance > farthestHeroCenter)
                        {
                            farthestHeroCenter = distance; farthestCameraX = x; farthestCameraZ = z;
                            farthestHeroX = s.X; farthestHeroZ = s.Z; farthestHeroKind = s.Kind.ToString();
                        }
                        if (distance > 220000f) heroCenterViewsPast220km++;
                    }
                }
                rows.Add(new { seed, state = ((WeatherRegimeType)state).ToString(),
                    halfX = extent.Item1, halfZ = extent.Item2, samples = n,
                    minimumCover, maximumCover, meanCover = sumCover / n,
                    cloudyShare = cloudy / (float)n, rainyShare = rainy / (float)n,
                    maximumRain, maximumTop, minimumMapWeight, cells = field.CellCount,
                    clusters = field.CloudClusterCount, heroes = field.SuperstructureCount,
                    farthestHeroCenter, farthestCameraX, farthestCameraZ, farthestHeroX, farthestHeroZ,
                    farthestHeroKind, heroCenterViewsPast220km });
            }
            EncodedBoundaryContinuity();
            string evidence = Environment.GetEnvironmentVariable("CLOUD_MAP_COVERAGE_JSON");
            if (!string.IsNullOrEmpty(evidence))
                File.WriteAllText(evidence, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void EncodedBoundaryContinuity()
        {
            const float nearHalf = 105000f, farHalf = 315000f;
            foreach (WeatherRegimeType state in new[] { WeatherRegimeType.Scattered, WeatherRegimeType.RainSquall, WeatherRegimeType.Storm })
            {
                CloudMaps maps = CloudMaps.Build(new WeatherKey(90210u, 0f, false, (byte)state),
                    900f, 60000f, 60000f, 13f, nearHalf, farHalf);
                foreach (float boundary in new[] { nearHalf * 0.92f, nearHalf, farHalf * 0.84f, farHalf })
                foreach (float along in new[] { -55000f, 0f, 55000f })
                foreach (float sign in new[] { -1f, 1f })
                {
                    WeatherPoint a = Displayed(maps, boundary * sign - 1f, along, nearHalf, farHalf);
                    WeatherPoint b = Displayed(maps, boundary * sign + 1f, along, nearHalf, farHalf);
                    TestAssert.That(Math.Abs(a.BackgroundCover - b.BackgroundCover) < 0.001f &&
                        Math.Abs(a.FrontCover - b.FrontCover) < 0.001f && Math.Abs(a.CellShape - b.CellShape) < 0.001f,
                        "Near/far onset and map boundaries have no cover step over a two-metre camera movement");
                    TestAssert.That(Math.Abs(a.CloudBase - b.CloudBase) < 10f && Math.Abs(a.LowTop - b.LowTop) < 10f,
                        "The map transition fades coverage without shifting the cloud ceiling abruptly");
                }
                WeatherPoint outside = Displayed(maps, farHalf + 1f, 0f, nearHalf, farHalf);
                TestAssert.That(outside.BackgroundCover == 0f && outside.FrontCover == 0f && outside.CellShape == 0f,
                    "The outer ordinary map is cleanly absent while independent heroes may still render");
            }
        }

        private static WeatherPoint Displayed(CloudMaps maps, float x, float z, float nearHalf, float farHalf)
        {
            CloudBodies.MapWeights(x, z, nearHalf, farHalf, out float nearWeight, out float farFade);
            WeatherPoint far = CloudBodies.SampleMap(maps.Far, maps.FarProfiles, CloudMaps.FarSize,
                x / (farHalf * 2f) + 0.5f, z / (farHalf * 2f) + 0.5f);
            far.BackgroundCover *= farFade; far.FrontCover *= farFade; far.CellShape *= farFade;
            if (nearWeight <= 0f) return far;
            WeatherPoint near = CloudBodies.SampleMap(maps.Near, maps.NearProfiles, CloudMaps.NearSize,
                x / (nearHalf * 2f) + 0.5f, z / (nearHalf * 2f) + 0.5f);
            return CloudBodies.BlendMaps(far, near, nearWeight);
        }
    }
}
