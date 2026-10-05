namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>The Personnel File's and the aircrew ID card's paperwork (display only): a stable file number from the pilot's identity,
    /// the barcode's bar pattern and the bio excerpt. Nothing here is stored; the same pilot always reads the same.</summary>
    internal static class PersonnelFile
    {
        public const int Bars = 28;

        /// <summary>"PF-0417": four digits from the callsign and name (FNV-1a, so it does not change between runs).</summary>
        public static string Number(string callsign, string name)
        {
            uint h = Hash(callsign, name);
            return "PF-" + (h % 10000u).ToString("0000");
        }

        /// <summary>The barcode as bar widths in 1..3 units (a gap of one unit follows each bar); the same pilot, the same bars.</summary>
        public static int Pattern(string callsign, string name, int[] into)
        {
            if (into == null) return 0;
            uint h = Hash(callsign, name);
            int n = into.Length < Bars ? into.Length : Bars;
            for (int i = 0; i < n; i++)
            {
                h = h * 1664525u + 1013904223u;
                into[i] = 1 + (int)((h >> 24) % 3u);
            }
            return n;
        }

        /// <summary>The bio's first <paramref name="max"/> characters, cut at a word, never with an ellipsis; blank in, blank out.</summary>
        public static string Excerpt(string bio, int max)
        {
            if (string.IsNullOrWhiteSpace(bio) || max <= 0) return "";
            string s = bio.Trim().Replace('\r', ' ').Replace('\n', ' ');
            if (s.Length <= max) return s;
            int cut = s.LastIndexOf(' ', max);
            return (cut > max / 2 ? s.Substring(0, cut) : s.Substring(0, max)).TrimEnd(' ', ',', ';', '-');
        }

        /// <summary>A five-letter ladder cell's fill: 1 for ranks passed, the progress inside the current rank, 0 above.
        /// <paramref name="overall"/> is <c>PilotXp.Fill</c> (rank + progress, in fifths).</summary>
        public static float Cell(float overall, int cell)
        {
            float f = overall * 5f - cell;
            return f <= 0f ? 0f : f >= 1f ? 1f : f;
        }

        private static uint Hash(string a, string b)
        {
            uint h = 2166136261u;
            h = Mix(h, a);
            h = (h ^ 0x1Fu) * 16777619u;
            return Mix(h, b);
        }

        private static uint Mix(uint h, string s)
        {
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++) h = (h ^ char.ToUpperInvariant(s[i])) * 16777619u;
            return h;
        }
    }
}
