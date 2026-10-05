namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    internal enum RibbonId : byte { Kills5, Kills10, Sorties5, Sorties20, Wingman, Veteran, Ace, Legend }

    /// <summary>The Personnel File's ribbon rack (user ruling 2026-10-04: simple thresholds, display only). Rank is the WingRank
    /// integer (0 Rookie … 4 Legend).</summary>
    internal static class Ribbons
    {
        public const int Max = 8;

        public static int For(int kills, int sorties, int rank, RibbonId[] into)
        {
            if (into == null) return 0;
            int n = 0;
            void Add(bool earned, RibbonId id)
            {
                if (earned && n < into.Length) into[n++] = id;
            }
            Add(kills >= 5, RibbonId.Kills5);
            Add(kills >= 10, RibbonId.Kills10);
            Add(sorties >= 5, RibbonId.Sorties5);
            Add(sorties >= 20, RibbonId.Sorties20);
            Add(rank >= 1, RibbonId.Wingman);
            Add(rank >= 2, RibbonId.Veteran);
            Add(rank >= 3, RibbonId.Ace);
            Add(rank >= 4, RibbonId.Legend);
            return n;
        }

        public static string Word(RibbonId id)
        {
            switch (id)
            {
                case RibbonId.Kills5: return "5 KILLS";
                case RibbonId.Kills10: return "10 KILLS";
                case RibbonId.Sorties5: return "5 SORTIES";
                case RibbonId.Sorties20: return "20 SORTIES";
                case RibbonId.Wingman: return "WINGMAN";
                case RibbonId.Veteran: return "VETERAN";
                case RibbonId.Ace: return "ACE";
                default: return "LEGEND";
            }
        }
    }
}
