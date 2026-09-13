using System;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static class SettingsChoices
    {
        private static readonly string[] Backgrounds = { "PLAIN", "GRID", "CHECKER", "HEXAGON", "CARBON", "RADAR", "CUSTOM" };

        private static int Background(bool grid, bool checker, bool image, int preset)
        {
            if ((grid ? 1 : 0) + (checker ? 1 : 0) + (image ? 1 : 0) > 1) return -1;
            return image ? 3 + Math.Max(0, Math.Min(3, preset)) : checker ? 2 : grid ? 1 : 0;
        }

        public static string BackgroundName(bool grid, bool checker, bool image, int preset)
        {
            int mode = Background(grid, checker, image, preset);
            return mode < 0 ? "MIXED" : Backgrounds[mode];
        }

        public static int CycleBackground(bool grid, bool checker, bool image, int preset, int direction)
        {
            int current = Background(grid, checker, image, preset);
            if (current < 0) return direction < 0 ? 6 : 0;
            return (current + (direction < 0 ? 6 : 1)) % 7;
        }

        // Inspect dimensions before Unity allocates a decoded image.
        public static bool SupportedImage(byte[] data)
        {
            if (data == null || data.Length < 24 || data.Length > 16 * 1024 * 1024) return false;
            if (data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 &&
                data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10 &&
                data[12] == 73 && data[13] == 72 && data[14] == 68 && data[15] == 82)
                return Dimension(data, 16) && Dimension(data, 20);
            if (data[0] != 255 || data[1] != 216) return false;
            int offset = 2;
            while (offset + 3 < data.Length)
            {
                if (data[offset++] != 255) return false;
                while (offset < data.Length && data[offset] == 255) offset++;
                if (offset >= data.Length) return false;
                int marker = data[offset++];
                if (marker == 217 || marker == 218) return false;
                if (marker == 1 || marker >= 208 && marker <= 215) continue;
                if (offset + 2 > data.Length) return false;
                int length = (data[offset] << 8) | data[offset + 1];
                if (length < 2 || offset + length > data.Length) return false;
                if (marker >= 192 && marker <= 195)
                {
                    if (length < 8) return false;
                    int height = (data[offset + 3] << 8) | data[offset + 4];
                    int width = (data[offset + 5] << 8) | data[offset + 6];
                    return width > 0 && width <= 4096 && height > 0 && height <= 4096;
                }
                offset += length;
            }
            return false;
        }

        private static bool Dimension(byte[] data, int offset)
        {
            if (data[offset] != 0 || data[offset + 1] != 0) return false;
            int size = (data[offset + 2] << 8) | data[offset + 3];
            return size > 0 && size <= 4096;
        }
    }
}
