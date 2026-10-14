using System.Globalization;

namespace BoscaliSummer.Modules.Pmc.Runtime
{
    /// <summary>Reads the "r,g,b[,a]" colour setting, always with '.' decimals whatever the Windows locale.</summary>
    internal static class PmcColor
    {
        public static bool TryParse(string text, out float r, out float g, out float b, out float a)
        {
            r = g = b = 0f;
            a = 1f;
            string[] parts = (text ?? "").Split(',');
            if (parts.Length < 3 || parts.Length > 4) return false;
            return Read(parts[0], out r) && Read(parts[1], out g) && Read(parts[2], out b) && (parts.Length == 3 || Read(parts[3], out a));
        }

        private static bool Read(string part, out float value) =>
            float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
