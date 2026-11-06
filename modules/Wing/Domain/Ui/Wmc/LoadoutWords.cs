using System;
using System.Globalization;
using System.Text;
using static BoscaliSummer.Modules.Wing.Domain.WmcText;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>LOADOUT's words (spec WMC rebuild §LOADOUT): the build card, the chain, station and row words, the captions and the
    /// hints — each capped at the source, never with "…".</summary>
    internal static class LoadoutWords
    {
        public const int BuildChars = 60, CaptionChars = 22, StationChars = 40, StoreChars = 48, LiveryChars = 36;
        public const string EmptyStore = "EMPTY", Standard = "STANDARD (FACTION)";
        public const string Unreadable = "This airframe's hardpoints cannot be read here";

        /// <summary>The one empty-state line (no airframe to edit).</summary>
        public const string NoAirframe = "NO AIRFRAME · pick one above";

        /// <summary>Why nothing can be edited on a client: the host launches the wing, so the host's loadouts and liveries are the ones used.</summary>
        public const string ClientWhy = "HOST ONLY · the host launches the wing, so only the host edits loadouts and liveries";

        public static string Title(string code, bool custom) => code + " · " + (custom ? "CUSTOM LOADOUT" : "STANDARD FIT");

        /// <summary>STANDARD (nothing saved yet: the airframe's own stores), SUPPLY FIT (SUPPLY's fit flies the custom loadout), else SAVED.</summary>
        public static string Chip(bool custom, bool supplyFit, out string state)
        {
            state = !custom ? "inert" : supplyFit ? "info" : "live";
            return !custom ? "STANDARD" : supplyFit ? "SUPPLY FIT" : "SAVED";
        }

        /// <summary>The build card's second line (spec bezel v2: the STATIONS, MASS and ROLE tiles are gone; the card carries them):
        /// fitted of stations, stores mass, role, then what does not fly.</summary>
        public static string Build(in FitSummary f, int emptyHere)
        {
            string s = Stations(f) + " FITTED · " + Mass(f.Mass) + " KG · " + Role(f);
            string caption = StationsCaption(f, emptyHere);
            return caption == "ALL FITTED" || s.Length + 3 + caption.Length > BuildChars ? s : s + " · " + caption;
        }

        /// <summary>The station picker's head: "STATION 3 · INNER L" (the number is 1-based).</summary>
        public static string PickerHead(int station, string name) => "STATION " + N(station + 1) + " · " + (name ?? "").ToUpperInvariant();

        public static string PickerHolds(string label) => string.IsNullOrEmpty(label) ? "EMPTY" : WmcText.Cut(label, 30) + " IS FITTED";

        /// <summary>The station's name and, for a pair or a multi-pylon set, how many pylons it fits ("Fuselage Pylon ×2").</summary>
        public static string StationName(string name, int pylons)
        {
            string suffix = pylons > 1 ? " ×" + N(pylons) : "";
            return WmcText.Cut(name, StationChars - suffix.Length) + suffix;
        }

        public static string Stations(in FitSummary f) => N(f.Fitted) + "/" + N(f.Stations);

        public static string Mass(float kg)
        {
            double v = Math.Round(Math.Max(0f, kg));
            return v < 1_000_000 ? v.ToString("N0", CultureInfo.InvariantCulture) : (v / 1000).ToString("N0", CultureInfo.InvariantCulture) + "t";
        }

        /// <summary>A-A or A-G when one side has half again the other's rounds, MULTI when both fly, else what there is.</summary>
        public static string Role(in FitSummary f)
        {
            int aa = f.Aam, ag = f.Agm + f.Bombs;
            if (aa + ag > 0)
            {
                if (aa > ag * 1.5f) return "A-A";
                if (ag > aa * 1.5f) return "A-G";
                return "MULTI";
            }
            if (f.Cargo > 0) return "CARGO";
            if (f.MslDef > 0) return "MSL DEF";
            if (f.Ecm > 0) return "ECM";
            return "UNARMED";
        }

        /// <summary>What does not fly and why, whole parts within 22 characters; ALL FITTED when everything does.</summary>
        public static string StationsCaption(in FitSummary f, int emptyHere)
        {
            int empty = Math.Max(0, f.Stations - f.Fitted - f.Blocked - emptyHere);
            var sb = new StringBuilder();
            Part(sb, emptyHere, "EMPTY HERE", "EMPTY HERE");
            Part(sb, f.Blocked, "BLOCKED", "BLOCKED");
            Part(sb, empty, "EMPTY", "EMPTY");
            return sb.Length == 0 ? "ALL FITTED" : sb.ToString();
        }

        /// <summary>The status strip's alert when stations hold stores this mission refuses; null when none.</summary>
        public static string EmptyHereAlert(int n) =>
            n <= 0 ? null : n == 1 ? "1 STATION LAUNCHES EMPTY HERE · clear it or fit another store"
            : N(n) + " STATIONS LAUNCH EMPTY HERE · clear them or fit other stores";

        public static string Livery(string label) => WmcText.Cut(label, LiveryChars);

        public static string Hint(bool client, bool hasAirframe, bool custom)
        {
            if (client) return ClientWhy;
            if (!hasAirframe) return "Pick an airframe to edit its loadout.";
            return custom ? "A saved preset, not the aircraft in the air: SUPPLY FIT CUSTOM flies it. Click a station to fit a store."
                : "Standard stores shown: the first change saves them as this airframe's CUSTOM loadout for SUPPLY FIT.";
        }

        private static void Part(StringBuilder sb, int n, string one, string many)
        {
            if (n <= 0) return;
            string part = N(n) + " " + (n == 1 ? one : many);
            int add = (sb.Length > 0 ? 3 : 0) + part.Length;
            if (sb.Length + add > CaptionChars) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(part);
        }
    }
}
