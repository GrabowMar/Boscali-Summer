using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The columns of a sky that are derived, not authored (spec 4.1): the optical strength of each thin layer, the horizon
    /// deck's cover and depth, and the cumulus activation level. Run once at library build, after a jitter, and per tape
    /// texel by the baker, so the picture and the CPU cover agree.
    /// </summary>
    internal static class SkyDerived
    {
        /// <summary>Grazing views stack cumulus groups, so the horizon reads about this many times the ground fraction (spec 4.1; tuned on the seam metric in slice 4).</summary>
        public const float HorizonStack = 1.5f;

        public static void Apply(ref SkyParams sky)
        {
            sky.Mid.Strength = 0.6f * sky.OpacityMid;
            sky.High.Strength = 0.8f * sky.OpacityHigh;
            sky.HorizonDepth = Math.Max(300f, Math.Max(sky.DeckDepth, 0.5f * sky.PuffDepth));
            sky.HorizonCover = Math.Max(0.9f * sky.SheetCover,
                Math.Min(1f, HorizonStack * sky.CumulusCover + 0.2f * sky.Convective));
            sky.CumulusLevel = CumulusLevels.For(sky.CumulusCover, sky.CapacityCover);
        }
    }

    /// <summary>
    /// The cumulus activation level (spec 6): the level at which a lattice of this genus reaches the wanted mean cover.
    /// A group at rank r has strength smoothstep(r*0.7, r*0.7 + 0.35, level), so the mean strength of the lattice is
    /// M(level), an S-curve. With Poisson overlap a lattice of mean strength mu has cover 1 - (1 - capacity)^mu, so
    /// mu = ln(1 - cover) / ln(1 - capacity) and the level is the inverse of M at mu.
    /// </summary>
    internal static class CumulusLevels
    {
        public static float MeanStrength(float level)
        {
            if (level <= 0f) return 0f;
            if (level <= 0.35f)
            {
                float y = level / 0.35f;
                return 0.5f * (y * y * y - y * y * y * y / 2f);
            }
            if (level <= 0.70f) return (level - 0.35f) / 0.7f + 0.25f;
            float z = Math.Min(1f, (level - 0.70f) / 0.35f);
            return (level - 0.35f) / 0.7f + 0.5f * (0.5f - z * z * z + z * z * z * z / 2f);
        }

        /// <summary>Inverse of <see cref="MeanStrength"/> by bisection (M is strictly increasing). A mu above M(1) clamps to level 1.</summary>
        public static float Inverse(float mu)
        {
            if (mu <= 0f) return 0f;
            if (mu >= MeanStrength(1f)) return 1f;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (MeanStrength(mid) < mu) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>mu = ln(1 - cumulusCover) / ln(1 - capacity), clamped to 0..1; 0 when either is zero.</summary>
        public static float Mu(float cumulusCover, float capacity)
        {
            if (cumulusCover <= 0f || capacity <= 0f) return 0f;
            double mu = Math.Log(1.0 - Math.Min(cumulusCover, 0.95f)) / Math.Log(1.0 - Math.Min(capacity, 0.95f));
            return Scalar.Clamp01((float)mu);
        }

        public static float For(float cumulusCover, float capacity) => Inverse(Mu(cumulusCover, capacity));
    }

    /// <summary>
    /// The class measure (spec 8.2). <see cref="ClassConditions"/> is nominal: a pure function of one sky row, with no
    /// sampling, so a pool, a jitter clamp and a test can all use it deterministically. It rises with each input.
    /// </summary>
    internal static class SkyMath
    {
        /// <summary>Rain only falls where the sky has closed: smoothstep(0.3, 0.6, cover).</summary>
        public static float Gate(float cover) => WeatherMath.Smoothstep(0.3f, 0.6f, cover);

        /// <summary>The low cover of a row: sheet and cumulus combined.</summary>
        public static float LowCover(in SkyParams s) => 1f - (1f - s.SheetCover) * (1f - s.CumulusCover);

        /// <summary>The cover that gates stratiform rain: the low cover, or the middle layer times its opacity (an opaque altostratus still rains).</summary>
        public static float RainCover(in SkyParams s) => Math.Max(LowCover(s), s.Mid.Cover * s.OpacityMid);

        /// <summary>Opacity-weighted sky cover of the three levels: 1 - (1 - low)(1 - mid*opacityMid)(1 - high*opacityHigh).</summary>
        public static float SkyCover(float low, float mid, float opacityMid, float high, float opacityHigh) =>
            1f - (1f - low) * (1f - mid * opacityMid) * (1f - high * opacityHigh);

        private static readonly float[] CoverKnots = { 0f, 0.125f, 0.28f, 0.45f, 0.75f, 1f };
        private static readonly float[] ConditionKnots = { 0.02f, 0.12f, 0.28f, 0.45f, 0.60f, 0.733f };

        /// <summary>Piecewise-linear map from sky cover to the vanilla conditions scale, anchored on the old state table.</summary>
        public static float G(float cover)
        {
            cover = Scalar.Clamp01(cover);
            for (int i = 1; i < CoverKnots.Length; i++)
                if (cover <= CoverKnots[i])
                    return WeatherMath.Lerp(ConditionKnots[i - 1], ConditionKnots[i],
                        (cover - CoverKnots[i - 1]) / (CoverKnots[i] - CoverKnots[i - 1]));
            return ConditionKnots[ConditionKnots.Length - 1];
        }

        /// <summary>min(1, g(M) + 0.12*(1 - exp(-R/1.5)) + K*(0.30 + 0.25*Sev)); M the opacity-weighted sky cover, R the gated stratiform rain, K the convective share.</summary>
        public static float ClassConditions(in SkyParams s)
        {
            float m = SkyCover(LowCover(s), s.Mid.Cover, s.OpacityMid, s.High.Cover, s.OpacityHigh);
            float rain = s.StratRain * Gate(RainCover(s));
            float conditions = G(m) + 0.12f * (1f - (float)Math.Exp(-rain / 1.5f)) +
                Scalar.Clamp01(s.Convective) * (0.30f + 0.25f * Scalar.Clamp01(s.Severity));
            return Math.Min(1f, conditions);
        }

        /// <summary>The class a row reads as, through the unchanged band edges 0.12 / 0.28 / 0.45 / 0.60 / 0.75 / 0.88.</summary>
        public static WeatherRegimeType ClassOf(in SkyParams s) => RegimeSnapshot.FromConditions(ClassConditions(s)).Type;
    }
}
