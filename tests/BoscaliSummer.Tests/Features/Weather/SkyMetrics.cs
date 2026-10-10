using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The sky contact sheet's measurable pass criteria (spec 10.3), computed from one pose rendered twice: the sky with clouds (its
    /// luminance) and its per-pixel difference from the same sky without clouds. Images are top row first. Bands come from the ATMOS-X reference study (visual-targets.md section 2): cumulus lit/dark 3.0-4.4,
    /// overcast 1.5-2.5, luminance deviation 0.055-0.27, sky cover 6-99 %. Pure (no Unity), so the bench and the offline tests share it.
    /// </summary>
    internal static class SkyMetrics
    {
        /// <summary>The share of the frame the metrics read: the reference thumbnails' rows 7-77 of 124, between the top rim and the haze veil.</summary>
        public const float RowTop = 7f / 124f, RowBottom = 77f / 124f;

        public struct Result
        {
            /// <summary>Share of the region whose luminance differs from the cloud-free render by more than 0.06.</summary>
            public float CloudFraction;
            /// <summary>Share of the region whose difference from the cloud-free render exceeds 0.02: the area of even a faint layer (the cover metric needs 0.06).</summary>
            public float LayerFraction;
            public float OpenFraction => 1f - CloudFraction;
            public float LumaMean, LumaStdDev;
            /// <summary>Mean of the brightest 3 % of cloud pixels over the mean of the darkest 3 %; 0 when there are too few cloud pixels.</summary>
            public float LitDarkRatio;
            public bool HasLitDark;
            /// <summary>Twice the horizontal lag at which the luminance autocorrelation first falls under 0.5, in degrees: a feature's width.</summary>
            public float FeatureDegrees;
        }

        /// <summary>What a sky is expected to look like, from its nominal columns.</summary>
        public struct Expectation
        {
            public float Cover;
            public bool Cumuliform, Sheet, Clear;
            /// <summary>The sky's cloud is a cumulus group or a storm cell: until the lattice (slice 3) makes cover uniform, the sheet
            /// places the camera at the nearest one and only asks that it is in view (no cover band).</summary>
            public bool Feature;
            /// <summary>Thin high cloud and haze (nominal sky cover 0.05-0.15): a cirrus layer at opacity 0.15 changes few pixels by the
            /// 0.06 the cover metric needs, so these skies have no cover band; their layer is judged by LayerFraction instead (see ThinOnly).</summary>
            public bool Faint;
            /// <summary>The sky has no low cloud, so every pixel that differs from the open sky is a thin layer and the layer's visibility can be measured.</summary>
            public bool ThinOnly;
            /// <summary>The larger of the two thin layers' covers.</summary>
            public float LayerCover;
        }

        public static Expectation For(in SkyParams nominal, bool hasLow, LowGenus genus)
        {
            float cover = SkyMath.SkyCover(SkyMath.LowCover(nominal), nominal.Mid.Cover, nominal.OpacityMid, nominal.High.Cover, nominal.OpacityHigh);
            bool cumuliform = hasLow && genus <= LowGenus.Cumulonimbus && SkyMath.LowCover(nominal) >= 0.12f;
            bool sheet = SkyMath.ClassOf(nominal) >= WeatherRegimeType.Overcast && nominal.SheetCover >= 0.8f;
            bool feature = hasLow && (nominal.CumulusCover > 0f || nominal.Convective > 0.3f || genus == LowGenus.Cumulonimbus);
            return new Expectation { Cover = cover, Cumuliform = cumuliform, Sheet = sheet, Clear = cover < 0.05f, Faint = cover >= 0.05f && cover < 0.15f, Feature = feature,
                ThinOnly = SkyMath.LowCover(nominal) < 0.05f, LayerCover = Math.Max(nominal.Mid.Cover, nominal.High.Cover) };
        }

        /// <summary>The mean absolute luminance difference of two same-sized frames over the metrics' rows: how different two skies look.</summary>
        public static float MeanAbsDifference(float[] a, float[] b, int width, int height)
        {
            int y0 = Math.Max(0, (int)(height * RowTop)), y1 = Math.Min(height, (int)(height * RowBottom));
            double sum = 0;
            int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < width; x++) { sum += Math.Abs(a[y * width + x] - b[y * width + x]); n++; }
            return n == 0 ? 0f : (float)(sum / n);
        }

        /// <param name="luma">Luminance of the frame with clouds, top row first.</param>
        /// <param name="difference">Per pixel, how far the frame is from the same pose without clouds (largest channel difference, 0-1); above 0.06 is cloud.</param>
        public static Result Measure(float[] luma, float[] difference, int width, int height, float degreesPerPixel)
        {
            int y0 = Math.Max(0, (int)(height * RowTop)), y1 = Math.Min(height, (int)(height * RowBottom));
            double sum = 0, sumSquares = 0;
            int total = 0, cloud = 0, layer = 0;
            var cloudLuma = new List<float>();
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < width; x++)
                {
                    float l = luma[y * width + x];
                    sum += l; sumSquares += l * l; total++;
                    if (difference[y * width + x] > 0.02f) layer++;
                    if (difference[y * width + x] > 0.06f) { cloud++; cloudLuma.Add(l); }
                }
            var result = new Result();
            if (total == 0) return result;
            result.CloudFraction = (float)cloud / total;
            result.LayerFraction = (float)layer / total;
            result.LumaMean = (float)(sum / total);
            result.LumaStdDev = (float)Math.Sqrt(Math.Max(0.0, sumSquares / total - (sum / total) * (sum / total)));
            if (cloudLuma.Count >= 400)
            {
                cloudLuma.Sort();
                int n = Math.Max(1, (int)(cloudLuma.Count * 0.03));
                double dark = 0, lit = 0;
                for (int i = 0; i < n; i++) { dark += cloudLuma[i]; lit += cloudLuma[cloudLuma.Count - 1 - i]; }
                result.HasLitDark = dark > 1e-4;
                result.LitDarkRatio = result.HasLitDark ? (float)(lit / dark) : 0f;
            }
            result.FeatureDegrees = Feature(luma, width, y0, y1) * degreesPerPixel;
            return result;
        }

        private static float Feature(float[] luma, int width, int y0, int y1)
        {
            int rows = 24, maxLag = width / 4;
            var correlation = new double[maxLag + 1];
            int used = 0;
            for (int r = 0; r < rows; r++)
            {
                int y = y0 + (y1 - y0) * (2 * r + 1) / (2 * rows);
                double mean = 0;
                for (int x = 0; x < width; x++) mean += luma[y * width + x];
                mean /= width;
                double variance = 0;
                for (int x = 0; x < width; x++) { double d = luma[y * width + x] - mean; variance += d * d; }
                if (variance < 1e-6) continue;
                used++;
                for (int lag = 0; lag <= maxLag; lag++)
                {
                    double c = 0;
                    for (int x = 0; x + lag < width; x++) c += (luma[y * width + x] - mean) * (luma[y * width + x + lag] - mean);
                    correlation[lag] += c / variance;
                }
            }
            if (used == 0) return 0f;
            for (int lag = 1; lag <= maxLag; lag++)
                if (correlation[lag] / used < 0.5) return 2f * lag;
            return 2f * maxLag;
        }

        /// <summary>The failed criteria of a measured sky, empty when it passes. Bands apply only where the study gives one.</summary>
        public static List<string> Failures(in Result r, in Expectation e)
        {
            var failed = new List<string>();
            // A thin layer must be seen. Where the sky has no low cloud every pixel that differs at all from the open sky is the layer, and it must
            // show at least half its own cover (0.02 and 0.5 are initial figures). A Clear sky used to pass for showing little cloud, which is also
            // what a layer that draws nothing shows.
            if (e.ThinOnly && e.LayerCover >= 0.10f && r.LayerFraction < 0.5f * e.LayerCover)
                failed.Add("the thin layer is invisible: " + r.LayerFraction.ToString("0.00") + " of the view differs from the open sky, its cover is " +
                    e.LayerCover.ToString("0.00") + " (the visible area must be at least half the cover)");
            if (e.Clear)
            {
                if (r.CloudFraction > 0.15f) failed.Add("a clear sky shows " + r.CloudFraction.ToString("0.00") + " cloud (limit 0.15)");
                return failed;
            }
            if (e.Faint && !e.Feature) return failed;
            float lowest = e.Feature ? 0.03f : Math.Max(0.06f, e.Cover - 0.35f), highest = e.Feature || e.Sheet ? 1f : Math.Min(0.99f, e.Cover + 0.25f);
            if (r.CloudFraction < lowest || r.CloudFraction > highest)
                failed.Add("cloud cover " + r.CloudFraction.ToString("0.00") + " outside " + lowest.ToString("0.00") + ".." + highest.ToString("0.00") + " (nominal sky cover " + e.Cover.ToString("0.00") + ")");
            if (e.Cover >= 0.1f && (r.LumaStdDev < 0.055f || r.LumaStdDev > 0.27f))
                failed.Add("luminance deviation " + r.LumaStdDev.ToString("0.000") + " outside 0.055..0.27");
            if (r.HasLitDark && e.Cumuliform && (r.LitDarkRatio < 3.0f || r.LitDarkRatio > 4.4f))
                failed.Add("lit/dark " + r.LitDarkRatio.ToString("0.0") + " outside the cumulus band 3.0..4.4");
            if (r.HasLitDark && e.Sheet && (r.LitDarkRatio < 1.5f || r.LitDarkRatio > 2.5f))
                failed.Add("lit/dark " + r.LitDarkRatio.ToString("0.0") + " outside the overcast band 1.5..2.5");
            return failed;
        }
    }
}
