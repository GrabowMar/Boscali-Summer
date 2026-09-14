namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>The three staff tiers every faction fields. Weight is the share of the tree one post is worth.</summary>
    internal static class CommandTier
    {
        public const int Theater = 0;
        public const int Component = 1;
        public const int Base = 2;
        public const int Count = 3;

        /// <summary>Six posts per faction: one theater, two component, three base.</summary>
        public const int SlotCount = 6;

        /// <summary>Hard ceiling if the roster ever grows; wire and buffers are sized from this.</summary>
        public const int MaximumSlots = 8;

        public static bool Valid(int tier) => tier >= 0 && tier < Count;

        public static int Weight(int tier) => tier == Theater ? 3 : tier == Component ? 2 : 1;

        public static string Rank(int tier) =>
            tier == Theater ? "GEN" : tier == Component ? "MAJ GEN" : "COL";

        public static string FallbackRole(int tier) =>
            tier == Theater ? "THEATER COMMANDER"
            : tier == Component ? "COMPONENT COMMANDER"
            : "BASE COMMANDER";
    }
}
