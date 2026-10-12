using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class CloudMorphologyTests
    {
        public static void Run()
        {
            ShallowClusterKeepsOwnCeiling();
            SeparateMapCeilings();
            FixedSites();
            LocalProfileContinuity();
            DominantBodyHeight();
            EncodedMapParity();
        }

        private static void ShallowClusterKeepsOwnCeiling()
        {
            var noise = new byte[4 * 4 * 4 * 4];
            Array.Fill(noise, (byte)255);
            var sky = new SkyParams { CloudBase = 1400f, LobeRung = 5f, PuffDepth = 2000f,
                DeckDepth = 1200f, BaseSharp = 65f, BaseWobble = 80f, Dome = 1.35f,
                Billow = 0.75f, Anvil = 0.9f, DeckSmooth = 0.15f };
            var bodies = new CloudBodies(noise, 4, sky);
            var p = new WeatherPoint { CloudBase = 1400f, LowTop = 3500f, CloudTop = 10000f, CellShape = 0.8f };
            float between = bodies.Density(p, 0f, 5000f, 0f, 0f);
            TestAssert.That(between < 0.001f,
                $"A shallow ordinary cluster must not borrow the aggregate ceiling: {between}");
            TestAssert.That(bodies.Density(p, 0f, 2100f, 0f, 0f) > 0.2f,
                "Ceiling separation retains the shallow cluster's dense body");
        }

        private static void SeparateMapCeilings()
        {
            var structure = new byte[] { 0, 0, 220, 48 };
            var profiles = new byte[] { 0, 0, 86, 0 };
            WeatherPoint p = CloudBodies.SampleMap(structure, profiles, 1, 0.5f, 0.5f);
            TestAssert.That(Math.Abs(p.LowTop - 48f / 255f * 16000f) < 0.01f,
                "Structure alpha transports only the low/tower ceiling");
            TestAssert.That(p.CloudTop >= p.LowTop && Math.Abs(p.CloudBase - 86f / 255f * CloudMaps.BaseRange) < 0.01f,
                "CPU map decode retains the aggregate ceiling and the 4096 m cloud base");
            WeatherPoint blend = CloudBodies.BlendMaps(new WeatherPoint { LowTop = 1400f, CloudTop = 1400f }, p, 0.5f);
            TestAssert.That(Math.Abs(blend.LowTop - (1400f + p.LowTop) * 0.5f) < 0.01f,
                "Displayed-map fade carries the ordinary ceiling");
        }

        private static void FixedSites()
        {
            for (uint seed = 1; seed <= 12; seed++)
            {
                // The lattice belongs to the tape, not to the state: two moments of one key hold the same candidates in the same places.
                var key = new WeatherKey(seed, 0f, true, (byte)WeatherRegimeType.Scattered);
                var early = new WeatherField(); var late = new WeatherField();
                early.Build(key, 100f, 40000f, 40000f);
                late.Build(key, 4000f, 40000f, 40000f);
                int compared = 0;
                for (int gz = -3; gz <= 3; gz++)
                for (int gx = -3; gx <= 3; gx++)
                for (int k = 0; k < 3; k++)
                {
                    bool a = StormLattice.Resolve(early.Lattice, gx, gz, k, out StormCell first);
                    bool b = StormLattice.Resolve(late.Lattice, gx, gz, k, out StormCell second);
                    TestAssert.That(a == b, "A state/settle-time change never adds or removes a candidate");
                    if (!a) continue;
                    TestAssert.That(first.X == second.X && first.Z == second.Z && first.Radius == second.Radius,
                        "A state/settle-time change never relocates a cell site");
                    compared++;
                }
                TestAssert.That(compared > 20, "the comparison saw candidates: " + compared);
                var scattered = new WeatherField();
                scattered.Build(new WeatherKey(seed, 0f, false, (byte)WeatherRegimeType.Scattered), 300f, 40000f, 40000f);
                WeatherMath.HeadingToVector(scattered.PrevailingHeading, out float wx, out float wz);
                for (int cell = -2; cell <= 2; cell++)
                {
                    int n = CumulusLattice.Fetch(scattered.Lattice, null, cell, cell / 2, out CumulusGroup[] groups);
                    for (int i = 0; i < n; i++)
                        TestAssert.That(Math.Abs(groups[i].AxisX * wx + groups[i].AxisZ * wz) > 0.94f,
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
            var sky = new SkyParams { LobeRung = 5f, PuffDepth = 2000f, DeckDepth = 1200f,
                Dome = 1.35f, Anvil = 0.9f, BaseSharp = 65f, BaseWobble = 80f };
            var bodies = new CloudBodies(noise, 4, sky);
            var p = new WeatherPoint { CloudBase = 1400f, LowTop = 3500f, CloudTop = 10000f, CellShape = 0.8f };
            TestAssert.That(bodies.Density(p, 0f, 2100f, 0f, 0f, out float lower) > 0.2f && lower > 0.2f && lower < 0.5f,
                "A shallow cluster lights in its own height interval");
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
                    // The packed top is widened by the base variation (spec 6: Fill widens bottom and visualTop by BaseVariation).
                    float lowTop = Math.Max(truth.LowTop, truth.CloudBase + Math.Max(genus.PuffDepth,
                        field.Params.DeckDepth) + genus.BaseWobble * 0.5f) + truth.BaseVariation;
                    TestAssert.That(Math.Abs(actual.LowTop - lowTop) <= 16000f / 255f * 0.51f,
                        "The packed ordinary ceiling mirrors field truth within half a height texel");
                    TestAssert.That(Math.Abs(actual.CloudBase - truth.CloudBase) <= CloudMaps.BaseRange / 255f * 0.51f,
                        "The packed cloud base mirrors field truth within half a 16 m step");
                }
            }
        }
    }
}
