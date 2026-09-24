using System.Text;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// One rail button's identity: the short code the game already prints, the words that
    /// say what the code means, and the vector glyph that carries the same meaning without
    /// reading.
    ///
    /// Pure data: the rail renders it, tests read it, and nothing here touches Unity.
    /// </summary>
    internal readonly struct MfdRailEntry
    {
        public MfdRailEntry(string code, string name, string glyph)
        {
            Code = code;
            Name = name;
            Glyph = glyph;
        }

        public readonly string Code;
        public readonly string Name;
        public readonly string Glyph;

        public bool HasName => !string.IsNullOrEmpty(Name);
    }

    /// <summary>
    /// The dictionary between the bezel's three-letter short names and the words a player
    /// who has not memorised the cabin can read.
    ///
    /// The codes are load-bearing (the dual-mod protocol claims slots by
    /// <c>MFDScreen.shortName</c>), so they stay on the button; the descriptor sits under
    /// them. An unrecognised code still gets sanitised styling and a neutral glyph — a
    /// future game screen must never be renamed to fit this table.
    /// </summary>
    internal static class MfdRailCatalog
    {
        private const int MaxCodeLength = 8;

        public static MfdRailEntry For(string label)
        {
            string code = Sanitise(label, MaxCodeLength);
            switch (code)
            {
                case "FAC": return new MfdRailEntry(code, "FACTIONS", "faction");
                case "BDF": return new MfdRailEntry(code, "BOSCALI HQ", "faction");
                case "PALA": return new MfdRailEntry(code, "PALA HQ", "faction");
                case "MAP": return new MfdRailEntry(code, "TACTICAL", "map");
                case "MFD": return new MfdRailEntry(code, "DISPLAY", "hud");
                case "HUD": return new MfdRailEntry(code, "SYMBOLOGY", "hud");
                case "TGT": return new MfdRailEntry(code, "TARGETS", "target");
                case "MIS": return new MfdRailEntry(code, "MISSION", "flag");
                case "WMC": return new MfdRailEntry(code, "WING CMD", "air");
                case "OPS": return new MfdRailEntry(code, "SUPPORT", "support");
                case "STR": return new MfdRailEntry(code, "THEATER", "theater");
                case "SQD": return new MfdRailEntry(code, "PILOT", "person");
                case "PILOT": return new MfdRailEntry(code, "PERSONNEL", "person");
                case "RAD": return new MfdRailEntry(code, "RADIO", "radio");
                case "SET": return new MfdRailEntry(code, "SETTINGS", "settings");
                case "EVN": return new MfdRailEntry(code, "EVENTS", "pulse");
                case "COM": return new MfdRailEntry(code, "COMMS", "comms");
                case "ENV": return new MfdRailEntry(code, "WEATHER", "weather");
                default: return new MfdRailEntry(code, null, null);
            }
        }

        /// <summary>
        /// Codes that lead the rail, in the order they lead it. Every other button keeps
        /// the game's own order behind them. FAC is the merged faction readout; the
        /// native codes remain recognised while the game is setting up its screens.
        /// </summary>
        private static readonly string[] LeadCodes = { "FAC", "BDF", "PALA" };

        /// <summary>
        /// Rank in the rail's display order: the lead codes first, everything else after.
        /// The caller sorts stably, so unranked buttons keep the order the game gave them.
        /// </summary>
        public static int OrderRank(string label)
        {
            string code = Sanitise(label, MaxCodeLength);
            for (int i = 0; i < LeadCodes.Length; i++)
            {
                if (LeadCodes[i] == code) return i;
            }
            return LeadCodes.Length;
        }

        /// <summary>
        /// Uppercase, markup-free, whitespace-collapsed copy of a label. Button text comes
        /// from the game, so it can contain rich-text angle brackets or control characters;
        /// neither may leak into a label the mod composes.
        /// </summary>
        public static string Sanitise(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0) return "";

            var builder = new StringBuilder(maxLength);
            bool pendingSpace = false;

            for (int i = 0; i < text.Length && builder.Length < maxLength; i++)
            {
                char c = text[i];

                // Whitespace is tested before the control check on purpose: TMP labels
                // carry line breaks, and \n is a control character too. Testing IsControl
                // first deleted the break instead of collapsing it, which glued a
                // branded label's own markup onto its code ("BDF\n<size=" -> "BDFSIZE=").
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (c == '<' || c == '>' || char.IsControl(c)) continue;

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                    if (builder.Length >= maxLength) break;
                }

                builder.Append(char.ToUpperInvariant(c));
            }

            return builder.ToString().TrimEnd(' ');
        }
    }
}
