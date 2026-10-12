using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// Cloud streets (spec 6): where a row's Streets column is above zero the low volume's body field is stretched along the wind by
    /// 1 + Stretch * Streets and given parallel rows across it, 2.5-4 km apart (<see cref="NoiseXform.StreetSpacing"/>). The lattice
    /// supplies the elongated street zones (aspect 3-6, <see cref="CumulusLattice"/>); the rows and the stretch come from the
    /// shader, and CloudBodies mirrors them. FlightCloud.shader's StreetCompress, StreetRows and StreetBody are the twins:
    /// change both together.
    /// </summary>
    internal static class StreetMath
    {
        /// <summary>The body field is stretched along the wind by 1 + Stretch * Streets: at Streets 0.8 the lobes are 3.4 times longer than wide.</summary>
        public const float Stretch = 3f;

        /// <summary>How far a row can move the body value: Streets * RowAmplitude * (rows - 0.5), so 0.8 gives plus or minus 0.18.</summary>
        public const float RowAmplitude = 0.45f;

        /// <summary>The share of the along-wind coordinate removed before the noise is read: p' = p - u * dot(p, u) * Compress, so the
        /// noise features lengthen along u by 1 / (1 - Compress) = 1 + Stretch * Streets. Zero at Streets 0: the point is untouched.</summary>
        public static float Compress(float streets)
        {
            float k = Stretch * Scalar.Clamp01(streets);
            return k / (1f + k);
        }

        /// <summary>0..1 rows across the wind: a sine of the across-wind coordinate, with the row spacing, the first row's phase and a
        /// slow wobble (the broad noise) that bends the rows.</summary>
        public static float Rows(float across, float spacing, float phase, float wobble) =>
            0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * across / spacing + phase + wobble);

        /// <summary>The body value with the rows added, clamped to 0..1.</summary>
        public static float Body(float body, float streets, float rows) =>
            Scalar.Clamp01(body + Scalar.Clamp01(streets) * RowAmplitude * (rows - 0.5f));
    }
}
