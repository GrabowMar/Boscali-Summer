using System;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.Calls;

namespace BoscaliSummer.Modules.Support.Domain.C2
{

    /// <summary>The words of the CAP page: row state, reason chip kind, hover help and the footer line. Pure; no game types.</summary>
    internal static class C2Cap
    {

        /// <summary>The state word a row shows (cooldown as a clock).</summary>
        public static string StateWord(in CallTile t)
        {
            switch (t.State)
            {
                case CallState.Ready: return "READY";
                case CallState.Armed: return "ARMED";
                case CallState.Pending: return "PENDING";
                case CallState.Locked: return "LOCKED";
                case CallState.LowCredit: return "LOW ALLOC";
                case CallState.Offline: return "OFFLINE";
                default:
                    return TryCooldownSeconds(t.StateWord, out int s) ? "COOL " + C2Words.Clock(s) : "COOL";
            }
        }

        public static bool StartsNegative(string words) =>
            words != null && words.StartsWith("NEGATIVE", StringComparison.Ordinal);

        private static bool TryCooldownSeconds(string word, out int seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(word) || word[word.Length - 1] != 's') return false;
            return int.TryParse(word.Substring(0, word.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
        }
    }
}
