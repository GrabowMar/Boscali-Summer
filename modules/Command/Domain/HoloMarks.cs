using System;
using System.Globalization;
using System.Text;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>What a mark on the hologram stands for.</summary>
    internal enum HoloKind : byte { Surface, Air }

    /// <summary>
    /// One thing drawn over the hologram: east and north metres (the list's own frame: the runtime keeps global positions and subtracts the aircraft's at draw time), height above the sea,
    /// how far it was from the aircraft when listed, its colour, what it is, and a caller's index (an airbase's place in the scan).
    /// </summary>
    internal struct HoloMark
    {
        public float East, North, Height, Range;
        public uint Colour;
        public HoloKind Kind;
        public int Id;
    }

    /// <summary>
    /// The holo map's mark list and its words, without Unity: a bounded list kept nearest first (units beyond the cap that are farther than the farthest kept are not
    /// listed at all), and the text of every label, so a label is written only when its words change. English capitals, as the rest of the visor.
    /// </summary>
    internal static class HoloMarks
    {
        /// <summary>The most units drawn at once.</summary>
        internal const int MaximumUnits = 96;
        /// <summary>The most airbases named at once.</summary>
        internal const int MaximumAirbases = 6;
        internal const string Caption = "HOLO MAP";
        internal const int NameLength = 14;

        /// <summary>
        /// Offers a mark to a list kept in order of rising range and no longer than the array: a mark nearer than the farthest kept takes its place and pushes the farthest out; a
        /// mark of equal range goes after those already listed. Returns whether the mark was kept.
        /// </summary>
        internal static bool Offer(HoloMark[] list, ref int count, in HoloMark mark)
        {
            if (list.Length == 0 || (count == list.Length && !(mark.Range < list[count - 1].Range))) return false;
            int at = count < list.Length ? count : count - 1;
            while (at > 0 && mark.Range < list[at - 1].Range)
            {
                list[at] = list[at - 1];
                at--;
            }
            list[at] = mark;
            if (count < list.Length) count++;
            return true;
        }

        /// <summary>A range as the map prints it: "20 KM", "2.5 KM", "850 M".</summary>
        internal static string Range(float metres)
        {
            if (!(metres >= 0f)) return "--";
            if (metres < 999.5f) return ((int)(metres + .5f)).ToString(CultureInfo.InvariantCulture) + " M";
            float km = metres / 1000f;
            if (km >= 10f || Math.Abs(km - MathF.Round(km)) < .05f)
                return ((int)(km + .5f)).ToString(CultureInfo.InvariantCulture) + " KM";
            return km.ToString("0.0", CultureInfo.InvariantCulture) + " KM";
        }

        /// <summary>The right end of the caption shelf: the picture's radius and which way is up.</summary>
        internal static string ShelfFlag(int rangeKm, bool northUp) =>
            rangeKm.ToString(CultureInfo.InvariantCulture) + " KM  " + (northUp ? "N UP" : "HDG UP");

        /// <summary>An airbase's label: upper case, trimmed, no longer than <see cref="NameLength"/> characters.</summary>
        internal static string AirbaseLabel(string name) => Clip(name, NameLength);

        /// <summary>The selected target's label: its name (upper case, at most <see cref="NameLength"/> characters) and its range.</summary>
        internal static string TargetLabel(string name, float rangeMetres)
        {
            string clipped = Clip(name, NameLength);
            return clipped.Length == 0 ? Range(rangeMetres) : clipped + "  " + Range(rangeMetres);
        }

        private static string Clip(string text, int length)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(length);
            foreach (char ch in text.Trim())
            {
                if (sb.Length == length) break;
                sb.Append(char.IsControl(ch) ? ' ' : char.ToUpperInvariant(ch));
            }
            return sb.ToString().TrimEnd();
        }
    }
}
