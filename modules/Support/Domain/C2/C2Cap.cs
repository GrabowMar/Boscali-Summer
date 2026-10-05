using System;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.Calls;

namespace BoscaliSummer.Modules.Support.Domain.C2
{
    internal enum C2Chip : byte { None, Discount, Surcharge, Other }

    /// <summary>The words of the CAP page: row state, reason chip kind, hover help and the footer line. Pure; no game types.</summary>
    internal static class C2Cap
    {
        /// <summary>A quote reason that starts with a minus is a discount (green), a plus a surcharge (red).</summary>
        public static C2Chip ChipKind(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return C2Chip.None;
            return reason[0] == '-' ? C2Chip.Discount : reason[0] == '+' ? C2Chip.Surcharge : C2Chip.Other;
        }

        /// <summary>The state word a row shows (cooldown as a clock, a freeze without its minutes: the row help carries them).</summary>
        public static string StateWord(in CallTile t)
        {
            switch (t.State)
            {
                case CallState.Ready: return "READY";
                case CallState.Armed: return "ARMED";
                case CallState.Pending: return "PENDING";
                case CallState.Locked: return "LOCKED";
                case CallState.LowCredit: return "LOW CR";
                case CallState.Offline: return "OFFLINE";
                default:
                    return TryCooldownSeconds(t.StateWord, out int s) ? "COOL " + C2Words.Clock(s) : "FROZEN";
            }
        }

        /// <summary>The mono sub-line of a row: its AUTH code and tier, or the lock reason, plus UNLASE FREE on the JTAC row.</summary>
        public static string Sub(in CallTile t, bool unlaseFree)
        {
            string unlockText = UnlockText(t);
            string tier = string.IsNullOrEmpty(t.TierWord) ? "L" : t.TierWord;
            if (t.State == CallState.Locked && !string.IsNullOrEmpty(unlockText)) return "LOCKED · " + unlockText;
            string auth = "AUTH " + C2Words.AuthCode((int)t.Id, tier[0]);
            // The JTAC row trades its tier word for the UNLASE note: the room beside its extra button is short.
            return unlaseFree ? auth + " · UNLASE FREE" : auth + " · " + tier;
        }

        /// <summary>Hover help for a row and its primary button: what pressing does, or the NEGATIVE: reason - fix it is denied for.</summary>
        public static string RowTip(in CallTile t)
        {
            switch (t.State)
            {
                case CallState.Ready:
                    return "Arms " + t.Label + " at " + t.CostText + (string.IsNullOrEmpty(t.Reason) ? "" : " (" + t.Reason + ")") +
                           ". Press again to fire.";
                case CallState.Armed: return "Armed. Press EXECUTE or right-click the map to fire; ABORT disarms.";
                case CallState.Pending: return "Waiting for the host to answer.";
                case CallState.Locked: return CallWords.Refusal(CallRefusal.Locked, unlock: UnlockText(t));
                case CallState.LowCredit: return CallWords.Refusal(CallRefusal.LowCredit, need: Digits(t.CostText));
                case CallState.Offline: return CallWords.Refusal(CallRefusal.Offline);
                default:
                    return TryCooldownSeconds(t.StateWord, out int s)
                        ? CallWords.Refusal(CallRefusal.Cooldown, seconds: s)
                        : CallWords.Refusal(CallRefusal.Frozen, seconds: Digits(t.StateWord) * 60);
            }
        }

        /// <summary>The next-unlock goal a locked tile carries as its state word (empty when it only says LOCKED).</summary>
        public static string UnlockText(in CallTile t) =>
            t.State == CallState.Locked && !string.IsNullOrEmpty(t.StateWord) && t.StateWord != "LOCKED" ? t.StateWord : "";

        /// <summary>The footer slab word: ARMED, WAIT, NEG for a refusal, else READY.</summary>
        public static string Slab(bool armed, bool pending, string words) =>
            armed ? "ARMED" : pending ? "WAIT" : StartsNegative(words) ? "NEG" : "READY";

        /// <summary>The footer words: the controller's last words, else the hotline line with the next unlock.</summary>
        public static string FooterWords(string words, string nextUnlock) =>
            !string.IsNullOrEmpty(words) ? words
                : "HOTLINE OPEN · PRESS A CALL TO ARM" + (string.IsNullOrEmpty(nextUnlock) ? "" : " · NEXT: " + nextUnlock);

        public static bool StartsNegative(string words) =>
            words != null && words.StartsWith("NEGATIVE", StringComparison.Ordinal);

        /// <summary>The balance change as a signed word for the standing-by box: +9 CR, -140 CR, NO CHANGE.</summary>
        public static string Delta(int delta) =>
            delta == 0 ? "NO CHANGE" : (delta > 0 ? "+" : "-") + Math.Abs(delta).ToString(CultureInfo.InvariantCulture) + " CR";

        /// <summary>Console tone of the controller's words: a refusal is amber, everything else info.</summary>
        public static C2Tone LineTone(string words) => StartsNegative(words) ? C2Tone.Warn : C2Tone.Info;

        private static bool TryCooldownSeconds(string word, out int seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(word) || word[word.Length - 1] != 's') return false;
            return int.TryParse(word.Substring(0, word.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
        }

        private static int Digits(string s)
        {
            int n = 0;
            if (s == null) return 0;
            foreach (char c in s)
            {
                if (c < '0' || c > '9') { if (n > 0) break; continue; }
                n = n * 10 + (c - '0');
                if (n > 99999) break;
            }
            return n;
        }
    }
}
