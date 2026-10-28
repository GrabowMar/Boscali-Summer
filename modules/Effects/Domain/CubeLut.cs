using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Effects.Domain
{
    /// <summary>
    /// Reads the <c>.cube</c> 3D LUT text format (Resolve, Photoshop, every free LUT pack) and
    /// resamples it to the strip URP's Color Lookup accepts: <c>size*size</c> wide, <c>size</c>
    /// high, blue picks the slice, red runs along x inside it, green up y. Engine-free.
    /// </summary>
    internal static class CubeLut
    {
        public const int MaxSize = 65;

        /// <summary>RGB triples, red fastest, then green, then blue. Null when the text is not a complete 3D LUT.</summary>
        public static float[] Parse(string text, out int size)
        {
            size = 0;
            if (string.IsNullOrEmpty(text)) return null;
            float[] data = null;
            int filled = 0;
            char[] blanks = { ' ', '\t' };
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split(blanks, StringSplitOptions.RemoveEmptyEntries);
                if (char.IsLetter(line[0]))
                {
                    // ponytail: TITLE and DOMAIN_MIN/MAX are skipped, so the 0..1 domain is assumed
                    // (true of display LUTs); honour DOMAIN_* here if a scene-referred LUT ever matters.
                    if (parts[0] != "LUT_3D_SIZE") continue;
                    if (parts.Length < 2 || !int.TryParse(parts[1], out size) || size < 2 || size > MaxSize) return null;
                    data = new float[size * size * size * 3];
                    continue;
                }
                if (data == null || parts.Length < 3 || filled + 3 > data.Length) return null;
                for (int c = 0; c < 3; c++)
                    if (!float.TryParse(parts[c], NumberStyles.Float, CultureInfo.InvariantCulture, out data[filled++]))
                        return null;
            }
            return data != null && filled == data.Length ? data : null;
        }

        /// <summary>RGBA32 rows for a <c>size*size</c> by <c>size</c> strip, first row = green 0, trilinear resample.</summary>
        public static byte[] Bake(float[] cube, int cubeSize, int size)
        {
            var pixels = new byte[size * size * size * 4];
            float step = (cubeSize - 1) / (float)(size - 1);
            int at = 0;
            for (int g = 0; g < size; g++)
                for (int b = 0; b < size; b++)
                    for (int r = 0; r < size; r++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            float value = Sample(cube, cubeSize, r * step, g * step, b * step, c);
                            pixels[at++] = (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
                        }
                        pixels[at++] = 255;
                    }
            return pixels;
        }

        private static float Sample(float[] cube, int n, float r, float g, float b, int channel)
        {
            int r0 = Math.Min((int)r, n - 1), g0 = Math.Min((int)g, n - 1), b0 = Math.Min((int)b, n - 1);
            int r1 = Math.Min(r0 + 1, n - 1), g1 = Math.Min(g0 + 1, n - 1), b1 = Math.Min(b0 + 1, n - 1);
            float tr = r - r0, tg = g - g0, tb = b - b0;
            float At(int ri, int gi, int bi) => cube[((bi * n + gi) * n + ri) * 3 + channel];
            float Mix(float from, float to, float t) => from + (to - from) * t;
            float low = Mix(Mix(At(r0, g0, b0), At(r1, g0, b0), tr), Mix(At(r0, g1, b0), At(r1, g1, b0), tr), tg);
            float high = Mix(Mix(At(r0, g0, b1), At(r1, g0, b1), tr), Mix(At(r0, g1, b1), At(r1, g1, b1), tr), tg);
            return Mix(low, high, tb);
        }
    }
}
