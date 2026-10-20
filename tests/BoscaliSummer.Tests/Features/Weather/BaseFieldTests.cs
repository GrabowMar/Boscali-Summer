using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The base-height field and the re-ranged cloud-base byte (spec 6): the field's range and slope, its place in the sample, the
    /// 16 m encoding at every site that reads it, and the map bounds widened by the variation.</summary>
    internal static class BaseFieldTests
    {
        public static void Run()
        {
            FieldRangeSlopeAndSpread();
            SampleAddsTheFieldToTheLocalBase();
            BaseByteRoundTrips();
            MapsCarryTheVariedBase();
            FarTexelsAreAreaMeans();
        }

        private static void FieldRangeSlopeAndSpread()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                double sum = 0, sum2 = 0; int n = 0;
                var noise = new BaseHeightField(seed * 7919u);
                float previous = noise.At(-200000f, 31000f), worstStep = 0f;
                for (float x = -200000f; x <= 200000f; x += 100f)
                {
                    float v = noise.At(x, 31000f);
                    TestAssert.That(v >= -1f && v <= 1f, "the unit field stays in [-1, 1]");
                    worstStep = Math.Max(worstStep, Math.Abs(v - previous));
                    previous = v; sum += v; sum2 += v * v; n++;
                }
                TestAssert.That(worstStep <= BaseHeightField.MaxSlope * 100f * 1.001f, "no steeper than the quintic bound: " + worstStep + " in 100 m");
                double mean = sum / n, sd = Math.Sqrt(sum2 / n - mean * mean);
                TestAssert.That(Math.Abs(mean) < 0.45 && sd > 0.2, "the field uses its range: mean " + mean + ", sd " + sd);
            }
            TestAssert.That(new BaseHeightField(5u).At(12345f, -6789f) == new BaseHeightField(5u).At(12345f, -6789f) &&
                new BaseHeightField(5u).At(12345f, -6789f) != new BaseHeightField(6u).At(12345f, -6789f), "seeded");
            // At 400 m of variation and 45 m/s of tape speed the base moves at most 3 m/s: far inside the 30 m/s cap of spec 9.6.
            TestAssert.That(BaseHeightField.MaxSlope * 400f * 45f < 3.5f, "the base-height field cannot pop: " + BaseHeightField.MaxSlope * 400f * 45f + " m/s");
        }

        private static void SampleAddsTheFieldToTheLocalBase()
        {
            var field = new WeatherField();
            field.Build(new WeatherKey(17u, 0f, false, skyPin: 7), 600f, 40960f, 40960f);
            float variation = field.RowAt(0f, 0f).BaseVariation;
            TestAssert.That(variation == 150f, "sky 8 is a Humilis: 150 m of variation");
            float low = float.MaxValue, high = float.MinValue;
            for (float z = -40000f; z <= 40000f; z += 2000f)
            for (float x = -40000f; x <= 40000f; x += 2000f)
            {
                WeatherPoint p = field.Sample(x, z);
                field.Frame.ToTape(x, z, out float qx, out float qz);
                float expected = field.RowAt(0f, 0f).CloudBase + variation * new BaseHeightField(field.Key.LayoutSeed).At(qx, qz);
                TestAssert.That(Math.Abs(p.CloudBase - expected) < 0.01f && p.BaseVariation == variation, "the base is the row's base plus the field");
                low = Math.Min(low, p.CloudBase); high = Math.Max(high, p.CloudBase);
            }
            TestAssert.That(high - low > 0.5f * variation && high - low <= 2f * variation + 0.01f, "the base undulates by about the variation: " + (high - low));
        }

        private static void BaseByteRoundTrips()
        {
            var structure = new byte[4];
            for (float baseM = 250f; baseM <= 3600f; baseM += 7f)
            {
                // 16-bit since 2026-10-18 (CloudMaps.Word): high byte in channel 2, low byte in channel 1. One byte was 16 m steps, which drew terraces.
                int word = (int)(Math.Min(1f, baseM / CloudMaps.BaseRange) * 65535f + 0.5f);
                var profiles = new byte[] { 0, (byte)(word & 255), (byte)(word >> 8), 0 };
                WeatherPoint decoded = CloudBodies.SampleMap(structure, profiles, 1, 0.5f, 0.5f);
                TestAssert.That(Math.Abs(decoded.CloudBase - baseM) <= 0.1f, "16-bit base: " + baseM + " came back as " + decoded.CloudBase);
            }
        }

        private static void FarTexelsAreAreaMeans()
        {
            // Spec 6: a far texel is the mean of 3 x 3 samples over its area. 256 texels, the shape channel (where the cumulus groups ride).
            var key = new WeatherKey(90210u, 0f, false, (byte)WeatherRegimeType.Scattered);
            const float farHalf = 220000f;
            CloudMaps maps = CloudMaps.Build(key, 600f, 40960f, 40960f, 12f, 50000f, farHalf);
            var field = new WeatherField();
            field.Build(key, 600f, 40960f, 40960f, 12f);
            float third = 2f * farHalf / CloudMaps.FarSize / 3f;
            int texels = 0, apart = 0;
            for (int z = 4; z < CloudMaps.FarSize; z += 8)
            for (int x = 4; x < CloudMaps.FarSize; x += 8)
            {
                float wx = ((x + 0.5f) / CloudMaps.FarSize * 2f - 1f) * farHalf, wz = ((z + 0.5f) / CloudMaps.FarSize * 2f - 1f) * farHalf;
                float sum = 0f;
                for (int k = 0; k < 9; k++) sum += field.Sample(wx + (k % 3 - 1) * third, wz + (k / 3 - 1) * third).CellShape;
                float stored = maps.Far[(z * CloudMaps.FarSize + x) * 4 + 2] / 255f;
                TestAssert.That(Math.Abs(stored - sum / 9f) <= 0.5f / 255f + 1e-4f, "the far texel is the mean of 3 x 3 samples: stored " + stored + ", mean " + sum / 9f);
                if (Math.Abs(stored - field.Sample(wx, wz).CellShape) > 0.04f) apart++;
                texels++;
            }
            TestAssert.That(texels == 256 && apart > 0, "and it is not just the centre sample: " + apart + " of " + texels + " texels differ from it by more than 0.04");
            // The near window keeps one sample per texel: its texels are about 0.8 km, finer than any map-carried group.
            float nx = ((100 + 0.5f) / CloudMaps.NearSize * 2f - 1f) * 50000f, nz = ((60 + 0.5f) / CloudMaps.NearSize * 2f - 1f) * 50000f;
            TestAssert.That(maps.Near[(60 * CloudMaps.NearSize + 100) * 4 + 2] == (byte)(Math.Max(0f, Math.Min(1f, field.Sample(nx, nz).CellShape)) * 255f + 0.5f),
                "a near texel is still the single sample at its centre");
        }

        private static void MapsCarryTheVariedBase()
        {
            // A flat-base Humilis at the largest variation (the ledge check): a one-keyframe fixture with 400 m of variation.
            var field = new WeatherField();
            var key = new WeatherKey(17u, 0f, false, skyPin: 7);
            field.Build(key, 600f, 40960f, 40960f);
            SkyParams sky = field.RowAt(0f, 0f);
            sky.BaseVariation = 400f;
            var tape = new BakedTape(1);
            tape.SetConstant(sky, LowGenus.Humilis);
            var fixture = new TapeFixture(tape, field.Frame);
            field.UseFixtureTape(fixture.Sampler, fixture.Frame);
            CloudMaps maps = CloudMaps.Build(key, 600f, 40960f, 40960f, 12f, 50000f, 220000f, fixture);
            float minBase = float.MaxValue, maxStep = 0f;
            WeatherPoint[] row = new WeatherPoint[CloudMaps.NearSize];
            for (int z = 0; z < CloudMaps.NearSize; z += 16)
            {
                for (int x = 0; x < CloudMaps.NearSize; x++)
                {
                    float u = (x + 0.5f) / CloudMaps.NearSize, v = (z + 0.5f) / CloudMaps.NearSize;
                    row[x] = CloudBodies.SampleMap(maps.Near, maps.NearProfiles, CloudMaps.NearSize, u, v);
                    WeatherPoint truth = field.Sample((u * 2f - 1f) * 50000f, (v * 2f - 1f) * 50000f);
                    TestAssert.That(Math.Abs(row[x].CloudBase - truth.CloudBase) <= CloudMaps.BaseRange / 255f * 0.51f, "the map stores the varied base to a 16 m step");
                    if (truth.LowCover > 0.02f) minBase = Math.Min(minBase, truth.CloudBase);
                    if (x > 0) maxStep = Math.Max(maxStep, Math.Abs(row[x].CloudBase - row[x - 1].CloudBase));
                }
            }
            // No ledges: neighbouring texels (one near texel is about 390 m here) differ by at most the field's slope times the spacing.
            float texel = 100000f / CloudMaps.NearSize;
            TestAssert.That(maxStep <= BaseHeightField.MaxSlope * 400f * texel + CloudMaps.BaseRange / 255f + 0.5f, "the underside has no step between texels: " + maxStep);
            TapeBounds.Compute(tape, 0f, TapeBounds.FarHalfWidth, out float boundsBottom, out float boundsTop);
            TestAssert.That(boundsBottom <= minBase + 1e-3f, "the altitude bounds are widened by the variation: " + boundsBottom + " vs lowest cloudy base " + minBase);
            // The envelope never claims a floor above a texel's own base.
            for (int z = 0; z < CloudMaps.EnvelopeSize; z += 8)
            for (int x = 0; x < CloudMaps.EnvelopeSize; x += 8)
            {
                int o = (z * CloudMaps.EnvelopeSize + x) * 4;
                if (maps.Envelope[o] == 0) continue;
                float floor = maps.Envelope[o + 2] / 255f * CloudMaps.VolumeTop;
                WeatherPoint own = CloudBodies.SampleMap(maps.Near, maps.NearProfiles, CloudMaps.NearSize,
                    (x * 2 + 1f) / CloudMaps.NearSize, (z * 2 + 1f) / CloudMaps.NearSize);
                TestAssert.That(floor <= own.CloudBase + CloudMaps.VolumeTop / 255f, "the envelope floor lies at or under the local base");
            }
        }
    }
}
