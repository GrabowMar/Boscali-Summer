using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    /// <summary>Uniform math for the helmet visor overlay.</summary>
    internal static class VisorMath
    {
        /// <summary>Below this every visor layer is invisible and the overlay quad is disabled.</summary>
        public const float Idle = .003f;

        /// <summary>Smoked sun visor by day: clear below -2 deg sun elevation, fully down from 10 deg.</summary>
        public static float SunVisor(float sunElevationDeg) =>
            Scalar.IsFinite(sunElevationDeg) ? Scalar.Clamp01((sunElevationDeg + 2f) / 12f) : 0f;

        /// <summary>Helmet brow shadow as the helmet slides down: none to 6 G, full at 9 G.</summary>
        public static float Brow(float loadG) => Scalar.IsFinite(loadG) ? Scalar.Clamp01((loadG - 6f) / 3f) : 0f;

        public static float Ease(float current, float target, float dt, float seconds)
        {
            if (!Scalar.IsFinite(target) || !(dt > 0f) || !Scalar.IsFinite(dt)) return current;
            if (!(seconds > 0f)) return target;
            return current + (target - current) * Scalar.Clamp01(dt / seconds);
        }

        public static bool FogsOnExhale(float exertion01, float temperatureC, bool temperatureKnown) =>
            exertion01 > .5f || (temperatureKnown && temperatureC < -10f);
    }

    /// <summary>
    /// The visor's soft layer image, built once off the main thread (RGBA, row 0 = bottom):
    /// G = breath-fog mask (lower edge), B = helmet brow (top edge). R and A are unused. Scratches and
    /// the shell frame are drawn by the shader at screen resolution, never baked: a low-res
    /// mask stretched across the screen reads as blobs.
    /// </summary>
    internal static class VisorMask
    {
        public static byte[] Build(int width, int height, int seed = 7319)
        {
            if (width < 16 || height < 16 || width > 4096 || height > 4096) return null;
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)(width - 1);
                    float edge = Smooth(.15f, 0f, Math.Min(u, 1f - u)) * Smooth(.6f, 0f, v);
                    float fog = Math.Min(1f, Smooth(.34f, 0f, v) * (.7f + .3f * Noise(x, y, seed)) + .25f * edge);
                    float arch = .03f * (1f - (2f * u - 1f) * (2f * u - 1f));
                    float brow = Smooth(.93f - arch, 1f, v);
                    int p = (y * width + x) * 4;
                    rgba[p + 1] = (byte)(fog * 255f + .5f);
                    rgba[p + 2] = (byte)(brow * 255f + .5f);
                }
            }
            return rgba;
        }

        private static float Smooth(float edge0, float edge1, float x) => Scalar.SmoothStep(edge0, edge1, x);

        // Smooth value noise on a 16 px lattice.
        private static float Noise(int x, int y, int seed)
        {
            float fx = x / 16f, fy = y / 16f;
            int ix = (int)fx, iy = (int)fy;
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }

    /// <summary>Breath fog at the visor's lower edge: blooms in 0.25 s on an exhale, clears in about 1.7 s.</summary>
    internal struct FogPulse
    {
        public float Value;
        private float peak;
        private bool rising;

        public void Trigger(float strength)
        {
            if (!Scalar.IsFinite(strength)) return;
            peak = Math.Max(Value, Scalar.Clamp01(strength));
            rising = true;
        }

        public void Step(float dt)
        {
            if (!(dt > 0f) || !Scalar.IsFinite(dt)) return;
            if (rising)
            {
                Value = Math.Min(peak, Value + dt / .25f);
                if (Value >= peak) rising = false;
                return;
            }
            Value *= (float)Math.Exp(-dt / .3f);
            if (Value < VisorMath.Idle) Value = 0f;
        }
    }
}
