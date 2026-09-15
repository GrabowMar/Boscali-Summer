using System;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// Clusters the sector field into maximal uniform rectangles with a binary partition
    /// tree: split the longer axis, stop at a uniform block. The texture bake then touches
    /// one rectangle per cluster instead of one per 1 km cell, so the quiet rear collapses
    /// to a few blocks and only the front stays fine-grained.
    ///
    /// <para>Keys are per-cell bytes — this grid packs <c>state &lt;&lt; 2 | band</c> and sets
    /// <see cref="NonMergeable"/> on contested cells, which must each keep their own cluster
    /// so the hatch split keeps following the cell's control value. Leaves are disjoint and
    /// cover every cell; there is at most one per cell.</para>
    /// </summary>
    internal static class SectorClusterTree
    {
        public const byte NonMergeable = 0x80;

        public struct Cluster
        {
            public int X, Y, Width, Height;
            public byte Key;
        }

        /// <summary>
        /// Writes the clustering into <paramref name="destination"/> and returns the cluster
        /// count. Callers size the destination at one entry per cell.
        /// </summary>
        public static int Build(byte[] keys, int resX, int resY, Cluster[] destination)
        {
            if (keys == null || destination == null || resX < 1 || resY < 1 ||
                keys.Length < resX * resY) return 0;
            int written = 0;
            Split(keys, resX, 0, 0, resX, resY, destination, ref written);
            return written;
        }

        private static void Split(byte[] keys, int resX, int x, int y, int width, int height,
            Cluster[] destination, ref int written)
        {
            if (written >= destination.Length || width < 1 || height < 1) return;

            byte first = keys[y * resX + x];
            if (Uniform(keys, resX, x, y, width, height, first) &&
                (width == 1 && height == 1 || (first & NonMergeable) == 0))
            {
                destination[written++] = new Cluster { X = x, Y = y, Width = width, Height = height, Key = first };
                return;
            }

            if (width >= height)
            {
                int half = width / 2;
                Split(keys, resX, x, y, half, height, destination, ref written);
                Split(keys, resX, x + half, y, width - half, height, destination, ref written);
            }
            else
            {
                int half = height / 2;
                Split(keys, resX, x, y, width, half, destination, ref written);
                Split(keys, resX, x, y + half, width, height - half, destination, ref written);
            }
        }

        private static bool Uniform(byte[] keys, int resX, int x, int y, int width, int height, byte key)
        {
            for (int r = y; r < y + height; r++)
            {
                int row = r * resX;
                for (int c = x; c < x + width; c++)
                    if (keys[row + c] != key) return false;
            }
            return true;
        }
    }
}
