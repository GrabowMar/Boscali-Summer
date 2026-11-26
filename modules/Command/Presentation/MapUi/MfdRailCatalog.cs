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
        public MfdRailEntry(string code, string name, AvIcon glyph, MfdRailGroup group, int place)
        {
            Code = code;
            Name = name;
            Glyph = glyph;
            Group = group;
            Rank = (int)group * 100 + place;
        }

        public readonly string Code;
        public readonly string Name;
        public readonly AvIcon Glyph;
        public readonly MfdRailGroup Group;

        /// <summary>Place in the rail: by group, then by the catalog's order inside it.</summary>
        public readonly int Rank;

        public bool HasName => !string.IsNullOrEmpty(Name);
    }

    /// <summary>When a pilot asks a key's question; the rail gathers its keys under these, in this order.</summary>
    internal enum MfdRailGroup { Sortie, War, Map, System, Aux }

    /// <summary>
    /// How the rail spends its height: keys under their short group captions while the keys stay comfortable,
    /// then bare keys down to the 44px pointer floor. Below that it does not fit and the stock bezel is left alone.
    /// </summary>
    internal readonly struct MfdRailPlan
    {
        public const float KeyMax = 60f;
        public const float KeyMin = 44f;
        public const float Gap = 2f;
        public const float Pad = 4f;
        public const float GroupHeight = 16f;

        public readonly bool Fits;
        public readonly bool Groups;
        public readonly float KeyHeight;

        private MfdRailPlan(bool fits, bool groups, float keyHeight)
        {
            Fits = fits;
            Groups = groups;
            KeyHeight = (float)System.Math.Floor(System.Math.Min(KeyMax, keyHeight));
        }

        public static MfdRailPlan Resolve(float height, int keys, int groups)
        {
            if (keys <= 0) return new MfdRailPlan(true, false, KeyMax);

            float body = height - Pad * 2f - (keys - 1) * Gap;
            float dressed = (body - groups * GroupHeight) / keys;
            // Group labels outrank tall keys (user ruling 2026-10-25): they stay down to 46px keys.
            if (dressed >= 46f) return new MfdRailPlan(true, true, dressed);
            return new MfdRailPlan(body / keys >= KeyMin, false, body / keys);
        }
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
            // The name is the console's own title word. FAC is the merged faction readout; the native HQ codes stay recognised
            // for the moments the game shows them. Each screen wears a glyph of its own.
            switch (code)
            {
                case "WMC": return new MfdRailEntry(code, "WING", AvIcon.Formation, MfdRailGroup.Sortie, 0);
                case "PILOT": return new MfdRailEntry(code, "PILOT", AvIcon.Pilot, MfdRailGroup.Sortie, 1);
                case "SQD": return new MfdRailEntry(code, "PILOT", AvIcon.Pilot, MfdRailGroup.Sortie, 2);
                case "STR": return new MfdRailEntry(code, "THEATER", AvIcon.Territory, MfdRailGroup.War, 0);
                case "MIS": return new MfdRailEntry(code, "MISSIONS", AvIcon.Clipboard, MfdRailGroup.War, 1);
                case "OPS": return new MfdRailEntry(code, "SUPPORT", AvIcon.CallDown, MfdRailGroup.War, 2);
                case "FAC": return new MfdRailEntry(code, "FACTION", AvIcon.Crest, MfdRailGroup.War, 3);
                case "BDF": return new MfdRailEntry(code, "BOSCALI HQ", AvIcon.Crest, MfdRailGroup.War, 4);
                case "PALA": return new MfdRailEntry(code, "PALA HQ", AvIcon.Crest, MfdRailGroup.War, 5);
                case "EVN": return new MfdRailEntry(code, "EVENTS", AvIcon.Burst, MfdRailGroup.War, 6);
                case "COM": return new MfdRailEntry(code, "COMMS", AvIcon.Headset, MfdRailGroup.Map, 0);
                case "MAP": return new MfdRailEntry(code, "MAP", AvIcon.Map2, MfdRailGroup.Map, 1);
                case "TGT": return new MfdRailEntry(code, "TARGETS", AvIcon.Designator, MfdRailGroup.Map, 2);
                case "DRN": return new MfdRailEntry(code, "DRONES", AvIcon.Drone, MfdRailGroup.Map, 3);
                case "SET": return new MfdRailEntry(code, "SETTINGS", AvIcon.AdjustmentsHorizontal, MfdRailGroup.System, 0);
                case "ENV": return new MfdRailEntry(code, "WEATHER", AvIcon.Cloud, MfdRailGroup.System, 1);
                case "RAD": return new MfdRailEntry(code, "RADIO", AvIcon.Radio, MfdRailGroup.System, 2);
                case "MFD": return new MfdRailEntry(code, "DISPLAY", AvIcon.Focus2, MfdRailGroup.System, 3);
                // Last key of the rail, under any unknown code too, and pinned to its bottom edge (MfdRail.Lay). Ships for everyone.
                case "MISC": return new MfdRailEntry(code, "EXTRAS", AvIcon.Stack2, MfdRailGroup.System, 999);
                default: return new MfdRailEntry(code, null, NeutralGlyph, MfdRailGroup.Aux, 0);
            }
        }

        /// <summary>
        /// A screen that keeps its bezel slot but gets no key: HUD, whose priority lists live on TGT FILTER. Its screen stays,
        /// because MfdBezel lays every mod screen out on it.
        /// </summary>
        public static bool OffRail(string code) => code == "HUD";

        /// <summary>The label a group of keys sits under.</summary>
        public static string GroupName(MfdRailGroup group)
        {
            switch (group)
            {
                case MfdRailGroup.Sortie: return "SORTIE";
                case MfdRailGroup.War: return "WAR";
                case MfdRailGroup.Map: return "MAP";
                default: return "SYSTEM";
            }
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
