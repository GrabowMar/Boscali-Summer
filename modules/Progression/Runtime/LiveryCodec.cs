using System;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>
    /// Packs straight RGBA32 pixels into DXT1 (or DXT5 when alpha matters) with a full mip chain: the
    /// formats the game ships its livery atlases in, so a painted copy costs what the original does
    /// instead of eight times as much. Pure arithmetic, so it runs on a worker thread and under the pure
    /// test suite. Endpoints are the block's colour box along its dominant diagonal with no inset, so a block
    /// that came out of a DXT texture goes back in within 5:6:5 rounding: the stock livery around the marking
    /// does not visibly degrade.
    /// </summary>
    internal static class LiveryCodec
    {
        /// <summary>Bytes of the whole chain for a texture of this size.</summary>
        public static int Size(int width, int height, bool alpha)
        {
            int total = 0, block = alpha ? 16 : 8;
            for (int w = width, h = height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
            {
                total += Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * block;
                if (w == 1 && h == 1) return total;
            }
        }

        /// <summary>Every mip level, largest first, rows in the order given.</summary>
        public static byte[] Encode(byte[] rgba, int width, int height, bool alpha)
        {
            if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
                throw new ArgumentException("livery pixels do not match their size");
            var packed = new byte[Size(width, height, alpha)];
            var r = new int[16]; var g = new int[16]; var b = new int[16]; var a = new int[16];
            byte[] level = rgba;
            int offset = 0;
            for (int w = width, h = height; ; )
            {
                for (int by = 0; by < h; by += 4)
                    for (int bx = 0; bx < w; bx += 4)
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            int x = Math.Min(bx + (i & 3), w - 1), y = Math.Min(by + (i >> 2), h - 1), p = (y * w + x) * 4;
                            r[i] = level[p]; g[i] = level[p + 1]; b[i] = level[p + 2]; a[i] = level[p + 3];
                        }
                        if (alpha) { AlphaBlock(a, packed, offset); offset += 8; }
                        ColourBlock(r, g, b, packed, offset);
                        offset += 8;
                    }
                if (w == 1 && h == 1) return packed;
                level = Halve(level, w, h, out int nw, out int nh);
                w = nw; h = nh;
            }
        }

        private static byte[] Halve(byte[] level, int w, int h, out int nw, out int nh)
        {
            nw = Math.Max(1, w / 2); nh = Math.Max(1, h / 2);
            var half = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
            {
                int y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1);
                    int p00 = (y0 * w + x0) * 4, p01 = (y0 * w + x1) * 4, p10 = (y1 * w + x0) * 4, p11 = (y1 * w + x1) * 4, o = (y * nw + x) * 4;
                    for (int c = 0; c < 4; c++)
                        half[o + c] = (byte)((level[p00 + c] + level[p01 + c] + level[p10 + c] + level[p11 + c] + 2) >> 2);
                }
            }
            return half;
        }

        private static void ColourBlock(int[] r, int[] g, int[] b, byte[] packed, int o)
        {
            int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            for (int i = 0; i < 16; i++)
            {
                if (r[i] < minR) minR = r[i]; if (r[i] > maxR) maxR = r[i];
                if (g[i] < minG) minG = g[i]; if (g[i] > maxG) maxG = g[i];
                if (b[i] < minB) minB = b[i]; if (b[i] > maxB) maxB = b[i];
            }
            // The colour box has four diagonals; red and green each run with blue or against it.
            int midR = (minR + maxR) >> 1, midG = (minG + maxG) >> 1, midB = (minB + maxB) >> 1, withR = 0, withG = 0;
            for (int i = 0; i < 16; i++)
            {
                withR += (r[i] - midR) * (b[i] - midB);
                withG += (g[i] - midG) * (b[i] - midB);
            }
            if (withR < 0) { int t = minR; minR = maxR; maxR = t; }
            if (withG < 0) { int t = minG; minG = maxG; maxG = t; }

            int c0 = (To5(maxR) << 11) | (To6(maxG) << 5) | To5(maxB);
            int c1 = (To5(minR) << 11) | (To6(minG) << 5) | To5(minB);
            if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
            packed[o] = (byte)c0; packed[o + 1] = (byte)(c0 >> 8); packed[o + 2] = (byte)c1; packed[o + 3] = (byte)(c1 >> 8);
            uint indices = 0;
            if (c0 != c1)                                          // equal endpoints: every texel is index 0
            {
                int r0 = Expand5(c0 >> 11), g0 = Expand6(c0 >> 5 & 63), b0 = Expand5(c0 & 31);
                int r1 = Expand5(c1 >> 11), g1 = Expand6(c1 >> 5 & 63), b1 = Expand5(c1 & 31);
                int r2 = (2 * r0 + r1) / 3, g2 = (2 * g0 + g1) / 3, b2 = (2 * b0 + b1) / 3;
                int r3 = (r0 + 2 * r1) / 3, g3 = (g0 + 2 * g1) / 3, b3 = (b0 + 2 * b1) / 3;
                for (int i = 15; i >= 0; i--)
                {
                    int d0 = Far(r[i] - r0, g[i] - g0, b[i] - b0), d1 = Far(r[i] - r1, g[i] - g1, b[i] - b1);
                    int d2 = Far(r[i] - r2, g[i] - g2, b[i] - b2), d3 = Far(r[i] - r3, g[i] - g3, b[i] - b3);
                    uint pick = d0 <= d1 && d0 <= d2 && d0 <= d3 ? 0u : d1 <= d2 && d1 <= d3 ? 1u : d2 <= d3 ? 2u : 3u;
                    indices = indices << 2 | pick;
                }
            }
            packed[o + 4] = (byte)indices; packed[o + 5] = (byte)(indices >> 8);
            packed[o + 6] = (byte)(indices >> 16); packed[o + 7] = (byte)(indices >> 24);
        }

        private static void AlphaBlock(int[] a, byte[] packed, int o)
        {
            int min = 255, max = 0;
            for (int i = 0; i < 16; i++) { if (a[i] < min) min = a[i]; if (a[i] > max) max = a[i]; }
            packed[o] = (byte)max; packed[o + 1] = (byte)min;
            ulong indices = 0;
            if (max != min)
                for (int i = 15; i >= 0; i--)
                {
                    // Eight steps from max (code 0) down to min (code 1); codes 2..7 are the six between.
                    int step = ((max - a[i]) * 7 + (max - min) / 2) / (max - min);
                    indices = indices << 3 | (uint)(step == 0 ? 0 : step == 7 ? 1 : step + 1);
                }
            for (int k = 0; k < 6; k++) packed[o + 2 + k] = (byte)(indices >> (8 * k));
        }

        private static int To5(int v) => (v * 31 + 127) / 255;
        private static int To6(int v) => (v * 63 + 127) / 255;
        private static int Expand5(int v) => v << 3 | v >> 2;
        private static int Expand6(int v) => v << 2 | v >> 4;
        private static int Far(int dr, int dg, int db) => dr * dr + dg * dg + db * db;
    }
}
