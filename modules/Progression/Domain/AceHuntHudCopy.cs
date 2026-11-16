
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
        /// The card's second row: "TALON 01 · 3/4 · 034° 12KM · HEAT 40%". <paramref name="bearingDeg"/> is the ace's compass bearing (what the tape shows under its ACE mark), -1 when its position is not
        /// known (a client has none); <paramref name="range"/> its distance already formatted ("12KM"); <paramref name="heatPercent"/> -1 when there is no heat. Unknown parts are left out, never invented.
        /// </summary>
        public static string Line(string flight, int bearingDeg, string range, int heatPercent)
        {
            string line = flight ?? "";
            if (bearingDeg >= 0)
            {
                int b = bearingDeg % 360;
                line += (line.Length > 0 ? " · " : "") + (b < 100 ? "0" : "") + (b < 10 ? "0" : "") + b + "°" + (string.IsNullOrEmpty(range) ? "" : " " + range);
            }
            if (heatPercent >= 0) line += (line.Length > 0 ? " · " : "") + "HEAT " + heatPercent + "%";
            return line;
        }
    }
}
