using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Land-cover masks read from the game's own terrain splatmaps (forest = the lush map,
    /// farmland, grassland, bare rock), one byte per texel, row 0 at the south edge.
    /// </summary>
    internal sealed class LandCoverMasks
    {
        public int Size;
        public byte[] Forest, Fields, Grass, Rock;

        public bool Valid =>
            Size > 0 && Fits(Forest) && Fits(Fields) && Fits(Grass) && Fits(Rock);

        private bool Fits(byte[] mask) => mask != null && mask.Length == Size * Size;
    }

    /// <summary>
    /// Paints land cover into the relief's intel texture so forests, farmland, rock and
    /// deserts read at a glance. The intel brightness stays under the colour, so roads,
    /// ridges and towns survive; sea is never touched. Land the game gives no cover at all
    /// (no grass, crops, forest or rock) is arid ground and reads as sand.
    /// </summary>
    internal static class LandCover
    {
        // Muted map colours, kept away from the faction red and blue the control wash adds on top.
        internal static readonly byte[] ForestColour = { 38, 96, 54 };
        internal static readonly byte[] FieldsColour = { 132, 120, 62 };
        internal static readonly byte[] GrassColour = { 72, 104, 80 };
        internal static readonly byte[] RockColour = { 112, 104, 98 };
        internal static readonly byte[] AridColour = { 176, 142, 88 };

        /// <summary>How far a fully covered texel moves from the intel colour toward its cover.</summary>
        internal const float Strength = 0.6f;

        /// <summary>
        /// Tints <paramref name="rgb"/> (RGB24 or RGBA32 by <paramref name="stride"/>, row 0 at the south edge) in
        /// place. <paramref name="land"/> is the relief's dry-land sample grid,
        /// <paramref name="samples"/> on a side, over the same extent as the texture.
        /// </summary>
        public static void Tint(byte[] rgb, int width, int height, int stride, LandCoverMasks masks, bool[] land, int samples)
        {
            if (rgb == null || width < 2 || height < 2 || stride < 3 || rgb.Length < width * height * stride) return;
            if (masks == null || !masks.Valid || land == null || samples < 2 || land.Length != samples * samples) return;

            int size = masks.Size;
            // ponytail: one pass over the whole texture on the first map open (~4M texels at 2048).
            // Move it to a worker if that first open ever shows in the frame-spike log.
            for (int y = 0; y < height; y++)
            {
                int row = y * size / height * size;
                int gz = (int)((long)y * (samples - 1) / (height - 1));
                for (int x = 0; x < width; x++)
                {
                    int gx = (int)((long)x * (samples - 1) / (width - 1));
                    if (!land[gz * samples + gx]) continue;

                    int m = row + x * size / width;
                    int covered = masks.Forest[m] + masks.Fields[m] + masks.Grass[m] + masks.Rock[m];
                    float forest = masks.Forest[m] * (1.4f / 255f);
                    float fields = masks.Fields[m] * (1.2f / 255f);
                    float grass = masks.Grass[m] * (0.5f / 255f);
                    float rock = masks.Rock[m] * (1f / 255f);
                    float arid = Math.Max(0f, 1f - covered * (1.5f / 255f)) * 1.2f;
                    float total = forest + fields + grass + rock + arid;
                    if (total < 0.05f) continue;

                    int p = (y * width + x) * stride;
                    // The cover colour is lit by the texel's own brightness, so the intel detail stays.
                    float light = 0.45f + 1.1f * (rgb[p] * 0.3f + rgb[p + 1] * 0.59f + rgb[p + 2] * 0.11f) / 255f;
                    float weight = Strength * Math.Min(1f, total);
                    for (int c = 0; c < 3; c++)
                    {
                        float cover = (forest * ForestColour[c] + fields * FieldsColour[c] + grass * GrassColour[c] +
                                       rock * RockColour[c] + arid * AridColour[c]) / total;
                        float source = rgb[p + c];
                        rgb[p + c] = (byte)Math.Clamp(source + (cover * light - source) * weight, 0f, 255f);
                    }
                }
            }
        }
    }
}
