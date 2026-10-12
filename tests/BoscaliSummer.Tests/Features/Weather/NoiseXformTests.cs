using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The tape frame of slice 3 (spec 3.1, 6): tape-frame coordinates, the noise decorrelation per layer, and the low genus a
    /// baked tape carries per texel for the lattice.</summary>
    internal static class NoiseXformTests
    {
        public static void Run()
        {
            IdentityLeavesEveryTapAlone();
            RotationAndOffsetPerLayer();
            DependsOnSeedAndSaltOnly();
            TapeCoordinatesRoundTrip();
            GenusRidesWithTheTexels();
        }

        private static void IdentityLeavesEveryTapAlone()
        {
            var frame = new TapeFrame(40f, 0f);
            for (int layer = 0; layer < NoiseXform.Layers; layer++)
            {
                frame.NoiseXZ(1234.5f, -987.25f, layer, out float nx, out float nz);
                TestAssert.That(nx == 1234.5f && nz == -987.25f, "a frame built without a key reads the noise where the world is (layer " + layer + ")");
            }
        }

        private static void RotationAndOffsetPerLayer()
        {
            for (uint seed = 1; seed <= 40; seed++)
            {
                var xf = new NoiseXform(seed * 2654435761u);
                for (int layer = 0; layer < NoiseXform.Layers; layer++)
                {
                    TestAssert.That(Math.Abs(xf.Cos(layer) * xf.Cos(layer) + xf.Sin(layer) * xf.Sin(layer) - 1f) < 1e-5f, "a pure rotation");
                    TestAssert.That(xf.OffsetX(layer) >= 0f && xf.OffsetX(layer) < NoiseXform.MaxOffset &&
                        xf.OffsetZ(layer) >= 0f && xf.OffsetZ(layer) < NoiseXform.MaxOffset, "offsets stay in range");
                    xf.Apply(0f, 0f, layer, out float ox, out float oz);
                    xf.Apply(3000f, 4000f, layer, out float ax, out float az);
                    float length = (float)Math.Sqrt((ax - ox) * (ax - ox) + (az - oz) * (az - oz));
                    TestAssert.That(Math.Abs(length - 5000f) < 0.05f, "a rigid move: distances are kept, " + length);
                    if (layer >= NoiseXform.MidAligned)
                        TestAssert.That(xf.Cos(layer) == 1f && xf.Sin(layer) == 0f, "streak taps take the offset but never the rotation, so the stretch stays along the wind");
                }
                for (int a = 0; a < NoiseXform.Layers; a++)
                for (int b = a + 1; b < NoiseXform.Layers; b++)
                    TestAssert.That(xf.OffsetX(a) != xf.OffsetX(b) && xf.OffsetZ(a) != xf.OffsetZ(b), "every layer has its own offset");
                TestAssert.That(xf.StreetSpacing >= 2500f && xf.StreetSpacing <= 4000f, "street rows are 2.5-4 km apart");
            }
            var again = new NoiseXform(77u);
            var same = new NoiseXform(77u);
            for (int layer = 0; layer < NoiseXform.Layers; layer++)
                TestAssert.That(again.Cos(layer) == same.Cos(layer) && again.OffsetX(layer) == same.OffsetX(layer) && again.OffsetZ(layer) == same.OffsetZ(layer),
                    "deterministic");
        }

        private static void DependsOnSeedAndSaltOnly()
        {
            var key = new WeatherKey(5150u, 100f, true, (byte)WeatherRegimeType.Fair, layoutSalt: 2, headingDeg: 70f);
            WeatherKey moved = key.Rebased(900f, false).WithSets(5).WithAnchor(100f, 200f);
            TestAssert.That(moved.LayoutSeed == key.LayoutSeed, "Epoch, Offset, Dynamic, Sets and Anchor never move the noise");
            TestAssert.That(key.WithSkyPin(4).LayoutSeed == key.LayoutSeed,
                "a sky pin never moves the noise (they do change the tape: the lattice re-rolls)");
            TestAssert.That(key.WithSkyPin(4).TapeId != key.TapeId, "the lattice salt is the tape identity");
            TestAssert.That(key.WithLayoutSalt(3).LayoutSeed != key.LayoutSeed &&
                new WeatherKey(5151u, 100f, true, (byte)WeatherRegimeType.Fair, layoutSalt: 2, headingDeg: 70f).LayoutSeed != key.LayoutSeed,
                "another seed or another layout salt gives another noise");
        }

        private static void TapeCoordinatesRoundTrip()
        {
            var frame = new TapeFrame(250f, 1.5e6f, 12000f, 90000f, 0.8f);
            for (int i = -6; i <= 6; i++)
            for (int j = -6; j <= 6; j++)
            {
                float x = i * 13000f, z = j * 7000f;
                frame.ToTape(x, z, out float qx, out float qz);
                frame.FromTape(qx, qz, out float bx, out float bz);
                TestAssert.That(Math.Abs(bx - x) < 0.25f && Math.Abs(bz - z) < 0.25f, "to the tape frame and back");
                TestAssert.That(Math.Abs(frame.STape(qx, qz) - frame.S(x, z)) < 0.5f, "S of the tape point is S of the world point: D cancels");
            }
        }

        private static void GenusRidesWithTheTexels()
        {
            SkyParams a = SkyLibrary.Nominal(7), b = SkyLibrary.Nominal(45);
            BakedTape ramp = BakedTape.TwoKeyframes(a, b, 1500000f, 12000f, LowGenus.Humilis, LowGenus.Cumulonimbus);
            var sampler = new TapeSampler(ramp);
            TestAssert.That(sampler.GenusAt(1400000f) == LowGenus.Humilis && sampler.GenusAt(1600000f) == LowGenus.Cumulonimbus,
                "each side of the ramp keeps its own genus");
            TestAssert.That(sampler.GenusAt(1499000f) == LowGenus.Humilis && sampler.GenusAt(1501000f) == LowGenus.Cumulonimbus &&
                sampler.GenusAt(-5f) == LowGenus.Humilis && sampler.GenusAt(float.NaN) == LowGenus.Humilis && sampler.GenusAt(9e9f) == LowGenus.Cumulonimbus,
                "the genus changes at the ramp midpoint and clamps at the tape ends");
            var constant = new BakedTape(1);
            constant.SetConstant(a, LowGenus.Mediocris);
            int revision = constant.Revision;
            constant.SetConstant(a, LowGenus.Mediocris);
            TestAssert.That(constant.Revision == revision && constant.GenusAt(0) == LowGenus.Mediocris, "the same row and genus again is a no-op");
            constant.SetConstant(a, LowGenus.Congestus);
            TestAssert.That(constant.Revision == revision + 1 && TapeSampler.Constant(a, LowGenus.Congestus).GenusAt(123f) == LowGenus.Congestus,
                "a new genus alone is a change");
        }
    }
}
