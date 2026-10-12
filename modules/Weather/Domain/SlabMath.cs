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

        /// <summary>
        /// The thin-layer rank law (look pass round 1): the rank of a pattern value is its place in the pattern's own distribution, a Gaussian
        /// CDF of (pattern - 0.52) / sigma (the tanh form of the GELU), so the share of the sky whose rank exceeds 1 - c is about c. The old linear
        /// law saturate(0.5 + (pattern - 0.52) * 4.5) pushed most of the sky to the rails: cirrus of cover .12 filled half the sky and Detail
        /// (which scales the density by the rank) did nothing inside the cover. It does not depend on Detail on purpose. FlightCloud.shader's Rank
        /// is the twin. Change both together.
        /// </summary>
        public static float Rank(float pattern, float sigma)
        {
            float z = Math.Max(-5f, Math.Min(5f, (pattern - 0.52f) / sigma));
            return 0.5f + 0.5f * (float)Math.Tanh(0.7978846f * (z + 0.044715f * z * z * z));
        }

        /// <summary>The standard deviation of a layer's pattern value for its morphology weights (measured on the shipped volume by
        /// ThinLayerTests.PatternSigmaMatchesMeasurement; the weights are those of <see cref="Weights"/>, summing to at most 1).</summary>
        public static float PatternSigma(float cells, float ripples, float streaks)
        {
            float sum = cells + ripples + streaks;
            return sum <= 0f ? SigmaCells : Math.Max(0.02f, (cells * SigmaCells + ripples * SigmaRipples + streaks * SigmaStreaks) / sum);
        }
        public const float SigmaCells = 0.095f, SigmaRipples = 0.190f, SigmaStreaks = 0.082f;

        /// <summary>Tile in metres of the mesoscale fetch that carries the patch field (R) and the cell-size field (G): another tile than the
        /// 27 km warp fetch, so the two never line up.</summary>
        public const float PatchTile = 31000f;
        /// <summary>The domain warp of the pattern in scales (the 27 km fetch bends the pattern by up to WarpGain / 2 scales; kept at 1.6: 2.0 pushed the 8-bit parity of the cell probes past the density check tolerance) and the
        /// bend of the cell coordinate in cells (was 2.2).</summary>
        public const float WarpGain = 1.6f, BendGain = 1.5f;
        /// <summary>How far the curl bends a streak coordinate, in scales per unit of Curl (was 2): hooked cirrus read as molten swirls with closed loops.</summary>
        public const float CurlGain = 0.6f;
        /// <summary>Look pass round 2. The cell small-tap period in cells (was 0.41: it collapsed to 3-6 px dust and a fine cross-hatch at altitude); the phase
        /// bend of the two ripple row sets in radians per unit of bend (was 10 and 9: their gradient could cancel the rows' own, which drew concentric
        /// rings); and how far a streak's along-coordinate is shifted by a noise of the across position, in spans (so dashes of neighbouring fibres
        /// no longer line up in rows).</summary>
        public const float SmallCell = 0.62f, RowBend1 = 6f, RowBend2 = 5.4f, StreakStagger = 1.2f;
        /// <summary>The low volume's lobe lookup: the lookup point is warped by a vector noise on a FIXED tile of LobeWarpWave metres (a tile that follows
        /// the lobe rung would move the noise coordinate by position x d(1/period) wherever the rung ramps, and the CPU and GPU would disagree), by
        /// up to LobeWarp x the lobe width, capped at LobeWarpMax metres; and where a size field is high the lobes are LobeSmall of their size (a second
        /// tap turned 37 degrees).</summary>
        public const float LobeWarp = 1.6f, LobeWarpMax = 2200f, LobeWarpWave = 7000f, LobeSmall = 0.6f;
        public const float PatchLo = 0.38f, PatchHi = 0.66f;
        /// <summary>1 / the mean of <see cref="PatchMask"/> over a layer: the cover inside the patches is lifted by this, so the layer's
        /// average cover stays its Cover while the lanes between patches are clear.</summary>
        public const float PatchGain = 2.17f;
        /// <summary>The cover the rank is compared against is lifted by this: the soft edge and the depth falloff of the density leave the
        /// outer third of the covered area under the 0.1 that counts as visible, so the visible share would otherwise sit at 0.6-0.8 of Cover.</summary>
        public const float CoverTrim = 1.05f;

        /// <summary>The trim of a layer: <see cref="CoverTrim"/>, plus up to 0.9 more for the streak share and 0.45 for the rest at low covers (a streak pattern has a shorter
        /// tail than a cell pattern, so at cover 0.03-0.08 cirrus showed 0.3-0.6 of its Cover), and none for a veil.</summary>
        public static float Trim(float cover, float streaks, float veil)
            => WeatherMath.Lerp(CoverTrim + (0.9f * streaks + 0.45f * (1f - streaks - veil)) * (1f - WeatherMath.Smoothstep(0.02f, 0.3f, cover)), 1f, veil);
        public const float SizeLo = 0.15f, SizeHi = 0.85f;

        /// <summary>0 in a clear lane, 1 inside a patch, from the patch field (R of the mesoscale fetch).</summary>
        public static float PatchMask(float field) => WeatherMath.Smoothstep(PatchLo, PatchHi, field);

        /// <summary>How strongly a layer is patched: full for thin layers, none for decks (cover 0.6 and up) and none for a veil.</summary>
        public static float Patchiness(float cover, float veil) => (1f - WeatherMath.Smoothstep(0.2f, 0.6f, cover)) * (1f - veil);

        /// <summary>The factor on a layer's cover from the patch mask: 1 without patchiness, mask x gain with it.</summary>
        public static float PatchFactor(float mask, float patchiness) => WeatherMath.Lerp(1f, mask * PatchGain, patchiness);

        /// <summary>0 where cells are large, 1 where they are small, from the size field (G of the mesoscale fetch).</summary>
        public static float SizeMask(float field) => WeatherMath.Smoothstep(SizeLo, SizeHi, field);

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
