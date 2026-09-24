using System;
using System.Text.RegularExpressions;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Converts raw airbase scene object names and unit keys into clean, immersion-friendly
    /// military designations and extracts operational telemetry for MFD displays.
    /// </summary>
    internal static class MfdAirbaseFormatter
    {
        private static readonly Regex CloneSuffix = new Regex(@"\s*\(Clone\)$", RegexOptions.Compiled);
        private static readonly Regex UnitAirbasePrefix = new Regex(@"^<UNIT_AIRBASE>\+\+", RegexOptions.Compiled);

        /// <summary>Format an Airbase component into a clean tactical callsign / name.</summary>
        public static string Format(Airbase airbase)
        {
            if (airbase == null) return "AIRBASE";

            // 1. If mission designer provided an explicit DisplayName on SavedAirbase
            try
            {
                if (airbase.SavedAirbase != null && !string.IsNullOrWhiteSpace(airbase.SavedAirbase.DisplayName))
                {
                    return FormatRaw(airbase.SavedAirbase.DisplayName);
                }
            }
            catch
            {
                // Fallback to raw object inspection
            }

            // 2. Try unit-attached airbase name (carriers, ships)
            try
            {
                if (airbase.TryGetAttachedUnit(out Unit unit) && unit != null)
                {
                    if (!string.IsNullOrWhiteSpace(unit.UniqueName))
                        return FormatRaw(unit.UniqueName);
                    if (!string.IsNullOrWhiteSpace(unit.name))
                        return FormatRaw(unit.name);
                }
            }
            catch
            {
                // Fallback
            }

            // 3. Fallback to MapTower or Airbase GameObject name
            try
            {
                if (airbase.MapTower != null && !string.IsNullOrWhiteSpace(airbase.MapTower.UniqueName))
                {
                    return FormatRaw(airbase.MapTower.UniqueName);
                }
            }
            catch
            {
                // Fallback
            }

            return FormatRaw(airbase.name);
        }

        /// <summary>Format a raw identifier string into a clean title.</summary>
        public static string FormatRaw(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "AIRBASE";

            string s = raw.Trim();
            s = UnitAirbasePrefix.Replace(s, "");
            s = CloneSuffix.Replace(s, "").Trim();

            if (string.IsNullOrEmpty(s)) return "AIRBASE";

            // If it already has clean spaced words without underscores or camelcase squish, preserve
            if (s.Contains(" ") && !s.Contains("_"))
            {
                return s;
            }

            // Known pattern: airbase_island5 -> Island 05 Airfield
            Match mIsland = Regex.Match(s, @"^airbase_island(\d+)$", RegexOptions.IgnoreCase);
            if (mIsland.Success)
            {
                int num = int.TryParse(mIsland.Groups[1].Value, out int n) ? n : 0;
                return "Island " + (num > 0 ? num.ToString("00") : mIsland.Groups[1].Value) + " Airfield";
            }

            // Known cardinal directions: airbase_NE, airbase_north, etc.
            Match mCardinal = Regex.Match(s, @"^airbase_([A-Za-z]+)$", RegexOptions.IgnoreCase);
            if (mCardinal.Success)
            {
                string tag = mCardinal.Groups[1].Value.ToUpperInvariant();
                switch (tag)
                {
                    case "NE": return "Northeast Airbase";
                    case "NW": return "Northwest Airbase";
                    case "SE": return "Southeast Airbase";
                    case "SW": return "Southwest Airbase";
                    case "N": return "North Airbase";
                    case "S": return "South Airbase";
                    case "E": return "East Airbase";
                    case "W": return "West Airbase";
                    case "MAIN": return "Main Airbase";
                    case "CENTRAL": return "Central Airfield";
                    case "BOSCALI_NORTH": return "North Boscali Airbase";
                    case "CITY": return "City Airfield";
                    default:
                        return char.ToUpperInvariant(tag[0]) + tag.Substring(1).ToLowerInvariant() + " Airbase";
                }
            }

            // Carrier designations: AssaultCarrier1 -> Assault Carrier 01
            Match mCarrier = Regex.Match(s, @"^(AssaultCarrier|SmallCarrier|Carrier)(\d+)$", RegexOptions.IgnoreCase);
            if (mCarrier.Success)
            {
                string type = mCarrier.Groups[1].Value;
                string num = mCarrier.Groups[2].Value;
                if (int.TryParse(num, out int n)) num = n.ToString("00");
                string prefix = type.StartsWith("Assault", StringComparison.OrdinalIgnoreCase)
                    ? "Assault Carrier "
                    : type.StartsWith("Small", StringComparison.OrdinalIgnoreCase)
                    ? "Light Carrier "
                    : "Fleet Carrier ";
                return prefix + num;
            }

            // Clean underscores
            string clean = s.Replace("_", " ");

            // Split camelCase words
            clean = Regex.Replace(clean, @"([a-z])([A-Z])", "$1 $2");

            // Split letters and numbers (e.g. HIGHWAYSTRIP2 -> HIGHWAYSTRIP 2)
            clean = Regex.Replace(clean, @"([a-zA-Z])(\d+)", "$1 $2");

            // Capitalize individual words
            string[] words = clean.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Equals("airbase", StringComparison.OrdinalIgnoreCase)) words[i] = "Airbase";
                else if (w.Equals("heliport", StringComparison.OrdinalIgnoreCase)) words[i] = "Heliport";
                else if (w.Equals("carrier", StringComparison.OrdinalIgnoreCase)) words[i] = "Carrier";
                else if (w.Equals("highwaystrip", StringComparison.OrdinalIgnoreCase)) words[i] = "Highway Strip";
                else if (w.Equals("enrichmentplantnorth", StringComparison.OrdinalIgnoreCase)) words[i] = "Enrichment Plant North";
                else if (w.Length > 0)
                {
                    words[i] = char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "");
                }
            }

            string result = string.Join(" ", words);

            // Pad single digits at the end into two digits (e.g. "Highway Strip 2" -> "Highway Strip 02")
            Match mTrailingDigit = Regex.Match(result, @"^(.*)\b(\d)$");
            if (mTrailingDigit.Success)
            {
                result = mTrailingDigit.Groups[1].Value + "0" + mTrailingDigit.Groups[2].Value;
            }

            return result;
        }

        /// <summary>
        /// Generates a secondary operational telemetry readout for an airbase
        /// (e.g., "2 RUNWAYS • HANGARS READY", "1 HELIPAD • CARRIER").
        /// </summary>
        public static string OperationalTelemetry(Airbase airbase)
        {
            if (airbase == null) return "NO TELEMETRY";

            int runways = airbase.runways != null ? airbase.runways.Length : 0;
            int vpads = airbase.verticalLandingPoints != null ? airbase.verticalLandingPoints.Length : 0;
            bool hangars = airbase.AnyHangarsAvailable();
            bool isAttached = airbase.AttachedAirbase;

            string facility;
            if (runways > 0)
            {
                facility = runways == 1 ? "1 RUNWAY" : runways + " RUNWAYS";
            }
            else if (vpads > 0)
            {
                facility = vpads == 1 ? "1 HELIPAD" : vpads + " HELIPADS";
            }
            else
            {
                facility = "AERODROME";
            }

            string status = airbase.disabled ? "OFFLINE"
                : hangars ? "HANGARS READY"
                : "DEPOT READY";

            if (isAttached)
            {
                return facility + "  ·  NAVAL ASSET  ·  " + status;
            }

            return facility + "  ·  " + status;
        }
    }
}
