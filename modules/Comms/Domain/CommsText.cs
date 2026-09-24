using System;
using System.Globalization;
using System.Text;

namespace BoscaliSummer.Features.Comms.Domain
{
    /// <summary>
    /// Every string a player can put in front of another player passes through here: map
    /// labels, poll questions and options, and the names the host stamps on a post.
    ///
    /// <para>Mod labels are TextMeshPro with rich text on, so an angle bracket is markup, not a
    /// character: "&lt;size=400&gt;" in a map label would blank the screen for everyone who
    /// sees it. Brackets and control characters are dropped, whitespace collapses, and the
    /// length is capped before the host ever stores the text.</para>
    /// </summary>
    internal static class CommsText
    {
        public const int MaxLabel = 18;
        public const int MaxQuestion = 40;
        public const int MaxOption = 14;
        public const int MaxName = 20;

        /// <summary>Upper-cased, markup-free, whitespace-collapsed and at most <paramref name="max"/> long.</summary>
        public static string Clean(string text, int max, bool upper = true)
        {
            if (string.IsNullOrEmpty(text) || max <= 0) return "";

            var builder = new StringBuilder(Math.Min(text.Length, max));
            bool pendingSpace = false;
            for (int i = 0; i < text.Length && builder.Length < max; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (c == '<' || c == '>' || char.IsControl(c) || char.IsSurrogate(c)) continue;

                if (pendingSpace)
                {
                    pendingSpace = false;
                    builder.Append(' ');
                    if (builder.Length >= max) break;
                }
                builder.Append(upper ? char.ToUpperInvariant(c) : c);
            }
            return builder.ToString().TrimEnd(' ');
        }

        /// <summary>A display name that is never empty, so a feed line always says who.</summary>
        public static string Name(string name)
        {
            string clean = Clean(name, MaxName);
            return clean.Length > 0 ? clean : "PILOT";
        }

        /// <summary>
        /// Split one "A, B, C" field into poll options. Empty entries and duplicates are dropped,
        /// so "YES,,YES, NO" is a two-option poll rather than a rejected one.
        /// </summary>
        public static string[] SplitOptions(string text, int maxOptions)
        {
            if (string.IsNullOrEmpty(text) || maxOptions <= 0) return Array.Empty<string>();
            string[] raw = text.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new string[Math.Min(raw.Length, maxOptions)];
            int count = 0;
            for (int i = 0; i < raw.Length && count < result.Length; i++)
            {
                string option = Clean(raw[i], MaxOption);
                if (option.Length == 0 || Contains(result, count, option)) continue;
                result[count++] = option;
            }
            if (count == result.Length) return result;
            var trimmed = new string[count];
            Array.Copy(result, trimmed, count);
            return trimmed;
        }

        /// <summary>Kilometres east / north of the map centre, the grid the other OPS screens print.</summary>
        public static string Grid(float x, float z) => Km(x) + " / " + Km(z);

        /// <summary>A distance in the unit the player flies with.</summary>
        public static string Distance(float metres, bool metric)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres)) return "—";
            if (metric)
            {
                return metres < 1000f
                    ? Math.Round(metres).ToString("0", CultureInfo.InvariantCulture) + " M"
                    : (metres / 1000f).ToString(metres < 100000f ? "0.0" : "0", CultureInfo.InvariantCulture) + " KM";
            }
            float nm = metres / 1852f;
            return nm.ToString(nm < 100f ? "0.0" : "0", CultureInfo.InvariantCulture) + " NM";
        }

        /// <summary>True bearing from one ground point to another, 0–359, north up.</summary>
        public static int Bearing(float fromX, float fromZ, float toX, float toZ)
        {
            double degrees = Math.Atan2(toX - fromX, toZ - fromZ) * 180.0 / Math.PI;
            int rounded = (int)Math.Round(degrees);
            return ((rounded % 360) + 360) % 360;
        }

        /// <summary>"42s", "3m 05s": a countdown that fits in a chip.</summary>
        public static string Countdown(float seconds)
        {
            int total = seconds <= 0f || float.IsNaN(seconds) ? 0 : (int)Math.Ceiling(seconds);
            if (total < 60) return total.ToString(CultureInfo.InvariantCulture) + "s";
            return (total / 60).ToString(CultureInfo.InvariantCulture) + "m " +
                   (total % 60).ToString("00", CultureInfo.InvariantCulture) + "s";
        }

        private static string Km(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres)) return "—";
            return (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static bool Contains(string[] values, int count, string value)
        {
            for (int i = 0; i < count; i++)
                if (string.Equals(values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
