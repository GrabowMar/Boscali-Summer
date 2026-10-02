using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The handful of numeric helpers the weather field is built from. Pure and allocation-free,
    /// so the host, every client and the tests evaluate exactly the same sky.
    /// </summary>
    internal static class WeatherMath
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        public static float Clamp(float x, float min, float max) => x < min ? min : x > max ? max : x;

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Hermite smoothstep between two edges; the only easing the field uses.</summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>1 inside [0, rise-end], easing in over the rise and out over the fall.</summary>
        public static float Envelope(float x, float riseStart, float riseEnd, float fallStart, float fallEnd)
            => Smoothstep(riseStart, riseEnd, x) * (1f - Smoothstep(fallStart, fallEnd, x));

        public static float Hash01(uint seed, int a, int b = 0, int c = 0)
            => Deterministic.UnitFloat(Deterministic.Hash((int)seed, a, b, c));

        public static float HashRange(uint seed, int a, int b, int c, float min, float max)
            => Lerp(min, max, Hash01(seed, a, b, c));

        /// <summary>Downwind heading in degrees to a unit vector on the (x east, z north) plane.</summary>
        public static void HeadingToVector(float headingDeg, out float x, out float z)
        {
            float r = headingDeg * Deg2Rad;
            x = (float)Math.Sin(r);
            z = (float)Math.Cos(r);
        }

        public static float VectorToHeading(float x, float z)
        {
            float h = (float)Math.Atan2(x, z) * Rad2Deg;
            return WrapHeading(h);
        }

        public static float WrapHeading(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>Shortest signed angle from a to b in degrees, in (-180, 180].</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = WrapHeading(b - a);
            return d > 180f ? d - 360f : d;
        }

        public static float LerpAngle(float a, float b, float t) => WrapHeading(a + DeltaAngle(a, b) * t);

        /// <summary>
        /// Smooth 2-D value noise in [0, 1] with a quintic fade, lattice spacing 1. Deterministic
        /// per seed; continuous in both coordinates, which is what keeps rain patches from
        /// popping as they drift.
        /// </summary>
        public static float ValueNoise(uint seed, float x, float y)
        {
            double fx = Math.Floor(x);
            double fy = Math.Floor(y);
            int ix = (int)fx;
            int iy = (int)fy;
            float tx = (float)(x - fx);
            float ty = (float)(y - fy);
            float ux = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f);
            float uy = ty * ty * ty * (ty * (ty * 6f - 15f) + 10f);

            float a = Hash01(seed, ix, iy, 911);
            float b = Hash01(seed, ix + 1, iy, 911);
            float c = Hash01(seed, ix, iy + 1, 911);
            float d = Hash01(seed, ix + 1, iy + 1, 911);
            return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
        }

        /// <summary>Three octaves of <see cref="ValueNoise"/>, renormalised to [0, 1].</summary>
        public static float Fbm(uint seed, float x, float y)
        {
            float n = ValueNoise(seed, x, y) * 0.57f
                    + ValueNoise(seed + 101u, x * 2.03f + 17.1f, y * 2.03f - 9.3f) * 0.29f
                    + ValueNoise(seed + 202u, x * 4.11f - 5.7f, y * 4.11f + 3.9f) * 0.14f;
            return Clamp01(n);
        }
    }
}
