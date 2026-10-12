using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The look-pass rules of the thin layers (round 1): the rank law is a calibrated CDF so a layer covers about its Cover, the
    /// sigma constants match the shipped volume, cover clusters into patches with clear lanes, cell sizes and ripple spacings vary, and the
    /// rows of a ripple layer do not repeat at an even pitch.</summary>
    internal static class ThinLayerLookTests
    {
        public static void Run()
        {
            RankLawIsAGaussianCdf();
            PatternSigmaMatchesTheVolume();
            CoverFollowsNominal();
            PatchesLeaveClearLanes();
            SizeMaskSpansBothSizes();
            RippleRowsAreNotAComb();
            LobesVaryInSizeAndSpacing();
            FibreDashesDoNotLineUp();
        }

        private static CloudBodies Bodies() => new CloudBodies(BoscaliSummer.Modules.Weather.Visuals.CloudNoise3D.Generate(64, 47), 64, new SkyParams { LobeRung = 3f });

        private static void RankLawIsAGaussianCdf()
        {
            TestAssert.That(Math.Abs(SlabMath.Rank(0.52f, 0.1f) - 0.5f) < 1e-6f, "the mean pattern ranks 0.5");
            // z = 1.2816 is the 90th percentile, z = 2.054 the 98th, z = -1.2816 the 10th.
            TestAssert.That(Math.Abs(SlabMath.Rank(0.52f + 0.12816f, 0.1f) - 0.9f) < 0.01f, "one and a quarter sigma up ranks 0.90: " + SlabMath.Rank(0.52f + 0.12816f, 0.1f));
            TestAssert.That(Math.Abs(SlabMath.Rank(0.52f + 0.2054f, 0.1f) - 0.98f) < 0.01f, "two sigma up ranks 0.98: " + SlabMath.Rank(0.52f + 0.2054f, 0.1f));
            TestAssert.That(Math.Abs(SlabMath.Rank(0.52f - 0.12816f, 0.1f) - 0.1f) < 0.01f, "one and a quarter sigma down ranks 0.10");
            float previous = -1f;
            for (float p = -2f; p <= 3f; p += 0.01f)
            {
                float r = SlabMath.Rank(p, 0.02f);
                TestAssert.That(r >= previous && r >= 0f && r <= 1f && !float.IsNaN(r), "the rank is finite, in [0,1] and monotone, even far out at p " + p);
                previous = r;
            }
        }

        /// <summary>The standard deviation of the pattern of each morphology over an 80 km square, against the constants the rank law divides by.</summary>
        private static void PatternSigmaMatchesTheVolume()
        {
            CloudBodies bodies = Bodies();
            foreach ((string name, float sigma, int morph) in new[] { ("cells", SlabMath.SigmaCells, 0), ("ripples", SlabMath.SigmaRipples, 1), ("streaks", SlabMath.SigmaStreaks, 2) })
                foreach (float rung in new[] { 4f, 6f, 8f })
                {
                    var layer = new SkyLayer { Cover = 0.5f, PeriodRung = rung, Softness = 0.5f, Detail = 0.7f, RippleWavelength = 900f, Aspect = 4f, Curl = 0.3f };
                    if (morph == 0) layer.Cells = 1f; else if (morph == 1) layer.Ripples = 1f; else layer.Streaks = 1f;
                    var rng = new Random(11);
                    double sum = 0, sum2 = 0; const int n = 3000;
                    for (int i = 0; i < n; i++)
                    {
                        bodies.Slab2D(layer, (float)(rng.NextDouble() * 80000.0), 4400f, (float)(rng.NextDouble() * 80000.0), 0, out float pattern, out _);
                        sum += pattern; sum2 += pattern * pattern;
                    }
                    double mean = sum / n, sd = Math.Sqrt(sum2 / n - mean * mean);
                    Console.WriteLine("  sigma " + name + " rung " + rung + ": sd " + sd.ToString("0.000") + " against " + sigma + ", mean " + mean.ToString("0.000"));
                    TestAssert.That(Math.Abs(sd / sigma - 1.0) < 0.25 && Math.Abs(mean - 0.52) < 0.04, name + " at rung " + rung + ": sd " + sd.ToString("0.000") + " against " + sigma + ", mean " + mean.ToString("0.000"));
                }
        }

        /// <summary>The visible share of every pattern-led layer is about its nominal Cover (the judge measured cirrus at 3.6-4.3 times it).</summary>
        private static void CoverFollowsNominal()
        {
            CloudBodies bodies = Bodies();
            var bad = new List<string>();
            foreach (LayerKind kind in (LayerKind[])Enum.GetValues(typeof(LayerKind)))
            {
                LayerArchetype a = SkyArchetypes.Layer(kind);
                if (a.Veil >= 0.3f * (a.Cells + a.Ripples + a.Streaks + a.Veil)) continue;
                foreach (float cover in new[] { 0.05f, 0.1f, 0.3f, 0.6f })
                {
                    float share = ThinLayerTests.AreaShare(bodies, a.ToLayer(cover));
                    Console.WriteLine("  cover " + kind + " " + cover + ": share " + share.ToString("0.000") + " (x" + (share / cover).ToString("0.00") + ")");
                    if (!(share >= 0.6f * cover && share <= 1.5f * cover)) bad.Add(kind + " at cover " + cover + " shows " + share.ToString("0.000") + " of the sky");
                }
            }
            TestAssert.That(bad.Count == 0, string.Join("; ", bad));
        }

        private static void PatchesLeaveClearLanes()
        {
            CloudBodies bodies = Bodies();
            LayerArchetype a = SkyArchetypes.Layer(LayerKind.AcFloccus);
            SkyLayer layer = a.ToLayer(0.2f);
            var rng = new Random(23);
            int patch = 0, lane = 0, empty = 0; const int n = 4000;
            for (int i = 0; i < n; i++)
            {
                float v = bodies.Slab2D(layer, (float)(rng.NextDouble() * 80000.0), 4400f, (float)(rng.NextDouble() * 80000.0), 0, out _, out float field);
                float mask = SlabMath.PatchMask(field);
                if (mask > 0.5f) patch++;
                if (mask < 0.02f) { lane++; if (v < 0.005f) empty++; }
            }
            TestAssert.That(patch >= 0.1f * n && patch <= 0.6f * n, "patches hold 10-60 % of the sky: " + patch / (float)n);
            TestAssert.That(lane >= 0.15f * n, "clear lanes are at least 15 % of the sky: " + lane / (float)n);
            TestAssert.That(empty >= 0.97f * lane, "a lane holds no cloud: " + empty + " of " + lane + " lane points are empty");
            // A deck is not patched and a veil neither: the cover mask there is the old slow modulation alone.
            TestAssert.That(SlabMath.Patchiness(1f, 0f) == 0f && SlabMath.Patchiness(0.1f, 1f) == 0f && SlabMath.Patchiness(0.1f, 0f) == 1f && SlabMath.Patchiness(0.6f, 0f) == 1f
                && SlabMath.Patchiness(0.8f, 0f) > 0.3f && SlabMath.Patchiness(0.8f, 0f) < 0.7f, "patchiness: full when thin, half at mackerel's cover 0.8, none for a full deck and for veils");
            TestAssert.That(Math.Abs(SlabMath.PatchFactor(0.7f, 0f) - 1f) < 1e-6f && SlabMath.PatchFactor(0f, 1f) == 0f, "patch factor: 1 without patchiness, 0 in a lane with it");
        }

        private static void SizeMaskSpansBothSizes()
        {
            TestAssert.That(SlabMath.SizeMask(0.1f) == 0f && SlabMath.SizeMask(0.9f) == 1f && Math.Abs(SlabMath.SizeMask(0.5f) - 0.5f) < 0.05f, "the size mask spans large and small cells");
            // On the shipped volume the size field is G of the mesoscale fetch: a fifth of the sky or more sits in each size.
            byte[] noise = BoscaliSummer.Modules.Weather.Visuals.CloudNoise3D.Generate(64, 47);
            int large = 0, small = 0, total = 0;
            for (int i = 1; i < noise.Length; i += 4) { total++; float m = SlabMath.SizeMask(noise[i] / 255f); if (m < 0.2f) large++; if (m > 0.8f) small++; }
            TestAssert.That(large >= 0.1f * total && small >= 0.08f * total, "both cell sizes occur: " + large / (float)total + " large, " + small / (float)total + " small");
        }

        /// <summary>Round 2: the low volume's lobes (warped lookup, a smaller lobe tap where the size field is high). Along lines through the field the
        /// spacing of the body's upcrossings must vary (a Worley tile of equal lobes gives a narrow spread) and the body stays in [0,1].</summary>
        private static void LobesVaryInSizeAndSpacing()
        {
            CloudBodies bodies = Bodies();
            var spacings = new List<double>();
            for (int line = 0; line < 24; line++)
            {
                float z0 = 7000f + line * 2113f, x0 = 3000f + line * 1237f;
                bool above = false; float last = float.NaN;
                for (int i = 0; i < 1600; i++)
                {
                    float x = x0 + i * 25f, wz = z0;
                    bodies.WarpLobes(ref x, 1500f, ref wz, 0f, 0f);
                    float v = bodies.Bodies(x, 1500f, wz, 700f);
                    TestAssert.That(v >= 0f && v <= 1f && !float.IsNaN(v), "the body is finite and in [0,1]");
                    bool now = v > 0.5f;
                    if (now && !above) { if (!float.IsNaN(last)) spacings.Add(x - last); last = x; }
                    above = now;
                }
            }
            double mean = 0; foreach (double s in spacings) mean += s / spacings.Count;
            double var = 0; foreach (double s in spacings) var += (s - mean) * (s - mean) / spacings.Count;
            double cv = Math.Sqrt(var) / mean;
            Console.WriteLine("  lobe upcrossing spacing: " + spacings.Count + " gaps, mean " + mean.ToString("0") + " m, cv " + cv.ToString("0.000"));
            TestAssert.That(spacings.Count > 200 && cv > LobeSpacingCv, "lobe spacing varies (cv " + cv.ToString("0.000") + ")");
        }
        private const double LobeSpacingCv = 0.55;
        private const double DashAlignment = 0.3;

        /// <summary>Round 2: the dashes of a streak layer. Two fibres a fibre-width apart must not break at the same places: the correlation of their
        /// cloud/clear patterns along the wind stays low (the staggered along-coordinate), where aligned dashes read as dotted rows.</summary>
        private static void FibreDashesDoNotLineUp()
        {
            CloudBodies bodies = Bodies();
            var layer = new SkyLayer { Cover = 0.3f, PeriodRung = 6f, Softness = 0.5f, Detail = 0.7f, Streaks = 1f, Aspect = 5f, Curl = 0.1f };
            double mean = 0; int count = 0;
            for (int line = 0; line < 30; line++)
            {
                float x0 = 4000f + line * 2711f, z0 = 6000f + line * 1409f;
                const int steps = 200;
                var p = new float[steps]; var q = new float[steps];
                for (int i = 0; i < steps; i++)
                {
                    bodies.Slab2D(layer, x0, 8000f, z0 + i * 150f, 0, out p[i], out _);
                    bodies.Slab2D(layer, x0 + 900f, 8000f, z0 + i * 150f, 0, out q[i], out _);
                }
                double mp = 0, mq = 0; for (int i = 0; i < steps; i++) { mp += p[i] / steps; mq += q[i] / steps; }
                double num = 0, dp = 0, dq = 0;
                for (int i = 0; i < steps; i++) { num += (p[i] - mp) * (q[i] - mq); dp += (p[i] - mp) * (p[i] - mp); dq += (q[i] - mq) * (q[i] - mq); }
                if (dp > 1e-9 && dq > 1e-9) { mean += num / Math.Sqrt(dp * dq); count++; }
            }
            mean /= Math.Max(1, count);
            Console.WriteLine("  neighbouring fibre correlation: " + mean.ToString("0.000") + " over " + count + " pairs");
            TestAssert.That(Math.Abs(mean) < DashAlignment, "neighbouring fibres break at different places: correlation " + mean.ToString("0.000"));
        }

        /// <summary>Along a line across the rows, the pattern of a ripple layer must not repeat at the nominal wavelength with the correlation of a comb.</summary>
        private static void RippleRowsAreNotAComb()
        {
            CloudBodies bodies = Bodies();
            var layer = new SkyLayer { Cover = 0.5f, PeriodRung = 6f, Softness = 0.5f, Detail = 0.7f, Ripples = 1f, RippleWavelength = 900f, Aspect = 2f };
            double meanCorrelation = 0;
            const int lines = 40, steps = 160, lag = 8; // 900 m wavelength in 112.5 m steps: a lag of eight steps is one wavelength
            for (int line = 0; line < lines; line++)
            {
                var v = new float[steps];
                // The default wind is +z, so "across" is x: step along x.
                for (int i = 0; i < steps; i++) bodies.Slab2D(layer, 5000f + line * 3137f + i * 112.5f, 4400f, 9000f + line * 1571f, 0, out v[i], out _);
                double mean = 0; foreach (float x in v) mean += x / steps;
                double num = 0, den = 0;
                for (int i = 0; i < steps; i++) den += (v[i] - mean) * (v[i] - mean);
                for (int i = 0; i + lag < steps; i++) num += (v[i] - mean) * (v[i + lag] - mean);
                meanCorrelation += (den <= 1e-9 ? 0.0 : num / den) / lines;
            }
            Console.WriteLine("  ripple autocorrelation at one wavelength: mean " + meanCorrelation.ToString("0.000"));
            TestAssert.That(meanCorrelation < 0.5, "rows do not repeat at an even pitch: mean correlation at one wavelength " + meanCorrelation.ToString("0.000"));
        }
    }
}
