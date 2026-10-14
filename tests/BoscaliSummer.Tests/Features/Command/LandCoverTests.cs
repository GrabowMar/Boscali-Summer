using System.Linq;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class LandCoverTests
    {
        public static void Run()
        {
            // A 4 x 2 texture over all-land: forest, farmland, bare (arid) land, rock, column by column.
            const int width = 4, height = 2;
            var land = new[] { true, true, true, true };
            var masks = new LandCoverMasks { Size = 4, Forest = new byte[16], Fields = new byte[16], Grass = new byte[16], Rock = new byte[16] };
            // Texture row y reads mask row y * 4 / 2: rows 0 and 2.
            foreach (int row in new[] { 0, 8 })
            {
                masks.Forest[row] = 255;
                masks.Fields[row + 1] = 255;
                masks.Rock[row + 3] = 255;
            }
            byte[] rgb = Enumerable.Repeat((byte)40, width * height * 3).ToArray();

            LandCover.Tint(rgb, width, height, 3, masks, land, 2);

            int forest = 0, fields = 3, arid = 6;
            TestAssert.That(rgb[forest + 1] > rgb[forest] && rgb[forest + 1] > rgb[forest + 2], "forest reads green");
            TestAssert.That(rgb[fields] > rgb[fields + 2] && rgb[fields + 1] > rgb[fields + 2],
                "farmland reads khaki (red and green over blue)");
            TestAssert.That(rgb[arid] > rgb[arid + 1] && rgb[arid + 1] > rgb[arid + 2] && rgb[arid] > 60,
                "land with no cover reads as sand");

            byte[] sea = Enumerable.Repeat((byte)40, width * height * 3).ToArray();
            LandCover.Tint(sea, width, height, 3, masks, new bool[4], 2);
            TestAssert.That(sea.All(v => v == 40), "sea is never tinted");

            byte[] same = (byte[])rgb.Clone();
            LandCover.Tint(rgb, width, height, 3, new LandCoverMasks { Size = 3 }, land, 2);
            TestAssert.That(rgb.SequenceEqual(same), "broken masks leave the texture untouched");

            byte[] rgba = Enumerable.Repeat((byte)40, width * height * 4).ToArray();
            LandCover.Tint(rgba, width, height, 4, masks, land, 2);
            TestAssert.That(rgba[1] > rgba[0] && rgba[3] == 40, "RGBA texels tint colour and keep alpha");
        }
    }
}
