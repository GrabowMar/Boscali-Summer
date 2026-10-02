using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal enum CallRefusal : byte
    {
        None, LowCredit, Cooldown, Locked, FriendliesClose, OutOfRange, NoAim, Offline, Busy, Timeout, Unavailable, Frozen
    }

    /// <summary>Core §2a: every refusal says what fixes it.</summary>
    internal static class CallWords
    {
        public static string Refusal(CallRefusal r, int need = 0, int seconds = 0, string unlock = null)
        {
            switch (r)
            {
                case CallRefusal.None: return "";
                case CallRefusal.LowCredit: return "NEGATIVE: LOW CREDIT — NEED " + need + " CR";
                case CallRefusal.Cooldown: return "NEGATIVE: COOLDOWN " + seconds + "s";
                case CallRefusal.Locked: return "NEGATIVE: LOCKED — " + (string.IsNullOrEmpty(unlock) ? "HOLD MORE GROUND" : unlock);
                case CallRefusal.FriendliesClose: return "NEGATIVE: FRIENDLIES CLOSE — MOVE THE AIM";
                case CallRefusal.OutOfRange: return "NEGATIVE: OUT OF RANGE — AIM CLOSER";
                case CallRefusal.NoAim: return "NEGATIVE: NO AIM — DESIGNATE OR RIGHT-CLICK MAP";
                case CallRefusal.Offline: return "NEGATIVE: OPS OFFLINE — WAIT FOR THE HOST LINK";
                case CallRefusal.Busy: return "NEGATIVE: LINE BUSY — STAND BY";
                case CallRefusal.Timeout: return "NEGATIVE: NO ANSWER — CREDIT RETURNED";
                case CallRefusal.Frozen:
                    return "NEGATIVE: CREDIT FROZEN " + (int)Math.Ceiling(seconds / 60f) + " MIN — NEW FACTION";
                default: return "NEGATIVE: UNAVAILABLE — TRY ANOTHER CALL";
            }
        }

        public static string TierWord(CallTier tier) =>
            tier == CallTier.Strategic ? "STRATEGIC" : tier == CallTier.Heavy ? "HEAVY" : "LIGHT";
    }
}
