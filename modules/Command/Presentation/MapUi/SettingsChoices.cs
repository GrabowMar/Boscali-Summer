
using BoscaliSummer.Core.Util;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static class SettingsChoices
    {
        // Legacy procedural presets intentionally resolve to matte. Custom keeps its persisted id (3).
        public static string BackgroundName(bool grid, bool checker, bool image, int preset) =>
            image && preset == 3 ? "CUSTOM" : "MATTE";

        public static int CycleBackground(bool grid, bool checker, bool image, int preset, int direction) =>
            image && preset == 3 ? 0 : 1;

        // Inspect dimensions before Unity allocates a decoded image.
        public static bool SupportedImage(byte[] data)
        {
            if (data == null || data.Length < 24 || data.Length > 16 * 1024 * 1024) return false;
            if (PngSprites.IsSupported(data, 4096, out _, out _)) return true;
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
    }
}
