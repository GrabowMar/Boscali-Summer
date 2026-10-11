using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Cloud streets (spec 6): the stretch and the rows (StreetMath), and the CPU twin of the shader making a street sky's bodies
    /// longer along the wind than across it.</summary>
    internal static class StreetTests
    {
        public static void Run()
        {
            StreetMathBehaves();
            BodiesAreLongerAlongTheWind();
        }

        private static void StreetMathBehaves()
        {
            TestAssert.That(StreetMath.Compress(0f) == 0f, "no streets, no stretch: the point is untouched");
            float previous = 0f;
            for (float s = 0.05f; s <= 1f; s += 0.05f)
            {
                float c = StreetMath.Compress(s);
                TestAssert.That(c > previous && c < 1f, "the stretch grows with Streets");
                TestAssert.That(Math.Abs(1f / (1f - c) - (1f + StreetMath.Stretch * s)) < 1e-4f, "features lengthen by 1 + k * Streets: " + 1f / (1f - c));
                previous = c;
            }
            for (float across = -9000f; across <= 9000f; across += 137f)
            {
                float rows = StreetMath.Rows(across, 3200f, 1.1f, 0.4f);
                TestAssert.That(rows >= 0f && rows <= 1f, "rows are 0..1");
                TestAssert.That(Math.Abs(rows - StreetMath.Rows(across + 3200f, 3200f, 1.1f, 0.4f)) < 1e-4f, "one row every spacing");
                float body = 0.5f;
                TestAssert.That(Math.Abs(StreetMath.Body(body, 0.8f, rows) - body) <= 0.8f * StreetMath.RowAmplitude * 0.5f + 1e-6f, "a row moves the body by at most Streets * 0.225");
                TestAssert.That(StreetMath.Body(body, 0f, rows) == body, "no streets, no rows");
            }
            TestAssert.That(StreetMath.Body(0.98f, 1f, 1f) <= 1f && StreetMath.Body(0.02f, 1f, 0f) >= 0f, "the body stays in 0..1");
        }

        /// <summary>The mean density change over a short step along the wind and across it, for a layer of bodies with the given Streets.</summary>
        private static float Anisotropy(byte[] noise, float streets, uint layoutSeed)
        {
            SkyParams sky = SkyLibrary.Nominal(9);
            sky.Streets = streets;
            var frame = new TapeFrame(40f, 0f, noise: new NoiseXform(layoutSeed));
            var bodies = new CloudBodies(noise, 64, TapeSampler.Constant(sky, LowGenus.Humilis), frame);
            var weather = new WeatherPoint { BackgroundCover = 0.95f, CloudBase = 1400f, LowTop = 2000f, CloudTop = 2000f, CellShape = 0.9f };
            float ux = frame.UX, uz = frame.UZ, wx = -frame.UZ, wz = frame.UX, d = 700f;
            double along = 0, across = 0;
            for (int i = 0; i < 400; i++)
            {
                float x = (i % 20) * 1300f - 13000f, z = (i / 20) * 1100f - 11000f, y = 1650f;
                float v = bodies.Density(weather, x, y, z, 0f);
                along += Math.Abs(bodies.Density(weather, x + ux * d, y, z + uz * d, 0f) - v);
                across += Math.Abs(bodies.Density(weather, x + wx * d, y, z + wz * d, 0f) - v);
            }
            return (float)(across / Math.Max(1e-6, along));
        }

        private static void BodiesAreLongerAlongTheWind()
        {
            byte[] noise = CloudNoise3D.Generate(64, 47);
            float round = 0f, streets = 0f;
            const int tapes = 6;
            for (uint seed = 1; seed <= tapes; seed++)
            {
                round += Anisotropy(noise, 0f, seed * 977u);
                streets += Anisotropy(noise, 0.8f, seed * 977u);
            }
            round /= tapes; streets /= tapes;
            TestAssert.That(round > 0.8f && round < 1.25f, "without streets the bodies are as long across the wind as along it: " + round);
            TestAssert.That(streets > 1.5f * round, "with streets they change much faster across the wind than along it: " + streets + " against " + round);
        }
    }
}
