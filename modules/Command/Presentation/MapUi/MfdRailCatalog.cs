using System.Text;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// One rail button's identity: the short code the game already prints, the words that
    /// say what the code means, and the kit v2 <see cref="AvIcon"/> that carries the same
    /// meaning without reading.
    ///
    /// Pure data: the rail renders it, tests read it, and nothing here touches Unity.
    /// </summary>
    internal readonly struct MfdRailEntry
    {
        public MfdRailEntry(string code, string name, AvIcon glyph)
        {
            Code = code;
            Name = name;
            Glyph = glyph;
        }

        public readonly string Code;
        public readonly string Name;
        public readonly AvIcon Glyph;

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

        /// <summary>The neutral icon an unrecognised code still gets (spec §5.4's "list" fallback).</summary>
        private const AvIcon NeutralGlyph = AvIcon.ListDetails;

        public static MfdRailEntry For(string label)
        {
            string code = Sanitise(label, MaxCodeLength);
            switch (code)
            {
                case "FAC": return new MfdRailEntry(code, "FACTIONS", AvIcon.BuildingBank);
                case "BDF": return new MfdRailEntry(code, "BOSCALI HQ", AvIcon.BuildingBank);
                case "PALA": return new MfdRailEntry(code, "PALA HQ", AvIcon.BuildingBank);
                case "MAP": return new MfdRailEntry(code, "TACTICAL", AvIcon.Map2);
                case "MFD": return new MfdRailEntry(code, "DISPLAY", AvIcon.Focus2);
                case "HUD": return new MfdRailEntry(code, "SYMBOLOGY", AvIcon.Focus2);
                case "TGT": return new MfdRailEntry(code, "TARGETS", AvIcon.Target);
                case "MIS": return new MfdRailEntry(code, "MISSION", AvIcon.Flag);
                case "WMC": return new MfdRailEntry(code, "WING CMD", AvIcon.Plane);
                case "OPS": return new MfdRailEntry(code, "SUPPORT", AvIcon.Stack2);
                case "STR": return new MfdRailEntry(code, "THEATER", AvIcon.ChartArrows);
                case "SQD": return new MfdRailEntry(code, "PILOT", AvIcon.User);
                case "PILOT": return new MfdRailEntry(code, "PERSONNEL", AvIcon.User);
                case "RAD": return new MfdRailEntry(code, "RADIO", AvIcon.Antenna);
                case "SET": return new MfdRailEntry(code, "SETTINGS", AvIcon.AdjustmentsHorizontal);
                case "EVN": return new MfdRailEntry(code, "EVENTS", AvIcon.Activity);
                case "COM": return new MfdRailEntry(code, "COMMS", AvIcon.Message2);
                case "ENV": return new MfdRailEntry(code, "WEATHER", AvIcon.Cloud);
                default: return new MfdRailEntry(code, null, NeutralGlyph);
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
