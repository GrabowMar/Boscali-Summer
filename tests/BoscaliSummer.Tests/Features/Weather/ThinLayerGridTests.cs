using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Look pass round 3: the rules that keep thin layers from drawing grids and arcs (smooth bends that never fold, feathered fringes, fibres) and the
    /// smooth noise they rest on.</summary>
    internal static class ThinLayerGridTests
    {
        public static void Run()
        {
            SmoothNoiseIsSmoothAndUniform();
            BendsDoNotFold();
            FringesFeatherAndDetailActs();
            CirrusIsThinAndFibrous();
            CellPatternMeanDoesNotDependOnThePeriod();
        }

        /// <summary>Round 3: the cell taps read fixed slices of the volume, so the pattern's mean (and with it the layer's cover) is the same at every period rung.
        /// A tap whose slice followed altitude and period moved the mean by 0.03-0.05 (B ranges 0.38-0.54 across slices) and cirrocumulus showed 1.9 times its cover.</summary>
        private static void CellPatternMeanDoesNotDependOnThePeriod()
        {
            CloudBodies bodies = Bodies();
            for (float rung = 0f; rung <= 12f; rung += 2f)
                foreach (float cells in new[] { 1f, 0.5f })
                {
                    var layer = new SkyLayer { Cover = 0.5f, PeriodRung = rung, Softness = 0.5f, Detail = 0.7f, Cells = cells, Ripples = 1f - cells, RippleWavelength = 900f };
                    var rng = new Random(31);
                    double sum = 0; const int n = 1500;
                    for (int i = 0; i < n; i++)
                    {
                        bodies.Slab2D(layer, (float)(rng.NextDouble() * 80000.0), 4400f + 13f * i, (float)(rng.NextDouble() * 80000.0), 0, out float pattern, out _);
                        sum += pattern / n;
                    }
                    TestAssert.That(Math.Abs(sum - 0.515) < 0.02, "the cell pattern's mean at rung " + rung + " (cells " + cells + ") is " + sum.ToString("0.000"));
                }
        }

        private static CloudBodies Bodies() => new CloudBodies(BoscaliSummer.Modules.Weather.Visuals.CloudNoise3D.Generate(64, 47), 64, new SkyParams { LobeRung = 3f });

        private static void SmoothNoiseIsSmoothAndUniform()
        {
            var rng = new Random(9);
            double sum = 0, sum2 = 0, sumB = 0; int n = 20000;
            float lo = 1f, hi = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)(rng.NextDouble() * 400.0 - 200.0), v = (float)(rng.NextDouble() * 400.0 - 200.0);
                SmoothNoise.Vec2(u, v, SmoothNoise.SaltBend, out float a, out float b);
                sum += a; sum2 += a * a; sumB += b;
                lo = Math.Min(lo, Math.Min(a, b)); hi = Math.Max(hi, Math.Max(a, b));
                // Smooth: a step of 1/64 of a cell moves the value by under 3 % of the range.
                SmoothNoise.Vec2(u + 0.015625f, v, SmoothNoise.SaltBend, out float a2, out _);
                TestAssert.That(Math.Abs(a2 - a) < 0.03f, "the noise is smooth: " + Math.Abs(a2 - a));
            }
            double mean = sum / n, sd = Math.Sqrt(sum2 / n - mean * mean);
            TestAssert.That(lo >= 0f && hi <= 1f && Math.Abs(mean - 0.5) < 0.02 && Math.Abs(sumB / n - 0.5) < 0.02 && sd > 0.15 && sd < 0.25,
                "uniform enough: range " + lo + ".." + hi + ", mean " + mean.ToString("0.000") + ", sd " + sd.ToString("0.000"));
            TestAssert.That(SmoothNoise.Hash(3, -7, 5u) == SmoothNoise.Hash(3, -7, 5u) && SmoothNoise.Hash(3, -7, 5u) != SmoothNoise.Hash(-7, 3, 5u) && SmoothNoise.Hash(3, -7, 5u) != SmoothNoise.Hash(3, -7, 6u),
                "the hash is deterministic and depends on both corners and on the salt");
            Console.WriteLine("  hash(12,-34,bend) = 0x" + SmoothNoise.Hash(12, -34, SmoothNoise.SaltBend).ToString("X8"));
        }

        /// <summary>The bent coordinate q = p + displacement(p) must not fold: the round-1 and round-2 warps read the volume's fine octaves and folded 38 % and
        /// 32 % of the plane, which drew concentric arcs, mirrored lobes and straight-edged facets. The Jacobian of the map stays above 0.15 almost everywhere.</summary>
        private static void BendsDoNotFold()
        {
            CloudBodies bodies = Bodies();
            var rng = new Random(3);
            int foldsBend = 0, n = 4000;
            for (int i = 0; i < n; i++)
            {
                float cs = 225f, x = (float)(rng.NextDouble() * 60000.0), z = (float)(rng.NextDouble() * 60000.0), h = cs * 0.05f;
                void Map(float px, float pz, out float qx, out float qz)
                {
                    bodies.CellBend(px, pz, 4400f, cs, SlabMath.BendTile, out float bx, out float by);
                    qx = px + bx * cs * SlabMath.BendGain; qz = pz + by * cs * SlabMath.BendGain;
                }
                if (Fold(Map, x, z, h)) foldsBend++;
            }
            Console.WriteLine("  cell bend folds " + (foldsBend / (double)n).ToString("0.000") + " of the plane");
            TestAssert.That(foldsBend <= 0.01f * n, "the cell bend folds under 1 % of the plane: " + foldsBend / (float)n);
            foreach (float rung in new[] { 0f, 2f, 3f, 4f, 6f })
            {
                int folds = 0;
                for (int i = 0; i < n; i++)
                {
                    float x = (float)(rng.NextDouble() * 60000.0), z = (float)(rng.NextDouble() * 60000.0);
                    void Map(float px, float pz, out float qx, out float qz) { qx = px; qz = pz; bodies.WarpLobes(ref qx, 1500f, ref qz, rung, 0f); }
                    if (Fold(Map, x, z, 20f)) folds++;
                }
                Console.WriteLine("  lobe warp rung " + rung + " folds " + (folds / (double)n).ToString("0.000"));
                TestAssert.That(folds <= 0.04f * n, "the lobe warp at rung " + rung + " folds under 4 % of the plane: " + folds / (float)n);
            }
            // The curl of a hooked streak layer: the same noise at 3.1 scales and the curl gain (in scales).
            int curlFolds = 0;
            for (int i = 0; i < n; i++)
            {
                float x = (float)(rng.NextDouble() * 400.0), z = (float)(rng.NextDouble() * 400.0);
                void Map(float px, float pz, out float qx, out float qz)
                {
                    SmoothNoise.Vec2(px / 3.1f + 0.61f, pz / 3.1f + 0.61f, SmoothNoise.SaltCurl, out float a, out float b);
                    qx = px + (a - 0.5f) * SlabMath.CurlGain; qz = pz + (b - 0.5f) * SlabMath.CurlGain;
                }
                if (Fold(Map, x, z, 0.05f)) curlFolds++;
            }
            TestAssert.That(curlFolds == 0, "the curl never folds a streak: " + curlFolds + " of " + n);
            // Round 6: the meander shifts a streak across (0.9 scales) and along (0.15 spans, a streak span of 8 scales): the same lattice of 3 scales, and the shifted coordinate must not fold.
            int meanderFolds = 0;
            for (int i = 0; i < n; i++)
            {
                float x = (float)(rng.NextDouble() * 400.0), z = (float)(rng.NextDouble() * 400.0);
                void Map(float px, float pz, out float qx, out float qz)
                {
                    SmoothNoise.Vec2(px / SlabMath.MeanderTile + 0.29f, pz / SlabMath.MeanderTile + 0.83f, SmoothNoise.SaltMeander, out float a, out float b);
                    qx = px + (a - 0.5f) * 8f * SlabMath.MeanderAlong; qz = pz + (b - 0.5f) * SlabMath.Meander;
                }
                if (Fold(Map, x, z, 0.05f)) meanderFolds++;
            }
            // Round 6: the bend of the mesoscale lookup point (in tiles: amplitude / tile).
            int mesoFolds = 0;
            for (int i = 0; i < n; i++)
            {
                float x = (float)(rng.NextDouble() * 400.0), z = (float)(rng.NextDouble() * 400.0);
                void Map(float px, float pz, out float qx, out float qz)
                {
                    SmoothNoise.Vec2(px + 0.37f, pz + 0.19f, SmoothNoise.SaltMeso, out float a, out float b);
                    qx = px + (a - 0.5f) * SlabMath.MesoWarpAmp / SlabMath.MesoWarpTile; qz = pz + (b - 0.5f) * SlabMath.MesoWarpAmp / SlabMath.MesoWarpTile;
                }
                if (Fold(Map, x, z, 0.05f)) mesoFolds++;
            }
            TestAssert.That(mesoFolds == 0, "the mesoscale lookup bend never folds: " + mesoFolds + " of " + n);
            TestAssert.That(meanderFolds == 0, "the streak meander never folds a streak: " + meanderFolds + " of " + n);
        }

        private delegate void MapFn(float x, float z, out float qx, out float qz);

        private static bool Fold(MapFn map, float x, float z, float h)
        {
            map(x, z, out float q0x, out float q0z);
            map(x + h, z, out float qxx, out float qxz);
            map(x, z + h, out float qzx, out float qzz);
            float a = (qxx - q0x) / h, b = (qzx - q0x) / h, c = (qxz - q0z) / h, d = (qzz - q0z) / h;
            return a * d - b * c < 0.15f;
        }

        /// <summary>Round 3, G2: Detail acts inside the cover. A cirrus layer shows feathered fringes (a third or more of its visible area is under half
        /// density) and its mean density falls as Detail rises.</summary>
        private static void FringesFeatherAndDetailActs()
        {
            CloudBodies bodies = Bodies();
            SkyLayer ci = SkyArchetypes.Layer(LayerKind.CirrusFibratus).ToLayer(0.3f);
            double[] mean = new double[3]; int[] covered = new int[3];
            float[] details = { 0.05f, 0.3f, 1f };
            int fringe = 0, visible = 0; const int n = 6000;
            for (int k = 0; k < 3; k++)
            {
                SkyLayer l = ci; l.Detail = details[k];
                var rng = new Random(5);
                for (int i = 0; i < n; i++)
                {
                    float v = bodies.Slab2D(l, (float)(rng.NextDouble() * 54000.0), 4400f, (float)(rng.NextDouble() * 54000.0));
                    mean[k] += v / n;
                    if (v > 0.1f) { covered[k]++; if (k == 1) { visible++; if (v < 0.5f) fringe++; } }
                }
            }
            Console.WriteLine("  cirrus cover .3: mean density over the sky by Detail .05/.3/1: " + mean[0].ToString("0.000") + " " + mean[1].ToString("0.000") + " " + mean[2].ToString("0.000")
                + ", fringe share " + (fringe / (double)Math.Max(1, visible)).ToString("0.00") + ", covered " + covered[0] + "/" + covered[1] + "/" + covered[2]);
            TestAssert.That(mean[0] > mean[1] + 0.005 && mean[1] > mean[2] + 0.005, "Detail acts inside the cover of a cirrus layer: the mean density falls with Detail");
            TestAssert.That(fringe >= 0.3 * visible, "a cirrus layer has feathered fringes: " + fringe / (double)Math.Max(1, visible));
        }

        /// <summary>Round 3, G2: cirrus must read as thin fibres. The wind blows along +z at heading 0, so the streaks run along z: the field changes faster
        /// across them (over 1.5 km in x) than along them.</summary>
        private static void CirrusIsThinAndFibrous()
        {
            CloudBodies bodies = Bodies();
            SkyLayer ci = SkyArchetypes.Layer(LayerKind.CirrusFibratus).ToLayer(0.3f);
            // The decorrelation lag of the pattern value (the lag where its autocorrelation first falls under 0.5), along z and along x.
            double[] lag = new double[2];
            for (int axis = 0; axis < 2; axis++)
            {
                var rng = new Random(2);
                double[] corr = new double[9]; int n = 500;
                for (int i = 0; i < n; i++)
                {
                    float x = (float)(rng.NextDouble() * 60000.0), z = (float)(rng.NextDouble() * 60000.0);
                    bodies.Slab2D(ci, x, 8800f, z, 0, out float p0, out _);
                    for (int k = 1; k <= 8; k++)
                    {
                        bodies.Slab2D(ci, axis == 0 ? x : x + k * 500f, 8800f, axis == 0 ? z + k * 500f : z, 0, out float pk, out _);
                        corr[k] += (p0 - 0.52) * (pk - 0.52);
                    }
                    corr[0] += (p0 - 0.52) * (p0 - 0.52);
                }
                lag[axis] = 4000;
                for (int k = 1; k <= 8; k++) if (corr[k] / corr[0] < 0.5) { lag[axis] = k * 500; break; }
                Console.WriteLine("  cirrus pattern correlation along " + (axis == 0 ? "z (the streaks)" : "x (across)") + ": " + string.Join(" ", Array.ConvertAll(corr, c => (c / corr[0]).ToString("0.00"))));
            }
            TestAssert.That(lag[0] >= 2.5 * lag[1], "cirrus is fibrous: its pattern holds over " + lag[0] + " m along the streaks and " + lag[1] + " m across them");
        }
    }
}
