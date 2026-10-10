using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The generalised thin layers (spec 4.3, 7.4) and the feature-size ladder (6): the cover masks, the morphology weights, the
    /// altitude profile and its integral, the CPU 2D field with its four morphologies, and the rung blend of the low bodies.</summary>
    internal static class ThinLayerTests
    {
        public static void Run()
        {
            SlabCoverMaskValues();
            WeightsNormaliseAndDrop();
            ProfileIntegralAndOverlap();
            SlabFieldMorphologies();
            SlabFieldRungBlendIsContinuous();
            BodiesLadderBlend();
        }

        private static void SlabCoverMaskValues()
        {
            // Spec 4.3 table: where the new mask reaches 1.0, against the peak of the old mask at body 1.
            foreach ((float cover, float full, float oldPeak) in new[] { (0.02f, 1.00f, 0.11f), (0.05f, 0.975f, 0.28f), (0.15f, 0.925f, 0.83f) })
            {
                TestAssert.That(Math.Abs(SlabMath.SlabCoverMask(full, cover, 0.5f) - 1f) < 1e-4f, "cover " + cover + " is fully cloud at body " + full);
                TestAssert.That(SlabMath.SlabCoverMask(full - 0.04f, cover, 0.5f) < 0.99f && SlabMath.SlabCoverMask(1f - cover - 0.001f, cover, 0.5f) == 0f,
                    "and clear below its threshold 1 - cover");
                TestAssert.That(Math.Abs(SlabMath.CoverMask(1f, cover) - oldPeak) < 0.01f, "the old mask peaks at " + oldPeak + " (too faint for a thin layer)");
            }
            TestAssert.That(Math.Abs(SlabMath.SlabCoverMask(0.7f, 0.6f, 0.5f) - SlabMath.CoverMask(0.7f, 0.6f)) < 1e-6f, "from cover 0.30 up it is the old mask");
            TestAssert.That(SlabMath.SlabCoverMask(0.905f, 0.10f, 0f) - SlabMath.SlabCoverMask(0.905f, 0.10f, 1f) > 0.1f,
                "softness widens the edge: at body 0.905 a hard layer is at 0.20 and a soft one at 0.07");
            TestAssert.That(Math.Abs(SlabMath.CoverMask(0.5f, 0f)) < 1e-6f && CloudBodies.CoverMask(0.7f, 0.5f) == SlabMath.CoverMask(0.7f, 0.5f),
                "CloudBodies.CoverMask is the shared mask");
        }

        private static void WeightsNormaliseAndDrop()
        {
            SlabMath.Weights(new SkyLayer { Cells = 1f }, out float c, out float r, out float s, out float v);
            TestAssert.That(c == 1f && r == 0f && s == 0f && v == 0f, "a single weight is the whole");
            SlabMath.Weights(new SkyLayer { Cells = 0.6f, Ripples = 0.4f }, out c, out r, out s, out v);
            TestAssert.That(Math.Abs(c - 0.6f) < 1e-6f && Math.Abs(r - 0.4f) < 1e-6f, "weights are normalised to sum 1");
            SlabMath.Weights(new SkyLayer { Cells = 3f, Ripples = 1f, Streaks = 0.1f, Veil = 0.1f }, out c, out r, out s, out v);
            TestAssert.That(s == 0f && v == 0f && Math.Abs(c - 0.75f) < 1e-6f && Math.Abs(r - 0.25f) < 1e-6f,
                "a weight under 0.05 is dropped and the rest renormalised");
            SlabMath.Weights(default, out c, out r, out s, out v);
            TestAssert.That(c + r + s == 0f && v == 1f, "a layer with no weights is a veil");
            SlabMath.Weights(SkyLayer.LegacyMid(0.5f, 0.2f), out c, out r, out s, out v);
            TestAssert.That(Math.Abs(c + r + s + v - 1f) < 1e-5f && c == 0f && r > 0f && s > 0f, "the back-derived mid layer is ripples and streaks");
        }

        private static void ProfileIntegralAndOverlap()
        {
            TestAssert.That(Math.Abs(SlabMath.H1 - 0.2598076f) < 1e-6f && Math.Abs(SlabMath.H2 - 0.7401924f) < 1e-6f, "the plateau edges the shader hard-codes");
            TestAssert.That(SlabMath.Profile(0f) == 0f && SlabMath.Profile(1f) == 0f && SlabMath.Profile(0.5f) == 1f &&
                Math.Abs(SlabMath.Profile(0.1f) - 0.468f) < 1e-4f, "0 at the base and top, a plateau between");
            TestAssert.That(Math.Abs(SlabMath.Integral(1f) - 0.7706f) < 0.001f, "the profile averages 0.77, the factor the old shader baked in: " + SlabMath.Integral(1f));
            for (float h = 0f; h <= 1f; h += 0.037f)
            {
                double numeric = 0; const int n = 4000;
                for (int i = 0; i < n; i++) numeric += SlabMath.Profile((i + 0.5f) / n * h) * h / n;
                TestAssert.That(Math.Abs(numeric - SlabMath.Integral(h)) < 0.0005, "the integral matches numeric integration at h " + h);
            }
            // Cut a vertical ray through a 600 m layer into pieces: the optical weights add up to the whole layer however it is cut.
            foreach (int pieces in new[] { 1, 2, 3, 7, 24 })
            {
                double total = 0;
                for (int i = 0; i < pieces; i++)
                {
                    float lo = 4000f + 600f * i / pieces, hi = 4000f + 600f * (i + 1) / pieces;
                    total += SlabMath.Overlap(lo, hi, 4000f, 4600f) * (hi - lo);
                }
                TestAssert.That(Math.Abs(total - 600.0 * SlabMath.Integral(1f)) < 0.5, "pieces add up to the layer: " + pieces + " -> " + total);
            }
            TestAssert.That(SlabMath.Overlap(5000f, 5300f, 4000f, 4600f) == 0f && SlabMath.Overlap(3000f, 3500f, 4000f, 4600f) == 0f, "a sample wholly outside the layer weighs nothing");
            TestAssert.That(Math.Abs(SlabMath.Overlap(4300f, 4310f, 4000f, 4600f) - 1f) < 1e-6f, "a thin sample at mid height weighs the plateau");
            TestAssert.That(Math.Abs(SlabMath.Overlap(4310f, 4300f, 4000f, 4600f) - SlabMath.Overlap(4300f, 4310f, 4000f, 4600f)) < 1e-6f, "a falling ray is the same sample");
            TestAssert.That(SlabMath.Overlap(3980f, 4020f, 4000f, 4600f) > 0f && SlabMath.Overlap(3980f, 4020f, 4000f, 4600f) < 0.05f, "a sample straddling the base weighs a little");
            TestAssert.That(SlabMath.Overlap(1f, 2f, 5f, 5f) == 0f, "a layer with no thickness weighs nothing");
        }

        private static byte[] Uniform(byte r, byte g, byte b)
        {
            var bytes = new byte[4 * 4 * 4 * 4];
            for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = r; bytes[i + 1] = g; bytes[i + 2] = b; bytes[i + 3] = 255; }
            return bytes;
        }

        /// <summary>The one place the thin-layer tests build a CloudBodies: Task 5 switches it to the new row type.</summary>
        private static CloudBodies MakeBodies(byte[] noise, int size) => new CloudBodies(noise, size, new SkyParams { LobeRung = 3f });

        private static void SlabFieldMorphologies()
        {
            // With one constant noise value every pattern has the same value, so the four morphologies differ only by what they add:
            // ripples add their rows, the veil flattens, Detail sets the contrast. That isolates the plumbing from the noise.
            var bodies = MakeBodies(Uniform(200, 90, 255), 4);
            var cells = new SkyLayer { Cover = 0.6f, PeriodRung = 6f, Softness = 0.5f, Detail = 0.7f, Cells = 1f, RippleWavelength = 900f, Aspect = 1.4f };
            var veil = cells; veil.Cells = 0f; veil.Veil = 1f;
            var ripples = cells; ripples.Cells = 0f; ripples.Ripples = 1f;
            var streaks = cells; streaks.Cells = 0f; streaks.Streaks = 1f;
            float cellsValue = bodies.Slab2D(cells, 1200f, 4400f, 700f);
            float streakValue = bodies.Slab2D(streaks, 1200f, 4400f, 700f);
            TestAssert.That(cellsValue > 0.1f && Math.Abs(cellsValue - streakValue) < 1e-5f, "on a constant field cells and streaks agree");
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 40; i++)
            {
                float v = bodies.Slab2D(ripples, i * 137f, 4400f, i * 53f);
                lo = Math.Min(lo, v); hi = Math.Max(hi, v);
            }
            TestAssert.That(hi - lo > 0.05f, "ripples add parallel rows across the layer: spread " + (hi - lo));
            float veilValue = bodies.Slab2D(veil, 1200f, 4400f, 700f);
            // No pattern weight: body 0.5, flattened by the whole veil weight to 0.85, then the cover the mesoscale patch leaves (G = 90 -> patch 0.35).
            float patchCover = Math.Min(1f, 0.6f * (0.55f + 0.9f * Math.Max(0f, Math.Min(1f, 0.5f + (90f / 255f - 0.52f) * 3.16f))));
            // ... times the interior density Detail scales, lerp(1, body, Detail) = 1 - 0.7 * 0.15 for this layer's Detail 0.7.
            TestAssert.That(Math.Abs(veilValue - SlabMath.SlabCoverMask(0.85f, patchCover, 0.5f) * (1f - 0.7f * 0.15f)) < 1e-4f,
                "a pure veil is the flattened body 0.85 through the cover mask, times the interior density, got " + veilValue);
            TestAssert.That(bodies.Slab2D(new SkyLayer { Cover = 0.004f, Cells = 1f, Detail = 0.7f, PeriodRung = 6f }, 0f, 4400f, 0f) == 0f, "under 0.005 cover the layer draws nothing");

            // Detail never moves how much of the sky the layer covers (that is Cover's job, see ThinLayerAreaFollowsCover); it scales the
            // interior density, so a crisp layer (Detail 1) is thinner on average than a flat one (Detail 0.05) over the same samples.
            byte[] noise = BoscaliSummer.Modules.Weather.Visuals.CloudNoise3D.Generate(64, 47);
            var real = MakeBodies(noise, 64);
            var soft = cells; soft.Detail = 0.05f; soft.Cover = 0.5f;
            var sharp = cells; sharp.Detail = 1f; sharp.Cover = 0.5f;
            double meanSoft = 0, meanSharp = 0; int coveredSoft = 0, coveredSharp = 0; const int n = 600;
            for (int i = 0; i < n; i++)
            {
                float soft2d = real.Slab2D(soft, i * 811f, 4400f, i * 457f), sharp2d = real.Slab2D(sharp, i * 811f, 4400f, i * 457f);
                meanSoft += soft2d / n; meanSharp += sharp2d / n;
                if (soft2d > 0.1f) coveredSoft++;
                if (sharp2d > 0.1f) coveredSharp++;
            }
            TestAssert.That(Math.Abs(coveredSoft - coveredSharp) <= n * 0.08, "Detail does not move the covered area: " + coveredSoft + " vs " + coveredSharp + " of " + n);
            TestAssert.That(meanSharp < meanSoft - 0.005, "Detail thins the interior density: " + meanSoft + " (Detail 0.05) vs " + meanSharp + " (Detail 1)");
            // A curl bends the streaks: the same streak layer with and without curl gives different values somewhere.
            var curled = streaks; curled.Curl = 1f; curled.Aspect = 4f; streaks.Aspect = 4f;
            int differ = 0;
            for (int i = 0; i < 60; i++)
                if (Math.Abs(real.Slab2D(curled, i * 911f, 4400f, i * 331f) - real.Slab2D(streaks, i * 911f, 4400f, i * 331f)) > 0.02f) differ++;
            TestAssert.That(differ > 10, "curl moves the streaks: " + differ + " of 60 samples differ");
        }

        private static void SlabFieldRungBlendIsContinuous()
        {
            byte[] noise = BoscaliSummer.Modules.Weather.Visuals.CloudNoise3D.Generate(64, 47);
            var bodies = MakeBodies(noise, 64);
            var layer = new SkyLayer { Cover = 0.6f, Softness = 0.5f, Detail = 0.7f, Cells = 0.5f, Streaks = 0.5f, RippleWavelength = 900f, Aspect = 2f };
            float worst = 0f;
            for (int point = 0; point < 30; point++)
            {
                float x = point * 1237f, z = point * 619f;
                float previous = float.NaN;
                for (float rung = 5f; rung <= 6.0f; rung += 0.0125f)
                {
                    layer.PeriodRung = rung;
                    float v = bodies.Slab2D(layer, x, 4400f, z);
                    if (!float.IsNaN(previous)) worst = Math.Max(worst, Math.Abs(v - previous));
                    previous = v;
                }
            }
            TestAssert.That(worst < 0.25f, "the thin-layer field moves smoothly through a rung ramp (worst step " + worst + ")");
            layer.PeriodRung = 5f;
            float whole = bodies.Slab2D(layer, 1237f, 4400f, 619f);
            layer.PeriodRung = 5.3f;
            TestAssert.That(Math.Abs(bodies.Slab2D(layer, 1237f, 4400f, 619f) - whole) < 1e-6f, "below frac 0.35 the field is exactly the lower rung");
            layer.PeriodRung = 5.7f;
            float upper = bodies.Slab2D(layer, 1237f, 4400f, 619f);
            layer.PeriodRung = 6f;
            TestAssert.That(Math.Abs(upper - bodies.Slab2D(layer, 1237f, 4400f, 619f)) < 1e-6f, "above frac 0.65 it is exactly the upper rung");
        }

        private static void BodiesLadderBlend()
        {
            byte[] noise = BoscaliSummer.Modules.Weather.Visuals.CloudNoise3D.Generate(64, 47);
            var bodies = MakeBodies(noise, 64);
            TestAssert.That(bodies.BodiesLadder(4100f, 900f, 2300f, 3f, false, 0f) == bodies.Bodies(4100f, 900f, 2300f, Ladder.LobeMetres(3f)),
                "an integer rung is one plain Bodies call");
            TestAssert.That(bodies.BodiesLadder(4100f, 900f, 2300f, 3.3f, false, 0f) == bodies.BodiesLadder(4100f, 900f, 2300f, 3f, false, 0f),
                "below frac 0.35 it is still the lower rung");
            float worst = 0f;
            for (int point = 0; point < 40; point++)
            {
                float x = point * 1713f, z = point * 911f, previous = float.NaN;
                for (float rung = 2f; rung <= 3f; rung += 0.01f)
                {
                    float v = bodies.BodiesLadder(x, 1500f, z, rung, false, 0f);
                    if (!float.IsNaN(previous)) worst = Math.Max(worst, Math.Abs(v - previous));
                    previous = v;
                }
            }
            TestAssert.That(worst < 0.12f, "bodies move smoothly through a rung ramp (worst step " + worst + ")");
            float front = bodies.BodiesLadder(4100f + 5311f, 900f + 5311f, 2300f + 5311f, 3f, true, 0f);
            float plain = bodies.BodiesLadder(4100f + 5311f, 900f + 5311f, 2300f + 5311f, 3f, false, 0f);
            TestAssert.That(front != plain, "the front body is a larger lump than the deck's");
            TestAssert.That(bodies.BodiesLadder(4100f, 900f, 2300f, 3f, true, 1f) == bodies.Bodies(4100f, 900f, 2300f, 5200f), "a sheet front is the fixed 5.2 km body");
            TestAssert.That(bodies.BodiesLadder(4100f, 900f, 2300f, 3.5f, true, 1f) == bodies.BodiesLadder(4100f, 900f, 2300f, 3f, true, 1f),
                "and at every rung: both rungs of a sheet front are the same 5.2 km scale, so there is no second tap and no contrast boost");
        }
    }
}
