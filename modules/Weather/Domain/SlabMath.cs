using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The pure maths of the thin layers (spec 4.3, 7.4): the cover masks, the morphology weights, the vertical profile and
    /// its integral. FlightCloud.shader's CoverMask, SlabCoverMask, SlabWeights, SlabF and SlabOverlap are copies. Change both together.
    /// </summary>
    internal static class SlabMath
    {
        /// <summary>The vertical profile is saturate(K*h*(1-h)): the old middle layer's dome (4h(1-h) times 1.3, clamped), a plateau with rounded shoulders.</summary>
        public const float K = 5.2f;

        /// <summary>The gain of the thin-layer rank, saturate(0.5 + (pattern - 0.52) * RankGain). It does not depend on Detail on purpose: the
        /// covered area of a layer follows its Cover (a gain of 4.5 * Detail left cirrus at cover 0.30 covering 2-4 % of the sky), and Detail only
        /// scales the interior density. FlightCloud.shader's Slab2D hard-codes 4.5. Change both together.</summary>
        public const float RankGain = 4.5f;

        /// <summary>Where K*h*(1-h) reaches 1, below and above the middle: (1 -/+ sqrt(1 - 4/K)) / 2.</summary>
        public static readonly float H1 = (float)((1.0 - Math.Sqrt(1.0 - 4.0 / K)) / 2.0);
        public static readonly float H2 = 1f - H1;

        /// <summary>The low-cloud coverage remap: <paramref name="cover"/> of the sky becomes cloud, the rest stays clear.</summary>
        public static float CoverMask(float body, float cover)
        {
            float open = Scalar.Clamp01((body - (1f - cover)) / Math.Max(0.18f, cover));
            return WeatherMath.Lerp(open, Math.Max(open, 0.55f + body * 0.45f), WeatherMath.Smoothstep(0.82f, 1f, cover));
        }

        /// <summary>
        /// The thin layers' coverage remap (spec 4.3): the old mask's edge is max(0.18, cover) wide, so at cover 0.02 its peak
        /// was 0.11. This one is sharp below 0.15 and hands over to <see cref="CoverMask"/> by 0.30. <paramref name="softness"/>
        /// widens or narrows the sharp edge: 0.5 is the spec's edge exactly, 0 half as wide, 1 half as wide again.
        /// </summary>
        public static float SlabCoverMask(float body, float cover, float softness)
        {
            float edge = Math.Max(0.02f, 0.5f * cover) * WeatherMath.Lerp(0.5f, 1.5f, softness);
            float sharp = Scalar.Clamp01((body - (1f - cover)) / edge);
            return WeatherMath.Lerp(sharp, CoverMask(body, cover), WeatherMath.Smoothstep(0.15f, 0.30f, cover));
        }

        /// <summary>
        /// The four morphology weights of a layer, normalised to sum 1, with any weight under 0.05 dropped and the rest
        /// renormalised (so a fade never switches category, and the shader skips a tap that cannot show). A layer with no weights is a veil.
        /// </summary>
        public static void Weights(in SkyLayer row, out float cells, out float ripples, out float streaks, out float veil)
        {
            float sum = row.Cells + row.Ripples + row.Streaks + row.Veil;
            if (sum <= 0f) { cells = ripples = streaks = 0f; veil = 1f; return; }
            cells = row.Cells / sum; ripples = row.Ripples / sum; streaks = row.Streaks / sum; veil = row.Veil / sum;
            if (cells < 0.05f) cells = 0f;
            if (ripples < 0.05f) ripples = 0f;
            if (streaks < 0.05f) streaks = 0f;
            if (veil < 0.05f) veil = 0f;
            float kept = cells + ripples + streaks + veil; // the largest of four weights summing to 1 is at least 0.25
            cells /= kept; ripples /= kept; streaks /= kept; veil /= kept;
        }

        /// <summary>The altitude profile of a layer: 0 at its base and top, a plateau between.</summary>
        public static float Profile(float h) => h <= 0f || h >= 1f ? 0f : Math.Min(1f, K * h * (1f - h));

        private static float Rise(float h) => K * (0.5f * h * h - h * h * h / 3f);

        /// <summary>The integral of <see cref="Profile"/> from 0 to h (0.77 at h = 1, the factor the old shader baked in).</summary>
        public static float Integral(float h)
        {
            h = Scalar.Clamp01(h);
            if (h <= H1) return Rise(h);
            if (h <= H2) return Rise(H1) + (h - H1);
            return Rise(H1) + (H2 - H1) + (Rise(H1) - Rise(1f - h));
        }

        /// <summary>
        /// The optical weight of one ray sample that spans altitudes [lo, hi] through a layer [baseY, topY]: the mean of the
        /// profile over the sample, so that length x overlap x density is the sample's optical depth whatever the slant.
        /// Under 40 m of altitude span it is the profile at the middle of the sample.
        /// </summary>
        public static float Overlap(float lo, float hi, float baseY, float topY)
        {
            if (hi < lo) { float t = lo; lo = hi; hi = t; }
            float thick = topY - baseY;
            if (thick <= 0f) return 0f;
            float span = hi - lo;
            if (span < 40f) return Profile(((lo + hi) * 0.5f - baseY) / thick);
            float ha = Scalar.Clamp01((lo - baseY) / thick), hb = Scalar.Clamp01((hi - baseY) / thick);
            if (hb <= ha) return 0f;
            return thick * (Integral(hb) - Integral(ha)) / span;
        }
    }
}
