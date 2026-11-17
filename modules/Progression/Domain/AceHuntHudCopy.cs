
namespace BoscaliSummer.Modules.Progression.Domain
{
    /// <summary>
    /// The ace card's copy, pure so the test project links it directly. It names the encounter the SQD panel is showing, never a second version of it.
    /// </summary>
    internal static class AceHuntHudCopy
    {
        /// <summary>"TALON 01 · 3/4": who flies with the ace and how many are up (either part left out when unknown).</summary>
        public static string Flight(string wingName, int alive, int members)
        {
            string wing = string.IsNullOrEmpty(wingName) ? "" : wingName.ToUpperInvariant();
            string up = members > 0 ? alive + "/" + members : "";
            return wing.Length > 0 && up.Length > 0 ? wing + " · " + up : wing + up;
        }

        /// <summary>
        /// The card's second row: "TALON 01 · 3/4 · 12KM". <paramref name="range"/> is the ace's distance already formatted ("12KM"), null when its position is not known (a client has none; the bearing is
        /// the tape's, the heat the SYSTEMS strip's). Unknown parts are left out, never invented.
        /// </summary>
        public static string Line(string flight, string range)
        {
            string line = flight ?? "";
            if (!string.IsNullOrEmpty(range)) line += (line.Length > 0 ? " · " : "") + range;
            return line;
        }
    }
}
