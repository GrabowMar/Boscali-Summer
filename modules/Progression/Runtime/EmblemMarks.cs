using System;
using System.IO;
using System.IO.Compression;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>
    /// The insignia's centre marks: drawings traced into signed distances by tools/Build-EmblemArt.py and
    /// embedded as emblem-art.bin. A mark is four planes (the whole figure, its gold, its red, its line work),
    /// so it takes the badge's own tinctures and stays sharp at any size. Engine-free; the pure suite draws it.
    /// A mark is unpacked the first time it is drawn and the oldest is dropped past <see cref="Held"/>.
    /// Order in the file is the stored encoding, as in <see cref="EmblemDesign.ChargeNames"/>: append only.
    /// </summary>
    internal static class EmblemMarks
    {
        /// <summary>What a sample outside every plane reads: well clear of any edge.</summary>
        public const float Far = 1e6f;

        private const int Header = 7, Held = 12;
        private static readonly object Gate = new object();
        private static byte[] packed;
        private static byte[][] planes;
        private static int[] order;
        private static int cells, plane, count = -1, held;
        private static float unit;

        /// <summary>How many marks the embedded art carries; zero when it is missing.</summary>
        public static int Count
        {
            get
            {
                if (count < 0) Open();
                return count;
            }
        }

        /// <summary>
        /// Signed distances at a point of mark <paramref name="mark"/>'s -1..1 box, negative inside:
        /// the figure, its gold areas, its red areas and its line work. Gold and red each include the lines.
        /// </summary>
        public static void Sample(int mark, float x, float y, out float figure, out float gold, out float red, out float line)
        {
            figure = gold = red = line = Far;
            byte[] art = Planes(mark);
            if (art == null) return;
            float u = (x * 0.5f + 0.5f) * cells - 0.5f, v = (y * 0.5f + 0.5f) * cells - 0.5f;
            if (u < 0f || v < 0f || u > cells - 1 || v > cells - 1) return;
            int x0 = (int)u, y0 = (int)v, x1 = Math.Min(x0 + 1, cells - 1), y1 = Math.Min(y0 + 1, cells - 1);
            float fx = u - x0, fy = v - y0;
            int a = y0 * cells + x0, b = y0 * cells + x1, c = y1 * cells + x0, d = y1 * cells + x1;
            float wa = (1f - fx) * (1f - fy), wb = fx * (1f - fy), wc = (1f - fx) * fy, wd = fx * fy;
            figure = (128f - (art[a] * wa + art[b] * wb + art[c] * wc + art[d] * wd)) * unit;
            if (figure > unit * 32f) return;
            a += plane; b += plane; c += plane; d += plane;
            gold = (128f - (art[a] * wa + art[b] * wb + art[c] * wc + art[d] * wd)) * unit;
            a += plane; b += plane; c += plane; d += plane;
            red = (128f - (art[a] * wa + art[b] * wb + art[c] * wc + art[d] * wd)) * unit;
            a += plane; b += plane; c += plane; d += plane;
            line = (128f - (art[a] * wa + art[b] * wb + art[c] * wc + art[d] * wd)) * unit;
        }

        private static byte[] Planes(int mark)
        {
            if (count < 0) Open();
            if (mark < 0 || mark >= count) return null;
            byte[] ready = planes[mark];
            if (ready != null) return ready;
            // A badge is drawn on the main thread and on a paint worker; one of them unpacks, the other waits.
            lock (Gate)
            {
                if (planes[mark] != null) return planes[mark];
                try
                {
                    int at = BitConverter.ToInt32(packed, Header + mark * 8), length = BitConverter.ToInt32(packed, Header + mark * 8 + 4);
                    var art = new byte[plane * 4];
                    using (var source = new MemoryStream(packed, at, length, false))
                    using (var inflate = new DeflateStream(source, CompressionMode.Decompress))
                    {
                        int read = 0, got;
                        while (read < art.Length && (got = inflate.Read(art, read, art.Length - read)) > 0) read += got;
                        if (read != art.Length) return null;
                    }
                    if (held == Held)
                    {
                        planes[order[0]] = null;
                        Array.Copy(order, 1, order, 0, Held - 1);
                        held--;
                    }
                    order[held++] = mark;
                    planes[mark] = art;
                    return art;
                }
                catch (Exception) { return null; }
            }
        }

        /// <summary>Finds the art embedded next to this type, whichever assembly that is. Missing art draws nothing.</summary>
        private static void Open()
        {
            lock (Gate)
            {
                if (count >= 0) return;
                int found = 0;
                try
                {
                    System.Reflection.Assembly home = typeof(EmblemMarks).Assembly;
                    foreach (string name in home.GetManifestResourceNames())
                    {
                        if (!name.EndsWith("emblem-art.bin", StringComparison.Ordinal)) continue;
                        using (Stream stream = home.GetManifestResourceStream(name))
                        using (var buffer = new MemoryStream())
                        {
                            stream.CopyTo(buffer);
                            packed = buffer.ToArray();
                        }
                        break;
                    }
                    if (packed != null && packed.Length > Header && packed[0] == 'E' && packed[1] == 'A' && packed[2] == 'R' && packed[3] == '2')
                    {
                        cells = packed[5] | packed[6] << 8;
                        if (cells >= 32 && packed.Length >= Header + packed[4] * 8)
                        {
                            found = packed[4];
                            plane = cells * cells;
                            unit = 2f / (cells * 16f);      // one byte step: a sixteenth of a cell, a cell being 2 / cells
                            planes = new byte[found][];
                            order = new int[Held];
                        }
                    }
                }
                catch (Exception) { found = 0; }
                count = found;
            }
        }
    }
}
