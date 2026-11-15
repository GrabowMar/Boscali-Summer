namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The nation layer's screen words (pure): policy and outcome words. Decisions, doctrines and cards are in Politics.</summary>
    internal static class NationWords
    {
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
                case NationOutcome.NotProduced: return "NOT PRODUCED";
                case NationOutcome.NoSupport: return "NOT ENOUGH WAR SUPPORT";
                default: return "";
            }
        }
    }
}
