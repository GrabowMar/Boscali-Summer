using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal enum CallRefusal : byte
    {
        None, LowCredit, Cooldown, Locked, FriendliesClose, OutOfRange, NoAim, Offline, Busy, Timeout, Unavailable
    }

    /// <summary>Every refusal says what fixes it.</summary>
    internal static class CallWords
    {
        public static string Refusal(CallRefusal r, int need = 0, int seconds = 0, string unlock = null)
        {
            switch (r)
            {
                case CallRefusal.None: return "";
                case CallRefusal.LowCredit: return "NEGATIVE: LOW ALLOCATION — NEED " + need;
                case CallRefusal.Cooldown: return "NEGATIVE: COOLDOWN — WAIT " + Math.Max(0, seconds) + "s";
                case CallRefusal.Locked: return "NEGATIVE: LOCKED — " + (string.IsNullOrEmpty(unlock) ? "PERK NOT AUTHORISED" : unlock);
                case CallRefusal.FriendliesClose: return "NEGATIVE: FRIENDLIES CLOSE — MOVE THE AIM";
                case CallRefusal.OutOfRange: return "NEGATIVE: OUT OF RANGE — AIM CLOSER";
                case CallRefusal.NoAim: return "NEGATIVE: NO AIM — DESIGNATE OR RIGHT-CLICK MAP";
                case CallRefusal.Offline: return "NEGATIVE: OPS OFFLINE — WAIT FOR THE HOST LINK";
                case CallRefusal.Busy: return "NEGATIVE: LINE BUSY — STAND BY";
                case CallRefusal.Timeout: return "NEGATIVE: NO ANSWER — THE HOST DID NOT REPLY";
                default: return "NEGATIVE: UNAVAILABLE — TRY ANOTHER CALL";
            }
        }
    }
}
