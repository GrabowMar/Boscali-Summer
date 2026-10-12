using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>
    /// The staff-log lines the reinforcement desk writes. Each is at most 64 characters (StaffLog cuts, never wraps), upper case, and
    /// prints a dash for anything not known, never a confident zero. The reason tokens are short log words, not the pilot-facing
    /// sentences of TheaterWords.Wave (ruling N-24).
    /// </summary>
    internal static class WaveText
    {
        private const string Sep = " · ";
        private const int MaximumTitle = 27, MaximumPilot = 12, MaximumGroup = 20, MaximumNumber = 9999999;

        /// <summary>A finished sortie that came too late for its operation's window: paid, but no wave credit.</summary>
        internal const string NoCredit = "SORTIE DONE" + Sep + "OPERATION CHANGED" + Sep + "NO WAVE CREDIT";

        /// <summary>The bank after a counted sortie; credits banked beyond one wave (a held wave) still read as a full 3/3.</summary>
        internal static string Counted(int credits, string title, string pilot) =>
            "SORTIE " + (credits < 0 ? "-" : Math.Min(credits, WaveLedger.CreditsPerWave).ToString(CultureInfo.InvariantCulture)) + "/" +
            WaveLedger.CreditsPerWave + " DONE" + Sep + Clip(title, MaximumTitle) + Sep + Clip(pilot, MaximumPilot);

        internal static string BankFull(string title) =>
            "SORTIE DONE" + Sep + Clip(title, MaximumTitle) + Sep + "WAVE BANK FULL";

        internal static string Bought(string group, int cost, int pool) =>
            "WAVE BOUGHT" + Sep + Clip(group, MaximumGroup) + Sep + Spent(cost) + Sep + "POOL " + Number(pool);

        internal static string Held(WaveBlock block, int cost, int pool) =>
            "WAVE HELD" + Sep + Reason(block) + Sep + "COST " + Number(cost) + " POOL " + Number(pool);

        internal static string AiConvoy(string group, int cost) =>
            "AI CONVOY" + Sep + Clip(group, MaximumGroup) + Sep + Spent(cost);

        internal static string EventConvoy(string group, int cost) =>
            "EVENT CONVOY" + Sep + Clip(group, MaximumGroup) + Sep + Spent(cost);

        /// <summary>The short upper-case token for the gate that holds a wave, at most 16 characters; a dash for an undefined value.</summary>
        internal static string Reason(WaveBlock block)
        {
            switch (block)
            {
                case WaveBlock.None: return "READY";
                case WaveBlock.SwitchedOff: return "SWITCHED OFF";
                case WaveBlock.NoSorties: return "NO SORTIES";
                case WaveBlock.MissionForbids: return "MISSION FORBIDS";
                case WaveBlock.NoOperation: return "NO OPERATION";
                case WaveBlock.NoDepot: return "NO DEPOT";
                case WaveBlock.NoGroup: return "NO GROUP";
                case WaveBlock.Cooling: return "COOLING";
                case WaveBlock.PoolShort: return "POOL SHORT";
                default: return "-";
            }
        }

        // A negative number is unknown. Seven digits is the widest the line has room for.
        private static string Number(int value) =>
            value < 0 ? "-" : Math.Min(value, MaximumNumber).ToString(CultureInfo.InvariantCulture);

        private static string Spent(int cost) => cost < 0 ? "-" : "-" + Number(cost);

        private static string Clip(string text, int maximum)
        {
            string clean = text == null ? string.Empty : text.Trim().ToUpperInvariant();
            if (clean.Length == 0) return "-";
            return clean.Length > maximum ? clean.Substring(0, maximum).TrimEnd() : clean;
        }
    }
}
