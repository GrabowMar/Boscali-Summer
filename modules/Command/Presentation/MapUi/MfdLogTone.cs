namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Reads the theatre out of a game message line so the event stream is not a wall of
    /// identical grey text. Classification is deliberately keyword-based and one-way: the
    /// game owns the wording, this only paints what is already there, and an unrecognised
    /// line stays neutral rather than being forced into a meaning it may not have.
    ///
    /// Pure: no Unity or theme dependency. Body text stays warm white; colours apply only
    /// to the report category, so native rich-text spans cannot defeat contrast.
    /// </summary>
    internal static class MfdLogTone
    {
        internal enum Kind
        {
            Neutral,
            Ready,
            Caution,
            Danger,
        }

        public const string NeutralHex = "F3F0E8";
        public const string ReadyHex = "8ED9BE";
        public const string CautionHex = "F5BA56";
        public const string DangerHex = "F39A96";

        private static readonly string[] DangerWords =
        {
            "destroyed", "demolished", "shot down", "downed", "sunk", "crashed",
            "killed", "levelled", "leveled", "wrecked", "nuclear",
        };

        private static readonly string[] ReadyWords =
        {
            "captured", "secured", "recaptured", "landed", "repaired", "resupplied",
            "defended", "escaped", "recovered", "deployed",
        };

        private static readonly string[] CautionWords =
        {
            "intercepted", "engaged", "damaged", "launched", "fired", "jammed",
            "detected", "spotted", "incoming", "hit ", " hit", "strafed",
        };

        public static Kind Classify(string line)
        {
            if (string.IsNullOrEmpty(line)) return Kind.Neutral;

            if (Contains(line, DangerWords)) return Kind.Danger;
            if (Contains(line, ReadyWords)) return Kind.Ready;
            if (Contains(line, CautionWords)) return Kind.Caution;
            return Kind.Neutral;
        }

        public static string Hex(Kind kind)
        {
            switch (kind)
            {
                case Kind.Ready: return ReadyHex;
                case Kind.Caution: return CautionHex;
                case Kind.Danger: return DangerHex;
                default: return NeutralHex;
            }
        }

        public static string Label(Kind kind)
        {
            switch (kind)
            {
                case Kind.Ready: return "UPDATE";
                case Kind.Caution: return "CONTACT";
                case Kind.Danger: return "LOSS";
                default: return "REPORT";
            }
        }

        /// <summary>Native faction colours are authored for a different background. Strip presentation
        /// tags so they cannot override the readable mirrored body or fade old reports to near-black.</summary>
        public static string Plain(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            var clean = new System.Text.StringBuilder(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '<')
                {
                    int end = line.IndexOf('>', i + 1);
                    if (end >= 0)
                    {
                        string tag = line.Substring(i + 1, end - i - 1).Trim();
                        string name = tag.TrimStart('/').Split('=', ' ')[0];
                        bool presentation = name.StartsWith("#", System.StringComparison.Ordinal) ||
                            name.Equals("color", System.StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("alpha", System.StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("mark", System.StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("size", System.StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("font", System.StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("material", System.StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("b", System.StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("i", System.StringComparison.OrdinalIgnoreCase);
                        if (presentation) { i = end; continue; }
                    }
                }
                clean.Append(line[i]);
            }
            return clean.ToString();
        }

        /// <summary>Keep the report itself warm white; only its separate category marker carries tone.</summary>
        public static string Paint(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            return "<color=#" + NeutralHex + ">" + Plain(line) + "</color>";
        }

        private static bool Contains(string line, string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (line.IndexOf(words[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
