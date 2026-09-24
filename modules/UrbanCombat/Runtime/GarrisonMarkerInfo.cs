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
        private const string RoofSegment = "Roof:";
        private const string TierPrefix = ":t";

        /// <summary>
        /// Zone nests carry their urban tier as a `:tN` segment just before the marker
        /// suffix (`...Roof:Zone:2:1:t2$m...`). Fortify/seize/assault nests carry no tier.
        /// </summary>
        public static bool TryReadTier(string name, out int tier)
        {
            tier = 0;
            if (string.IsNullOrEmpty(name)) return false;
            int marker = name.IndexOf(Token, StringComparison.Ordinal);
            if (marker < 0) return false;
            int head = name.LastIndexOf(TierPrefix, marker, StringComparison.Ordinal);
            if (head < 0) return false;
            int digits = marker - (head + TierPrefix.Length);
            if (digits < 1 || digits > 2) return false;
            for (int i = head + TierPrefix.Length; i < marker; i++)
                if (name[i] < '0' || name[i] > '9') return false;
            tier = 0;
            for (int i = head + TierPrefix.Length; i < marker; i++)
                tier = tier * 10 + (name[i] - '0');
            return tier >= 0 && tier <= 9;
        }

        /// <summary>
        /// Reads the sanitized zone name from a zone nest (`...Roof:Zone:gen:slot...`).
        /// Fortify/seize/assault nests (`Roof:Assault:...`, `Roof:Seize:...`) have no zone
        /// membership and return false.
        /// </summary>
        public static bool TryReadZone(string name, string prefix, out string zone)
        {
            zone = null;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(prefix)) return false;
            string head = prefix + RoofSegment;
            if (!name.StartsWith(head, StringComparison.Ordinal)) return false;
            int start = head.Length;
            int end = name.IndexOf(':', start);
            if (end < 0)
            {
                end = name.IndexOf(Token, start, StringComparison.Ordinal);
                if (end < 0) end = name.Length;
            }
            if (end <= start) return false;
            zone = name.Substring(start, end - start);
            return zone.Length > 0 && !zone.Equals("Assault", StringComparison.Ordinal) &&
                !zone.Equals("Seize", StringComparison.Ordinal);
        }

        /// <summary>
        /// Sanitizes an airbase name for the nest-name zone segment. Colons would split
        /// the segment parse, so they and spaces become underscores.
        /// </summary>
        public static string SanitizeZone(string value) =>
            (value ?? "Airbase").Replace(':', '_').Replace(' ', '_');

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
