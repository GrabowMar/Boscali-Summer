namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Reads the theatre out of a game message line so the event stream is not a wall of
    /// identical grey text. Classification is deliberately keyword-based and one-way: the
    /// game owns the wording, this only paints what is already there, and an unrecognised
    /// line stays neutral rather than being forced into a meaning it may not have.
    ///
    /// Pure: no Unity, no theme, no per-frame allocation beyond the painted string the
    /// panel already built. Colours are the literal status rails, not the live accent, so
    /// a mission theme cannot make "destroyed" and "captured" the same hue.
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

        public const string NeutralHex = "8FA8B8";
        public const string ReadyHex = "00FFA3";
        public const string CautionHex = "FFB300";
        public const string DangerHex = "FF2A55";

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

        /// <summary>Wrap one line in its tone colour. TMP rich text, so nesting is safe.</summary>
        public static string Paint(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            return "<color=#" + Hex(Classify(line)) + ">" + line + "</color>";
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
