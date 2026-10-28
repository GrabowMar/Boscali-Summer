using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The base-height field (spec 6): seeded value noise at 20-40 km in the tape frame, scaled by the row's BaseVariation (150-400 m)
    /// and added to the cloud base in <see cref="WeatherField.Sample"/>, so a deck of one genus has a gently undulating underside instead
    /// of a flat ceiling. It is a function of the layout seed and the tape-frame position only, so it slides with the tape like every
    /// other layer. One field per <see cref="WeatherField"/>: it remembers the lattice values of the last cell it read, so a map row
    /// (which stays inside one 20 km cell for dozens of texels) pays for the four corner hashes once. Not thread-safe.
    /// </summary>
    internal sealed class BaseHeightField
    {
        public const float LargeScale = 40000f, SmallScale = 20000f;
        /// <summary>The field is the noise's deviation from its mean times this, clamped to +-1: value noise rarely leaves 0.3-0.7.</summary>
        public const float Gain = 2.6f;

        private const uint SaltMix = 0x5BE0CD19u;

        private readonly uint salt;
        // The last cell read at each octave and its four corner values.
        private readonly int[] cellX = { int.MinValue, int.MinValue }, cellZ = new int[2];
        private readonly float[] corners = new float[8];

        public BaseHeightField(uint layoutSeed)
        {
            LayoutSeed = layoutSeed;
            salt = layoutSeed ^ SaltMix;
        }

        public uint LayoutSeed { get; }

        /// <summary>The unit field in [-1, 1] at a tape-frame point; multiply by BaseVariation.</summary>
        public float At(float qx, float qz)
        {
            float n = 0.65f * Octave(0, qx / LargeScale, qz / LargeScale) + 0.35f * Octave(1, qx / SmallScale + 17.3f, qz / SmallScale - 9.1f);
            return Scalar.Clamp((n - 0.5f) * Gain, -1f, 1f);
        }

        private float Octave(int octave, float x, float z)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            int c = octave * 4;
            if (ix != cellX[octave] || iz != cellZ[octave])
            {
                cellX[octave] = ix;
                cellZ[octave] = iz;
                corners[c] = LatticeHash.Unit(salt, ix, iz, 0, octave);
                corners[c + 1] = LatticeHash.Unit(salt, ix + 1, iz, 0, octave);
                corners[c + 2] = LatticeHash.Unit(salt, ix, iz + 1, 0, octave);
                corners[c + 3] = LatticeHash.Unit(salt, ix + 1, iz + 1, 0, octave);
            }
            float tx = x - ix, tz = z - iz;
            float ux = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f), uz = tz * tz * tz * (tz * (tz * 6f - 15f) + 10f);
            float a = corners[c], b = corners[c + 1], cc = corners[c + 2], d = corners[c + 3];
            return a + (b - a) * ux + (cc - a) * uz + (a - b - cc + d) * ux * uz;
        }
    }
}
