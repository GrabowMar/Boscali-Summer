namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The nation layer's screen words (pure): focus names in <see cref="FocusId"/> order, policy and outcome words.</summary>
    internal static class NationWords
    {
        private static readonly string[] Focuses =
        {
            "NEW WORKS", "WAR INDUSTRY", "STATE CONTRACTS", "TOTAL MOBILISATION",
            "FIGHTER RESERVE", "SUPPLY CORPS", "ARMOURED RESERVE", "AIR ARMADA",
            "NATIONAL UNITY", "WAR BONDS", "SIGNALS BUREAU", "BLACK BUDGET",
        };

        public static string Focus(int id) => id >= 0 && id < Focuses.Length ? Focuses[id] : "";

        private static readonly string[] Effects =
        {
            "+1 slot, free factory", "Output +10 %", "Factory cost -25 %", "+2 slots, output +15 %",
            "4 fighters to stock", "Convoys -25 %", "6 armour, 3 air defence", "Wingman calls -25 %",
            "Stability +15", "5 000 funds now", "OPS income +50 %", "Perk prices -20 %",
        };

        public static string FocusEffect(int id) => id >= 0 && id < Effects.Length ? Effects[id].ToUpperInvariant() : "";

        /// <summary>INDUSTRY, MILITARY or STATE for a focus id (branch-major order).</summary>
        public static string BranchName(int id) => id < 4 ? "INDUSTRY" : id < 8 ? "MILITARY" : "STATE";

        public static string EconomyWord(EconomyPolicy p) => p == EconomyPolicy.War ? "WAR ECONOMY" : p == EconomyPolicy.Civilian ? "CIVILIAN ECONOMY" : "BALANCED ECONOMY";

        public static string PriorityWord(PriorityPolicy p) => p == PriorityPolicy.Air ? "AIR FIRST" : p == PriorityPolicy.Ground ? "GROUND FIRST" : "BALANCED";

        public static string Outcome(NationOutcome o)
        {
            switch (o)
            {
                case NationOutcome.Off: return "NATION OFF";
                case NationOutcome.Locked: return "LOCKED";
                case NationOutcome.NotOpen: return "NOT OPEN YET";
                case NationOutcome.NoFunds: return "NO FUNDS";
                case NationOutcome.NoSlot: return "NO FREE SLOT";
                case NationOutcome.NoSite: return "NO GROUND FOR IT";
                case NationOutcome.Busy: return "BUSY";
                case NationOutcome.Unknown: return "UNKNOWN";
                case NationOutcome.NotMember: return "NOT YOUR FACTION";
                default: return "";
            }
        }
    }
}
