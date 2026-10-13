using System;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Generates the tiling 3D noise volume the raymarch shader shapes clouds with.
    /// R = weather variation, G = fine erosion, B = single-scale rounded lobes.
    /// </summary>
    /// <remarks>
    /// Pure System.Math on plain arrays -- no UnityEngine calls -- so it can run on a
    /// worker thread. A 64^3 volume is tens of millions of hash evaluations, which would
    /// be a multi-second stall on the main thread under the game's old Mono runtime.
    /// </remarks>
    internal static class CloudNoise3D
    {
        private const int ValueOctaves = 4;
        private const int BaseWorleyOctaves = 3;
        private const int DetailWorleyOctaves = 2;
        private const int BasePeriod = 4;
        private const int DetailPeriod = 8;

        /// <summary>Returns size^3 RGBA bytes (4 per voxel), x-major then y then z.</summary>
        public static byte[] Generate(int size, int seed)
        {
            int count = size * size * size;
            float[] baseShape = new float[count];
            float[] detail = new float[count];
            byte[] bytes = new byte[count * 4];

            float inv = 1f / size;
            int index = 0;

            for (int z = 0; z < size; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++, index++)
                    {
                        float fx = x * inv;
                        float fy = y * inv;
                        float fz = z * inv;

                        float value = ValueFbm(fx, fy, fz, seed);
                        float billow = WorleyFbm(fx, fy, fz, BasePeriod, BaseWorleyOctaves, seed + 101, out float lobe);
                        bytes[index * 4 + 2] = (byte)(lobe * 255f);

                        // Same bounded remap as the 2D field: divisor is 2 - billow, in [1,2].
                        baseShape[index] = (value - (billow - 1f)) / (2f - billow);
                        detail[index] = WorleyFbm(fx, fy, fz, DetailPeriod, DetailWorleyOctaves, seed + 313, out _);
                    }
                }
            }

            Normalise(baseShape);
            Normalise(detail);

            for (int i = 0; i < count; i++)
            {
                byte r = (byte)(baseShape[i] * 255f);
                byte g = (byte)(detail[i] * 255f);
                int o = i * 4;
                bytes[o + 0] = r;
                bytes[o + 1] = g;
                bytes[o + 3] = 255;
            }

            return bytes;
        }

        private static float ValueFbm(float x, float y, float z, int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = BasePeriod;

            for (int octave = 0; octave < ValueOctaves; octave++)
            {
                sum += ValueNoise(x * period, y * period, z * period, period, seed + octave) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }

            return sum / total;
        }

        /// <summary>Inverted Worley fBm: near a feature point means dense.</summary>
        private static float WorleyFbm(float x, float y, float z, int basePeriod, int octaves, int seed, out float lobe)
        {
            lobe = 0f;
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = basePeriod;

            for (int octave = 0; octave < octaves; octave++)
            {
                float value = 1f - Worley(x * period, y * period, z * period, period, seed + octave);
                if (octave == 0) lobe = value;
                sum += value * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                period *= 2;
            }

            float result = sum / total;
            return result < 0f ? 0f : (result > 1f ? 1f : result);
        }

        private static float Worley(float x, float y, float z, int period, int seed)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            int zi = (int)Math.Floor(z);
            var ax = new Axis(xi, x, period, 374761393);
            var ay = new Axis(yi, y, period, 668265263);
            var az = new Axis(zi, z, period, 1440662683);
            int seedShare = unchecked(seed * 1274126177);
            float best = float.MaxValue;

            // The result is the smallest d over the 27 cells, whatever order they are met in. Each axis is walked floor cell, nearer neighbour, farther neighbour, and the
            // nearest possible point of a cell (Axis.Low, summed like d) only grows with the ranks: once a cell cannot beat the best so far, neither can the rest of its
            // row, nor of the rows after it, and nothing in it is hashed. (A cell whose point is no closer than the best could not change the minimum.)
            for (int rz = 0; rz < 3; rz++)
            {
                float lz = az.Low(rz);
                if (lz >= best) break;
                int dz = az.Delta(rz);
                for (int ry = 0; ry < 3; ry++)
                {
                    float ly = ay.Low(ry);
                    if (ly + lz >= best) break;
                    int dy = ay.Delta(ry);
                    for (int rx = 0; rx < 3; rx++)
                    {
                        if (ax.Low(rx) + ly + lz >= best) break;

                        int dx = ax.Delta(rx);
                        // One hash per cell, sliced into three 10-bit offsets: a third of
                        // the hashing of jittering each axis separately.
                        int h = HashCell(ax.Share(rx) + ay.Share(ry) + az.Share(rz) + seedShare);

                        float ox = xi + dx + Fraction[h & 1023] - x;
                        float oy = yi + dy + Fraction[(h >> 10) & 1023] - y;
                        float oz = zi + dz + Fraction[(h >> 20) & 1023] - z;

                        float d = ox * ox + oy * oy + oz * oz;
                        if (d < best)
                            best = d;
                    }
                }
            }

            float distance = (float)Math.Sqrt(best);
            return distance > 1f ? 1f : distance;
        }

        /// <summary>i / 1023f for the 10-bit slices of a cell hash (the same division, done once).</summary>
        private static readonly float[] Fraction = MakeFraction();

        private static float[] MakeFraction()
        {
            var table = new float[1024];
            for (int i = 0; i < table.Length; i++) table[i] = i / 1023f;
            return table;
        }

        /// <summary>One axis of a Worley probe, by rank: 0 the floor cell, 1 the neighbour on the nearer side, 2 the one on the farther side. For each: the cell offset, its share of
        /// <see cref="HashInt"/>'s sum (the wrapped index times the hash multiplier; the sum wraps like the int arithmetic it came from, so any order of the terms gives one value), and the
        /// least squared offset a feature point of that cell can have. The bounds use the float operations the real offsets use, and rounding is monotone: the point of the cell below sits
        /// at most at the floor cell's index, the point of the cell above at least at the next index, so no offset (or square of one) in those cells can be smaller.</summary>
        private readonly struct Axis
        {
            private readonly int share0, share1, share2, delta1;
            private readonly float low1, low2;

            public Axis(int cell, float v, int period, int multiplier)
            {
                float below = cell - v, above = cell + 1 - v;
                float lowBelow = below * below, lowAbove = above * above;
                bool belowIsNear = lowBelow <= lowAbove;
                delta1 = belowIsNear ? -1 : 1;
                low1 = belowIsNear ? lowBelow : lowAbove;
                low2 = belowIsNear ? lowAbove : lowBelow;
                unchecked
                {
                    share0 = Wrap(cell, period) * multiplier;
                    share1 = Wrap(cell + delta1, period) * multiplier;
                    share2 = Wrap(cell - delta1, period) * multiplier;
                }
            }

            public int Delta(int rank) => rank == 0 ? 0 : rank == 1 ? delta1 : -delta1;

            public int Share(int rank) => rank == 0 ? share0 : rank == 1 ? share1 : share2;

            /// <summary>0 for the floor cell itself: a point inside it can lie anywhere.</summary>
            public float Low(int rank) => rank == 0 ? 0f : rank == 1 ? low1 : low2;
        }

        private static float ValueNoise(float x, float y, float z, int period, int seed)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int z0 = (int)Math.Floor(z);

            float fx = Smooth(x - x0);
            float fy = Smooth(y - y0);
            float fz = Smooth(z - z0);

            int x1 = Wrap(x0 + 1, period);
            int y1 = Wrap(y0 + 1, period);
            int z1 = Wrap(z0 + 1, period);
            x0 = Wrap(x0, period);
            y0 = Wrap(y0, period);
            z0 = Wrap(z0, period);

            float c00 = Lerp(Hash01(x0, y0, z0, seed), Hash01(x1, y0, z0, seed), fx);
            float c10 = Lerp(Hash01(x0, y1, z0, seed), Hash01(x1, y1, z0, seed), fx);
            float c01 = Lerp(Hash01(x0, y0, z1, seed), Hash01(x1, y0, z1, seed), fx);
            float c11 = Lerp(Hash01(x0, y1, z1, seed), Hash01(x1, y1, z1, seed), fx);

            return Lerp(Lerp(c00, c10, fy), Lerp(c01, c11, fy), fz);
        }

        private static void Normalise(float[] data)
        {
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] < min)
                    min = data[i];
                if (data[i] > max)
                    max = data[i];
            }

            float span = max - min;
            if (span < 1e-5f)
                return;

            for (int i = 0; i < data.Length; i++)
                data[i] = (data[i] - min) / span;
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static int Wrap(int v, int period)
        {
            // A power of two (every period in use): the mask is the same non-negative remainder, for negative v too, without the division.
            if (period > 0 && (period & (period - 1)) == 0) return v & (period - 1);
            int r = v % period;
            return r < 0 ? r + period : r;
        }

        private static int HashInt(int x, int y, int z, int seed)
        {
            unchecked
            {
                return HashCell(x * 374761393 + y * 668265263 + z * 1440662683 + seed * 1274126177);
            }
        }

        /// <summary>The mixing rounds of <see cref="HashInt"/>, applied to its weighted sum of the cell indices and the seed.</summary>
        private static int HashCell(int n)
        {
            unchecked
            {
                n = (n ^ (n >> 13)) * 1274126177;
                return (n ^ (n >> 16)) & 0x7fffffff;
            }
        }

        private static float Hash01(int x, int y, int z, int seed)
        {
            return HashInt(x, y, z, seed) / (float)0x7fffffff;
        }
    }
}
