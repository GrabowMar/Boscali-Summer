using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class CloudMorphologyTests
    {
        public static void Run()
        {
            ShallowClusterBelowShield();
            SeparateMapCeilings();
            StaticFrontGeometry();
            FrontFamiliesAndFixedSites();
            LocalProfileContinuity();
            DominantBodyHeight();
            EncodedMapParity();
        }

        private static void ShallowClusterBelowShield()
        {
            var noise = new byte[4 * 4 * 4 * 4];
            Array.Fill(noise, (byte)255);
            var sky = new StateParams { CloudBase = 1400f, PuffScale = 4600f, PuffDepth = 2000f,
                LayerDepth = 1200f, BaseSharp = 65f, BaseWobble = 80f, Dome = 1.35f,
                Billow = 0.75f, Anvil = 0.9f, LayerSmooth = 0.15f };
            var bodies = new CloudBodies(noise, 4, sky);
            var p = new WeatherPoint { CloudBase = 1400f, LowTop = 3500f, CloudTop = 10000f,
                CellShape = 0.8f, FrontCover = 0.65f, FrontBase = 6500f, FrontTop = 10000f };
            float between = bodies.Density(p, 0f, 5000f, 0f, 0f);
            TestAssert.That(between < 0.001f,
                $"A shallow ordinary cluster must not borrow the high front shield's ceiling: {between}");
            TestAssert.That(bodies.Density(p, 0f, 2100f, 0f, 0f) > 0.2f,
                "Ceiling separation retains the shallow cluster's dense body");
            TestAssert.That(bodies.Density(p, 0f, 7500f, 0f, 0f) > 0.2f,
                "The independent high frontal shield remains visible");
        }

        private static void SeparateMapCeilings()
        {
            var structure = new byte[] { 0, 200, 220, 48 };
            var profiles = new byte[] { 105, 160, 22, 0 };
            WeatherPoint p = CloudBodies.SampleMap(structure, profiles, 1, 0.5f, 0.5f);
            TestAssert.That(Math.Abs(p.LowTop - 48f / 255f * 16000f) < 0.01f,
                "Structure alpha transports only the low/tower ceiling");
            TestAssert.That(p.CloudTop > p.LowTop && p.FrontTop > p.LowTop,
                "CPU map decode retains independent front and aggregate ceilings");
            WeatherPoint blend = CloudBodies.BlendMaps(new WeatherPoint { LowTop = 1400f, CloudTop = 1400f }, p, 0.5f);
            TestAssert.That(Math.Abs(blend.LowTop - (1400f + p.LowTop) * 0.5f) < 0.01f,
                "Displayed-map fade carries ordinary ceiling without borrowing front height");
        }

        private static void StaticFrontGeometry()
        {
            const uint layout = 99;
            SkySplit bare = SkySplit.From(layout, 0f, 40000f, 40000f, 130f);
            SkySplit visible = SkySplit.From(layout, 0.6f, 40000f, 40000f, 130f);
            var sky = new StateParams { Frontal = 1f };
            var a = new FrontState[2]; var b = new FrontState[2];
            int na = WeatherFronts.Fill(a, layout, sky, 40000f, 40000f, 130f, bare);
            int nb = WeatherFronts.Fill(b, layout, sky, 40000f, 40000f, 130f, visible);
            TestAssert.That(na == nb && na > 0, "Comparison retains the same front identities");
            for (int i = 0; i < na; i++)
                TestAssert.That(a[i].NormalX == b[i].NormalX && a[i].NormalZ == b[i].NormalZ &&
                    a[i].Offset == b[i].Offset && a[i].MeanderAmplitude == b[i].MeanderAmplitude &&
                    a[i].MeanderWavelength == b[i].MeanderWavelength && a[i].MeanderPhase == b[i].MeanderPhase,
                    "Strength/sky-split amount cannot relocate a seeded front");
        }

        private static void FrontFamiliesAndFixedSites()
        {
            for (uint seed = 1; seed <= 12; seed++)
            {
                var storm = new WeatherField(); var scattered = new WeatherField();
                storm.Build(new WeatherKey(seed, 0f, false, (byte)WeatherRegimeType.Storm), 600f, 40000f, 40000f);
                scattered.Build(new WeatherKey(seed, 0f, false, (byte)WeatherRegimeType.Scattered), 300f, 40000f, 40000f);
                for (int i = 0; i < storm.CellCount; i++)
                {
                    StormCell cell = storm.Cell(i);
                    if (cell.Cluster < 2)
                        TestAssert.That(Math.Abs(storm.Split.SignedDistance(cell.X, cell.Z)) < 6500f,
                            "Front-attached cell families follow the seeded curved frontal spine");
                    for (int j = 0; j < scattered.CellCount; j++)
                    {
                        StormCell early = scattered.Cell(j);
                        if (early.Slot == cell.Slot)
                            TestAssert.That(cell.X == early.X && cell.Z == early.Z,
                                "A state/settle-time change never relocates an active cell site");
                    }
                }
                WeatherMath.HeadingToVector(storm.PrevailingHeading, out float wx, out float wz);
                for (int i = 0; i < scattered.CloudClusterCount; i++)
                {
                    DryCloudCluster group = scattered.CloudCluster(i);
                    TestAssert.That(group.AxisX * wx + group.AxisZ * wz > 0.94f,
                        "Ordinary cumulus streets align with fixed prevailing wind with bounded irregularity");
                }
            }
        }

        private static void LocalProfileContinuity()
        {
            TestAssert.That(CloudShape.LocalAnvil(1800f, 0.9f) == 0f &&
                Math.Abs(CloudShape.LocalAnvil(7000f, 0.9f) - 0.9f) < 0.0001f,
                "Shallow puffs have no anvil while deep cells retain their developed cap");
            float previous = 0f;
            for (int depth = 0; depth <= 9000; depth += 25)
            {
                float anvil = CloudShape.LocalAnvil(depth, 0.9f);
                TestAssert.That(anvil >= previous && anvil - previous < 0.009f,
                    "Local genus develops continuously with column depth");
                TestAssert.That(CloudShape.LocalDome(1.35f, anvil) >= 1f &&
                    CloudShape.LocalDome(1.35f, anvil) <= 1.35f,
                    "Pinched stem interpolation remains within the two physical profiles");
                previous = anvil;
            }
            for (int state = 0; state <= 6; state++)
            {
                var field = new WeatherField();
                field.Build(new WeatherKey(73, 0f, false, (byte)state), 600f, 40000f, 40000f);
                for (int z = -35000; z <= 35000; z += 5000)
                for (int x = -35000; x <= 35000; x += 5000)
                {
                    WeatherPoint p = field.Sample(x, z);
                    TestAssert.That(p.LowTop >= p.CloudBase && p.CloudTop >= p.LowTop,
                        "Every state retains truthful aggregate and separate ordinary ceilings");
                }
            }
        }

        private static void DominantBodyHeight()
        {
            var noise = new byte[4 * 4 * 4 * 4]; Array.Fill(noise, (byte)255);
            var sky = new StateParams { PuffScale = 4600f, PuffDepth = 2000f, LayerDepth = 1200f,
                Dome = 1.35f, Anvil = 0.9f, BaseSharp = 65f, BaseWobble = 80f };
            var bodies = new CloudBodies(noise, 4, sky);
            var p = new WeatherPoint { CloudBase = 1400f, LowTop = 3500f, CloudTop = 10000f,
                CellShape = 0.8f, FrontCover = 0.65f, FrontBase = 6500f, FrontTop = 10000f };
            TestAssert.That(bodies.Density(p, 0f, 2100f, 0f, 0f, out float lower) > 0.2f && lower > 0.2f && lower < 0.5f,
                "A shallow cluster lights in its own height interval, independently of the high shield");
            TestAssert.That(bodies.Density(p, 0f, 7500f, 0f, 0f, out float upper) > 0.2f && upper > 0.1f && upper < 0.5f,
                "The dominant high front lights from its own local underside and crown");
            TestAssert.That(bodies.Density(default, 0f, 5000f, 0f, 0f, out float empty) == 0f && empty == 0f,
                "An empty density sample clears the lighting height");
            var rainy = new WeatherPoint { CloudBase = 2200f, LowTop = 4200f, CloudTop = 4200f,
                BackgroundCover = 0.7f, RainRate = 40f };
            TestAssert.That(bodies.Density(rainy, 0f, 1700f, 0f, 0f, out float scud) > 0.2f &&
                Math.Abs(scud - 2f / 7f) < 0.001f, "Under-base scud uses its own 700 m height band");
        }

        private static void EncodedMapParity()
        {
            foreach (WeatherRegimeType state in new[] { WeatherRegimeType.Clear, WeatherRegimeType.Storm })
            {
                var key = new WeatherKey(73, 0f, false, (byte)state);
                CloudMaps maps = CloudMaps.Build(key, 600f, 40000f, 40000f, 12f, 50000f, 220000f);
                byte maxRain = 0;
                foreach (byte[] profiles in new[] { maps.NearProfiles, maps.FarProfiles })
                    for (int i = 3; i < profiles.Length; i += 4) maxRain = Math.Max(maxRain, profiles[i]);
                TestAssert.That(maps.RainMaximum == maxRain / 255f,
                    "Rain gate maximum is exactly the displayed encoded profile alpha at both map levels");
                TestAssert.That(state == WeatherRegimeType.Clear ? maps.RainMaximum == 0f : maps.RainMaximum > 0.02f,
                    "Dry maps suppress rain shafts and storm maps retain an audible/visible rain source");
                var field = new WeatherField(); field.Build(key, 600f, 40000f, 40000f, 12f);
                for (int z = 16; z < CloudMaps.NearSize; z += 32)
                for (int x = 16; x < CloudMaps.NearSize; x += 32)
                {
                    float u = (x + 0.5f) / CloudMaps.NearSize, v = (z + 0.5f) / CloudMaps.NearSize;
                    WeatherPoint actual = CloudBodies.SampleMap(maps.Near, maps.NearProfiles, CloudMaps.NearSize, u, v);
                    WeatherPoint truth = field.Sample((u * 2f - 1f) * 50000f, (v * 2f - 1f) * 50000f);
                    CloudGenus genus = CloudShape.Resolve(field.Params);
                    float lowTop = Math.Max(truth.LowTop, truth.CloudBase + Math.Max(genus.PuffDepth,
                        field.Params.LayerDepth) + genus.BaseWobble * 0.5f);
                    TestAssert.That(Math.Abs(actual.LowTop - lowTop) <= 16000f / 255f * 0.51f &&
                        Math.Abs(actual.FrontTop - truth.FrontTop) <= 16000f / 255f * 0.51f,
                        "Packed ordinary/front ceilings mirror field truth within half a height texel");
                }
            }
        }
    }
}
