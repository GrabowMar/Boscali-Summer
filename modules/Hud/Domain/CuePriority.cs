using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the pilot is doing right now, for ranking the board's feeds.</summary>
    [Flags]
    internal enum CueContext : byte { None = 0, Approach = 1, Dock = 2, Combat = 4 }

    /// <summary>
    /// Context ranking for the feed cells and the cue strip (2026-10-10), pure. Severity still decides first (a Warning beats
    /// a Caution beats an Info); among feeds of the same tone the one that matters in the pilot's current situation wins the
    /// leftmost cell and with it the cue strip: the ILS words on approach, the SKYWELL director cues at the boom, the missile
    /// and strike lines in combat. Without a context the order is a fixed, sensible default, not acquisition order (which let a
    /// routine fuel line own the strip for most of a flight).
    /// </summary>
    internal static class CuePriority
    {
        public const int Default = 40;

        public static CueContext Detect(bool approach, bool dock, bool combat) =>
            (approach ? CueContext.Approach : 0) | (dock ? CueContext.Dock : 0) | (combat ? CueContext.Combat : 0);

        /// <summary>0..100; higher wins the cue strip among feeds of the same tone.</summary>
        public static int Rank(string channel, CueContext context)
        {
            int rank = Base(channel);
            if ((context & CueContext.Approach) != 0) rank = Math.Max(rank, Boost(channel, "ils", 100, "autopilot", 80));
            if ((context & CueContext.Dock) != 0) rank = Math.Max(rank, Boost(channel, "skywell", 100, "autopilot", 70));
            if ((context & CueContext.Combat) != 0)
                rank = Math.Max(rank, channel == "missile" ? 100 : channel == "lance" ? 95 : channel == "ace-hunt" ? 90 : channel == "support" ? 85 : 0);
            return rank;
        }

        private static int Boost(string channel, string first, int firstRank, string second, int secondRank) =>
            channel == first ? firstRank : channel == second ? secondRank : 0;

        private static int Base(string channel)
        {
            switch (channel)
            {
                case "missile": return 60;
                case "lance": return 55;
                case "support":
                case "ace-hunt": return 50;
                case "contracts": return 45;
                case "ils":
                case "skywell":
                case "autopilot": return 35;
                case "radio": return 30;
                default: return Default;
            }
        }
    }
}
