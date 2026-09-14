using System;
using System.Globalization;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Engine-free encoding of an occupied shell's measured flat-roof patch at the end of
    /// the spawned defense's networked unique name. Every client rebuilds the same
    /// shell-scaled marker from it without a cosmetic network message.
    /// </summary>
    internal static class GarrisonMarkerInfo
    {
        private const string Token = "$m";

        public static string Append(string name, float minX, float maxX, float minZ, float maxZ)
        {
            return string.Concat(name, Token,
                Format(minX), "_", Format(maxX), "_", Format(minZ), "_", Format(maxZ));
        }

        public static bool TryParse(
            string name,
            out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            if (string.IsNullOrEmpty(name)) return false;
            int index = name.LastIndexOf(Token, StringComparison.Ordinal);
            if (index < 0) return false;
            string[] parts = name.Substring(index + Token.Length).Split('_');
            if (parts.Length != 4) return false;
            return TryRead(parts[0], out minX) && TryRead(parts[1], out maxX) &&
                   TryRead(parts[2], out minZ) && TryRead(parts[3], out maxZ) &&
                   maxX - minX >= 4f && maxZ - minZ >= 4f;
        }

        private static string Format(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private static bool TryRead(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
