using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The contact sheet's metrics (spec 10.3), checked on synthetic images whose answers are known.</summary>
    internal static class SkyMetricsTests
    {
        public static void Run()
        {
            CoverIsTheShareThatDiffersFromTheOpenSky();
            LuminanceSpreadAndLitDarkRatio();
            FeatureSizeFollowsTheStripeWidth();
            VerdictUsesTheStudyBands();
            ExpectationsFollowTheLibrary();
            DistinctnessIsTheMeanAbsoluteDifference();
        }

        private static void DistinctnessIsTheMeanAbsoluteDifference()
        {
            float[] a = Flat(0.5f), b = Flat(0.5f);
            TestAssert.That(SkyMetrics.MeanAbsDifference(a, b, W, H) == 0f, "identical frames are 0 apart");
            for (int i = 0; i < b.Length; i++) b[i] = 0.6f;
            TestAssert.That(Math.Abs(SkyMetrics.MeanAbsDifference(a, b, W, H) - 0.1f) < 1e-5f, "a uniform 0.1 shift is 0.1 apart");
            float[] c = Flat(0.5f);
            for (int y = 90; y < H; y++) for (int x = 0; x < W; x++) c[y * W + x] = 1f;
            TestAssert.That(SkyMetrics.MeanAbsDifference(a, c, W, H) == 0f, "only the metrics' rows count");
        }

        private const int W = 320, H = 124;

        private static float[] Flat(float v)
        {
            var a = new float[W * H];
            Array.Fill(a, v);
            return a;
        }

        /// <summary>The per-pixel difference of an image from the open sky: what the bench computes from the two renders.</summary>
        private static float[] Diff(float[] image, float[] sky)
        {
            var d = new float[image.Length];
            for (int i = 0; i < d.Length; i++) d[i] = Math.Abs(image[i] - sky[i]);
            return d;
        }

        private static SkyMetrics.Result Measure(float[] image, float[] sky, float degrees) => SkyMetrics.Measure(image, Diff(image, sky), W, H, degrees);

        private static void CoverIsTheShareThatDiffersFromTheOpenSky()
        {
            float[] sky = Flat(0.5f), image = Flat(0.5f);
            SkyMetrics.Result none = Measure(image, sky, 0.3f);
            TestAssert.That(none.CloudFraction == 0f && none.OpenFraction == 1f && none.LumaStdDev < 1e-6f && !none.HasLitDark, "identical images: no cloud, no spread");
            // Brighten the left 30 % of every row by 0.3.
            for (int y = 0; y < H; y++) for (int x = 0; x < W * 3 / 10; x++) image[y * W + x] = 0.8f;
            SkyMetrics.Result third = Measure(image, sky, 0.3f);
            TestAssert.That(Math.Abs(third.CloudFraction - 0.3f) < 0.01f, "30 % of the region differs: " + third.CloudFraction);
            // Only rows 7-77 of 124 are read: a bright band outside them does not count.
            float[] outside = Flat(0.5f);
            for (int y = 90; y < H; y++) for (int x = 0; x < W; x++) outside[y * W + x] = 1f;
            TestAssert.That(Measure(outside, sky, 0.3f).CloudFraction == 0f, "the haze veil rows are outside the region");
            TestAssert.That(Math.Abs(SkyMetrics.RowTop - 7f / 124f) < 1e-6f && Math.Abs(SkyMetrics.RowBottom - 77f / 124f) < 1e-6f, "rows 7..77 of 124");
        }

        private static void LuminanceSpreadAndLitDarkRatio()
        {
            float[] sky = Flat(0.1f), image = Flat(0.1f);
            // Cloud pixels: a ramp from 0.2 (the darkest 3 %) to 0.8 (the brightest 3 %).
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) image[y * W + x] = 0.2f + 0.6f * x / (W - 1);
            SkyMetrics.Result ramp = Measure(image, sky, 0.3f);
            TestAssert.That(ramp.CloudFraction > 0.99f && ramp.HasLitDark, "a bright ramp is all cloud");
            TestAssert.That(Math.Abs(ramp.LitDarkRatio - 0.791f / 0.209f) < 0.1f, "lit over dark of the extremes: " + ramp.LitDarkRatio);
            TestAssert.That(Math.Abs(ramp.LumaStdDev - 0.6f / (float)Math.Sqrt(12.0)) < 0.01f, "the spread of a uniform ramp: " + ramp.LumaStdDev);
            float[] small = Flat(0.1f);
            for (int x = 0; x < 10; x++) small[40 * W + x] = 0.9f;
            TestAssert.That(!Measure(small, sky, 0.3f).HasLitDark, "too few cloud pixels give no lit/dark ratio");
        }

        private static void FeatureSizeFollowsTheStripeWidth()
        {
            float[] sky = Flat(0.1f);
            float[] Stripes(int period)
            {
                var a = new float[W * H];
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) a[y * W + x] = (x / (period / 2)) % 2 == 0 ? 0.8f : 0.3f;
                return a;
            }
            float small = Measure(Stripes(16), sky, 1f).FeatureDegrees;
            float large = Measure(Stripes(64), sky, 1f).FeatureDegrees;
            TestAssert.That(small > 0f && large > small * 2f && large < small * 4f, "four times the stripe width, about triple the feature (quantised lags): " + small + " -> " + large);
            TestAssert.That(Math.Abs(Measure(Stripes(64), sky, 0.5f).FeatureDegrees - large * 0.5f) < 1e-3f, "degrees follow the pixel angle");
            TestAssert.That(Measure(Flat(0.5f), sky, 1f).FeatureDegrees == 0f, "a flat image has no feature size");
        }

        private static void VerdictUsesTheStudyBands()
        {
            var cumulus = new SkyMetrics.Expectation { Cover = 0.30f, Cumuliform = true };
            var good = new SkyMetrics.Result { CloudFraction = 0.28f, LumaStdDev = 0.15f, LitDarkRatio = 3.5f, HasLitDark = true };
            TestAssert.That(SkyMetrics.Failures(good, cumulus).Count == 0, "a cumulus sky inside every band passes");
            var flat = good; flat.LitDarkRatio = 2.0f;
            TestAssert.That(SkyMetrics.Failures(flat, cumulus).Count == 1, "cumulus lit/dark 2.0 is below 3.0");
            var sparse = good; sparse.CloudFraction = 0.03f;
            TestAssert.That(SkyMetrics.Failures(sparse, cumulus).Count == 1, "cloud cover under the floor (0.06) fails");
            var overcast = new SkyMetrics.Expectation { Cover = 0.97f, Sheet = true };
            var deck = new SkyMetrics.Result { CloudFraction = 0.95f, LumaStdDev = 0.08f, LitDarkRatio = 2.0f, HasLitDark = true };
            TestAssert.That(SkyMetrics.Failures(deck, overcast).Count == 0, "an overcast sheet at lit/dark 2.0 passes");
            deck.CloudFraction = 1f;
            TestAssert.That(SkyMetrics.Failures(deck, overcast).Count == 0, "and a deck that fills the whole region is fine");
            deck.CloudFraction = 0.95f;
            deck.LitDarkRatio = 3.2f;
            TestAssert.That(SkyMetrics.Failures(deck, overcast).Count == 1, "overcast lit/dark 3.2 is above 2.5");
            deck.LitDarkRatio = 2.0f; deck.CloudFraction = 0.5f;
            TestAssert.That(SkyMetrics.Failures(deck, overcast).Count == 1, "an overcast that shows half open sky fails");
            deck.CloudFraction = 0.95f; deck.LumaStdDev = 0.02f;
            TestAssert.That(SkyMetrics.Failures(deck, overcast).Count == 1, "a deck flatter than 0.055 fails");
            var feature = new SkyMetrics.Expectation { Cover = 0.17f, Cumuliform = true, Feature = true };
            var group = good; group.CloudFraction = 0.9f;
            TestAssert.That(SkyMetrics.Failures(group, feature).Count == 0, "a placed cumulus group may fill the view: no cover band for a feature sky");
            group.CloudFraction = 0.01f;
            TestAssert.That(SkyMetrics.Failures(group, feature).Count == 1, "but a feature sky with no cloud in view fails");
            var faint = new SkyMetrics.Expectation { Cover = 0.11f, Faint = true };
            faint.ThinOnly = true; faint.LayerCover = 0.30f;
            TestAssert.That(SkyMetrics.Failures(new SkyMetrics.Result { CloudFraction = 0f, LayerFraction = 0.25f }, faint).Count == 0 &&
                SkyMetrics.Failures(new SkyMetrics.Result { CloudFraction = 1f, LayerFraction = 1f }, faint).Count == 0, "a faint high-cloud sky has no cover band, only the question whether its layer is seen");
            TestAssert.That(SkyMetrics.Failures(new SkyMetrics.Result { CloudFraction = 0f, LayerFraction = 0.02f }, faint).Count == 1,
                "a layer of cover 0.30 that shows 2 % of the view is invisible: a defect, not a faint sky");
            var invisibleClear = new SkyMetrics.Expectation { Cover = 0.045f, Clear = true, ThinOnly = true, LayerCover = 0.30f };
            TestAssert.That(SkyMetrics.Failures(new SkyMetrics.Result { LayerFraction = 0.01f }, invisibleClear).Count == 1 &&
                SkyMetrics.Failures(new SkyMetrics.Result { LayerFraction = 0.20f }, invisibleClear).Count == 0,
                "a clear sky whose cirrus draws nothing fails (it used to pass for showing little cloud)");
            TestAssert.That(SkyMetrics.Failures(new SkyMetrics.Result { LayerFraction = 0f }, new SkyMetrics.Expectation { Cover = 0.0075f, Clear = true, ThinOnly = true, LayerCover = 0.05f }).Count == 0,
                "a layer under cover 0.10 (the Blue CAVOK wisp) is not judged");
            var clear = new SkyMetrics.Expectation { Cover = 0.0075f, Clear = true };
            TestAssert.That(SkyMetrics.Failures(new SkyMetrics.Result { CloudFraction = 0.04f }, clear).Count == 0 &&
                SkyMetrics.Failures(new SkyMetrics.Result { CloudFraction = 0.4f }, clear).Count == 1, "a clear sky may show a little cloud, not a lot");
            var streaks = new SkyMetrics.Expectation { Cover = 0.045f, Clear = true, ThinOnly = true, LayerCover = 0.30f };
            TestAssert.That(SkyMetrics.Failures(new SkyMetrics.Result { CloudFraction = 0.19f, LayerFraction = 0.25f }, streaks).Count == 0 &&
                SkyMetrics.Failures(new SkyMetrics.Result { CloudFraction = 0.45f, LayerFraction = 0.5f }, streaks).Count == 1,
                "a clear sky of cirrus streaks may show about its layer's cover (0.30), not more");
            SkyDef opacus = SkyLibrary.Get(32);
            TestAssert.That(SkyMetrics.For(opacus.Nominal, opacus.HasLow, opacus.Genus.Genus).Sheet, "sky 33's full altostratus deck is a sheet");
        }

        private static void ExpectationsFollowTheLibrary()
        {
            SkyDef fair = SkyLibrary.Get(7), overcast = SkyLibrary.Get(28), blue = SkyLibrary.Get(0), storm = SkyLibrary.Get(43);
            SkyMetrics.Expectation f = SkyMetrics.For(fair.Nominal, fair.HasLow, fair.Genus.Genus);
            TestAssert.That(f.Cumuliform && f.Feature && !f.Sheet && !f.Clear && f.Cover > 0.15f && f.Cover < 0.30f, "sky 8 is a cumulus-group sky of about 16 % cover");
            SkyMetrics.Expectation o = SkyMetrics.For(overcast.Nominal, overcast.HasLow, overcast.Genus.Genus);
            TestAssert.That(o.Sheet && !o.Cumuliform && !o.Feature && o.Cover > 0.9f, "sky 29 is an overcast sheet, judged on cover");
            TestAssert.That(SkyMetrics.For(blue.Nominal, blue.HasLow, blue.Genus.Genus).Clear, "sky 1 is clear");
            SkyDef veil = SkyLibrary.Get(3);
            TestAssert.That(SkyMetrics.For(veil.Nominal, veil.HasLow, veil.Genus.Genus).Faint, "sky 4's cirrostratus veil is faint (nominal sky cover 0.11)");
            TestAssert.That(SkyMetrics.For(storm.Nominal, storm.HasLow, storm.Genus.Genus).Cumuliform, "sky 44's storm is cumuliform");
            SkyDef cirrus = SkyLibrary.Get(1);
            SkyMetrics.Expectation c2 = SkyMetrics.For(cirrus.Nominal, cirrus.HasLow, cirrus.Genus.Genus);
            TestAssert.That(c2.Clear && c2.ThinOnly && Math.Abs(c2.LayerCover - 0.30f) < 1e-6f, "sky 2's cirrus is a thin-only Clear sky whose layer must be seen");
            SkyDef two = SkyLibrary.Get(7);
            TestAssert.That(!SkyMetrics.For(two.Nominal, two.HasLow, two.Genus.Genus).ThinOnly, "a sky with cumulus is not thin-only: its low cloud hides the layer's own area");
        }
    }
}
