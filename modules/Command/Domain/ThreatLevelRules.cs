using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The threat level the SITREP tab and the news ticker show (annex 01 E12): 1 maximum to 5 nominal, 0 unknown.
    /// It reads the host's census only. Hostile air is what the faction tracks, so the comparison can miss a threat but never invent one.
    /// </summary>
    internal static class ThreatLevelRules
    {
        /// <summary>
        /// First rule that fires on known inputs wins; an unknown input never blocks a rule that does not need it, and 5 needs bases, air and sectors all known.
        /// 1: two contested bases, or one while tracked hostile air outnumbers ours. 2: one contested base, or four or more hostile aircraft and more than double ours.
        /// 3: eight or more contested sectors, or hostile air outnumbering ours. 4: any tracked hostile aircraft, or one to seven contested sectors. 5: none of those.
        /// </summary>
        internal static byte Level(in TheaterCensus c, out TheaterThreatWhy why)
        {
            why = TheaterThreatWhy.None;
            if (!c.Known) return 0;
            int bases = c.BasesContested, sectors = c.SectorsContested, ours = c.AirFriendly, theirs = c.AirHostileTracked;
            bool airKnown = ours >= 0 && theirs >= 0;
            bool outnumbered = airKnown && theirs > ours;

            if (bases >= 2) { why = TheaterThreatWhy.BasesContested; return 1; }
            if (bases == 1 && outnumbered) { why = TheaterThreatWhy.BaseContested; return 1; }
            if (bases == 1) { why = TheaterThreatWhy.BaseContested; return 2; }
            if (airKnown && theirs >= 4 && theirs > 2 * ours) { why = TheaterThreatWhy.AirOutnumbered; return 2; }
            if (sectors >= 8) { why = TheaterThreatWhy.GroundContact; return 3; }
            if (outnumbered) { why = TheaterThreatWhy.AirOutnumbered; return 3; }
            if (theirs > 0) { why = TheaterThreatWhy.AirContact; return 4; }
            if (sectors >= 1) { why = TheaterThreatWhy.GroundContact; return 4; }
            if (bases >= 0 && airKnown && sectors >= 0) { why = TheaterThreatWhy.Nominal; return 5; }
            return 0;
        }
    }
}
