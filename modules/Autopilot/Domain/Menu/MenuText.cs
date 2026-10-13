using System.Globalization;
using System.Text;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>Copy helpers shared by the catalog and the hint bar. All menu copy is UPPER CASE.</summary>
    internal static class MenuText
    {
        public const string Dot = " · ";
        public const string Times = "×";

        /// <summary>Upper case, cut to <paramref name="max"/> characters with an ellipsis. Null is empty.</summary>
        public static string Upper(string text, int max = 22)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string upper = text.Trim().ToUpperInvariant();
            return upper.Length <= max ? upper : upper.Substring(0, max - 1) + "…";
        }

        /// <summary>The first word when it fits <paramref name="max"/>, else the text cut hard to <paramref name="max"/> (no ellipsis: ring badges).</summary>
        public static string FirstWord(string text, int max)
        {
            string upper = Upper(text, 64);
            int space = upper.IndexOf(' ');
            string word = space > 0 ? upper.Substring(0, space) : upper;
            return word.Length <= max ? word : word.Substring(0, max);
        }

        /// <summary>1000 -> 1K, 1500 -> 1.5K, 300 -> 300.</summary>
        public static string Ammo(int n)
        {
            if (n < 1000) return n.ToString(CultureInfo.InvariantCulture);
            float k = n / 1000f;
            return (k == (int)k ? ((int)k).ToString(CultureInfo.InvariantCulture) : k.ToString("0.#", CultureInfo.InvariantCulture)) + "K";
        }

        /// <summary>A stable id fragment: lower-case letters and digits, everything else becomes a dash.</summary>
        public static string Slug(string text)
        {
            var sb = new StringBuilder(text?.Length ?? 0);
            bool dash = false;
            foreach (char c in text ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); dash = false; }
                else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            }
            return sb.ToString().TrimEnd('-');
        }

        /// <summary>Kilometres with one decimal: 4.2 KM. Metres under a kilometre: 800 M.</summary>
        public static string Distance(float km) =>
            km < 1f ? ((int)(km * 1000f / 10f) * 10).ToString(CultureInfo.InvariantCulture) + " M"
                    : km.ToString("0.#", CultureInfo.InvariantCulture) + " KM";

        public static string Plural(int n, string one, string many) => n + " " + (n == 1 ? one : many);
    }
}
