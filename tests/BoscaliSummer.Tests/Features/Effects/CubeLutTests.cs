using System.Globalization;
using System.Text;
using BoscaliSummer.Modules.Effects.Domain;
using BoscaliSummer.Tests;

namespace BoscaliSummer.Tests.Features.Effects;

internal static class CubeLutTests
{
    public static void Run()
    {
        // A 3^3 LUT that swaps red and blue, with the header noise real files carry.
        var text = new StringBuilder("# made by a test\r\nTITLE \"swap\"\r\nDOMAIN_MIN 0 0 0\r\nLUT_3D_SIZE 3\r\n\r\n");
        for (int b = 0; b < 3; b++)
            for (int g = 0; g < 3; g++)
                for (int r = 0; r < 3; r++)
                    text.Append((b / 2f).ToString(CultureInfo.InvariantCulture)).Append(' ')
                        .Append((g / 2f).ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append((r / 2f).ToString(CultureInfo.InvariantCulture)).Append("\r\n");

        float[] cube = CubeLut.Parse(text.ToString(), out int size);
        TestAssert.That(cube != null && size == 3 && cube.Length == 81, "a 3^3 cube parses past comments and keywords");

        // Resampled to 5: pixel (r=4, g=1, b=0) sits in slice 0, row 1, and must read (b, g, r) = (0, 0.25, 1).
        byte[] strip = CubeLut.Bake(cube, 3, 5);
        TestAssert.That(strip.Length == 5 * 5 * 5 * 4, "strip is size*size by size RGBA");
        int at = ((1 * 25) + (0 * 5) + 4) * 4;
        TestAssert.That(strip[at] == 0 && strip[at + 1] == 64 && strip[at + 2] == 255 && strip[at + 3] == 255,
            "red runs along x, green up y, blue picks the slice, values interpolate");

        TestAssert.That(CubeLut.Parse("LUT_3D_SIZE 3\n0 0 0\n", out _) == null, "a truncated cube is refused");
        TestAssert.That(CubeLut.Parse("LUT_1D_SIZE 16\n0 0 0\n", out _) == null, "a 1D LUT is refused");
        TestAssert.That(CubeLut.Parse("LUT_3D_SIZE 900\n", out _) == null, "an oversized cube is refused");
    }
}
