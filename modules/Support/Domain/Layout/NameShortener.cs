using System.Text;

namespace BoscaliSummer.Features.Support.Domain.Layout
{
    /// <summary>
    /// Place-name cleaning. A city-set token (CITY, BUILDING, BUILDINGS, SET) or a pure
    /// number is not a name; what remains is the town, else the nearest airbase's outskirts,
    /// else the grid. Word-boundary shortening for board labels arrives with the layout helpers.
    /// </summary>
    internal static class PlaceNames
    {
        public static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string text = raw.Replace("(Clone)", "").Replace('_', ' ').Replace('-', ' ');
            int paren = text.IndexOf('(');
            if (paren >= 0) text = text.Substring(0, paren);
            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Trim().ToUpperInvariant();
        }

        /// <summary>The name a town should carry. <paramref name="nearestAirbaseName"/> is already cleaned.</summary>
        public static string Town(string raw, string nearestAirbaseName, string gridRef)
        {
            string kept = KeepRealWords(Clean(raw));
            if (kept.Length > 0) return kept;
            string nearest = Clean(nearestAirbaseName);
            if (nearest.Length > 0) return nearest + " OUTSKIRTS";
            return "TOWN " + (gridRef ?? "");
        }

        private static string KeepRealWords(string cleaned)
        {
            if (cleaned.Length == 0) return "";
            var builder = new StringBuilder(cleaned.Length);
            int index = 0;
            while (index < cleaned.Length)
            {
                while (index < cleaned.Length && cleaned[index] == ' ') index++;
                int start = index;
                while (index < cleaned.Length && cleaned[index] != ' ') index++;
                if (start == index) break;
                if (Filler(cleaned, start, index - start)) continue;
                if (builder.Length > 0) builder.Append(' ');
                builder.Append(cleaned, start, index - start);
            }
            return builder.ToString();
        }

        private static bool Filler(string text, int start, int length)
        {
            if (length == 0) return true;
            bool digits = true;
            for (int i = 0; i < length; i++)
            {
                char c = text[start + i];
                if (c < '0' || c > '9')
                {
                    digits = false;
                    break;
                }
            }
            if (digits) return true;
            return Same(text, start, length, "CITY")
                || Same(text, start, length, "BUILDING")
                || Same(text, start, length, "BUILDINGS")
                || Same(text, start, length, "SET");
        }

        private static bool Same(string text, int start, int length, string word)
        {
            if (length != word.Length) return false;
            for (int i = 0; i < length; i++)
            {
                if (text[start + i] != word[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Fit a name into <paramref name="max"/> characters. The abbreviation that saves the most
        /// is applied, then the name is measured again. Trailing words drop only after that, and a
        /// word is cut only when it is the last one left.
        /// </summary>
        public static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || max <= 0) return "";
            string[] words = new string[16];
            int count = Split(text, words);
            if (count == 0) return "";
            while (Measure(words, count) > max)
            {
                int at = -1;
                int span = 1;
                int best = 0;
                for (int i = 0; i < count; i++)
                {
                    int saving = Saving(words, count, i, out int taken);
                    if (saving > best)
                    {
                        best = saving;
                        at = i;
                        span = taken;
                    }
                }
                if (at < 0) break;
                Apply(words, ref count, at, span);
            }
            while (count > 1 && Measure(words, count) > max) count--;
            if (Measure(words, count) <= max) return Join(words, count);
            string word = words[0];
            if (max <= 1) return "…";
            return word.Substring(0, max - 1) + "…";
        }

        private static int Split(string text, string[] words)
        {
            int count = 0;
            int i = 0;
            while (i < text.Length && count < words.Length)
            {
                while (i < text.Length && text[i] == ' ') i++;
                int start = i;
                while (i < text.Length && text[i] != ' ') i++;
                if (start == i) break;
                words[count++] = text.Substring(start, i - start).ToUpperInvariant();
            }
            return count;
        }

        private static int Measure(string[] words, int count)
        {
            int length = 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) length++;
                length += words[i].Length;
            }
            return length;
        }

        private static string Join(string[] words, int count)
        {
            if (count <= 0) return "";
            if (count == 1) return words[0];
            var builder = new StringBuilder(Measure(words, count));
            for (int i = 0; i < count; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append(words[i]);
            }
            return builder.ToString();
        }

        private static int Saving(string[] words, int count, int index, out int span)
        {
            span = 1;
            if (index + 1 < count && words[index] == "AIR" && words[index + 1] == "DEFENCE")
            {
                span = 2;
                return "AIR DEFENCE".Length - "AD".Length;
            }
            string word = words[index];
            string next = Abbreviate(word);
            if (next == null || next.Length >= word.Length) return 0;
            return word.Length - next.Length;
        }

        private static void Apply(string[] words, ref int count, int index, int span)
        {
            words[index] = span == 2 ? "AD" : Abbreviate(words[index]);
            if (span == 2)
            {
                for (int i = index + 1; i < count - 1; i++) words[i] = words[i + 1];
                count--;
            }
        }

        private static string Abbreviate(string word)
        {
            switch (word)
            {
                case "AIRBASE": return "AB";
                case "AIRFIELD": return "AF";
                case "AIRSTRIP": return "STRIP";
                case "STRIP": return "STR";
                case "NAVAL": return "NAV";
                case "HIGHWAY": return "HWY";
                case "INTERNATIONAL": return "INTL";
                case "NORTH": return "N";
                case "SOUTH": return "S";
                case "EAST": return "E";
                case "WEST": return "W";
                case "MOUNT": return "MT";
                case "ENRICHMENT": return "ENR";
                case "INDUSTRIAL": return "IND";
                case "STATION": return "STN";
                case "JUNCTION": return "JCT";
                default: return null;
            }
        }
    }
}
