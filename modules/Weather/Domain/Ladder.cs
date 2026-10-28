using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The feature-size ladders (spec 4.2, 6): noise is never sampled at a scale that varies in space or time. Sizes sit
    /// on a sqrt(2) ladder; authoring uses integer rungs and only a baked ramp holds a fractional rung index, which the
    /// shader and <see cref="Split"/> turn into two integer rungs and a smoothstep weight. FlightCloud.shader's
    /// LobeMetres, PeriodMetres and BodiesLadder are copies of these. Change both together.
    /// </summary>
    internal static class Ladder
    {
        public const int LobeRungs = 7, PeriodRungs = 13;
        public const float LobeBase = 700f, PeriodBase = 250f;

        /// <summary>Lobe rungs 0-6 = 700, 990, 1400, 1980, 2800, 3960, 5600 m.</summary>
        public static float LobeMetres(float rung) => LobeBase * (float)Math.Pow(2.0, 0.5 * rung);

        /// <summary>Period rungs 0-12 = 250, 354, 500, 707, 1000, 1414, 2000, 2828, 4000, 5657, 8000, 11314, 16000 m.</summary>
        public static float PeriodMetres(float rung) => PeriodBase * (float)Math.Pow(2.0, 0.5 * rung);

        public static float NearestLobeRung(float metres) =>
            Math.Max(0f, Math.Min(LobeRungs - 1, (float)Math.Round(2.0 * Math.Log(metres / LobeBase, 2.0))));

        /// <summary>A fractional rung index to its lower integer rung and the weight of the next one: w = smoothstep(0.35, 0.65, frac).</summary>
        public static void Split(float index, out float r0, out float w)
        {
            r0 = (float)Math.Floor(index);
            w = WeatherMath.Smoothstep(0.35f, 0.65f, index - r0);
        }

        /// <summary>Mixes two raw noise values at w while keeping contrast: the weighted sum's deviation from its mean (0.5 for the
        /// lobe sum, 0.52 for the thin-layer patterns) is divided by sqrt((1-w)^2 + w^2), the standard deviation of a weighted sum
        /// of two independent fields.</summary>
        public static float Mix(float raw0, float raw1, float w, float mean = 0.5f)
        {
            if (w <= 0.02f) return raw0;
            if (w >= 0.98f) return raw1;
            float mixed = raw0 + (raw1 - raw0) * w;
            return mean + (mixed - mean) / (float)Math.Sqrt((1f - w) * (1f - w) + w * w);
        }

        /// <summary>The erosion-period multiplier that makes the edge texture follow the lobe: clamp(0.9*lobe, 600, 2500) / 1250.</summary>
        public static float EdgeScale(float lobeMetres) => Scalar.Clamp(0.9f * lobeMetres, 600f, 2500f) / 1250f;
    }
}
