using System;
using BoscaliSummer.Modules.Support.Runtime;

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
                case CallRefusal.Cooldown: return "NEGATIVE: COOLDOWN — WAIT " + Math.Max(0, seconds) + "s";
                case CallRefusal.Locked: return "NEGATIVE: LOCKED — " + (string.IsNullOrEmpty(unlock) ? "HOLD MORE GROUND" : unlock);
                case CallRefusal.FriendliesClose: return "NEGATIVE: FRIENDLIES CLOSE — MOVE THE AIM";
                case CallRefusal.OutOfRange: return "NEGATIVE: OUT OF RANGE — AIM CLOSER";
                case CallRefusal.NoAim: return "NEGATIVE: NO AIM — DESIGNATE OR RIGHT-CLICK MAP";
                case CallRefusal.Offline: return "NEGATIVE: OPS OFFLINE — WAIT FOR THE HOST LINK";
                case CallRefusal.Busy: return "NEGATIVE: LINE BUSY — STAND BY";
                case CallRefusal.Timeout: return "NEGATIVE: NO ANSWER — WAIT FOR HOST BALANCE";
                case CallRefusal.Frozen:
                    return "NEGATIVE: CREDIT FROZEN " + (int)Math.Ceiling(seconds / 60f) + " MIN — STAND BY";
                default: return "NEGATIVE: UNAVAILABLE — TRY ANOTHER CALL";
            }
        }

        public static string Refusal(SupportResult result)
        {
            switch (result)
            {
                case SupportResult.Accepted: return "";
                case SupportResult.OutOfCoverage: return "NEGATIVE: OUTSIDE COVERAGE — AIM INSIDE SUPPORT AREA";
                case SupportResult.Disabled: return "NEGATIVE: CALL DISABLED — TRY ANOTHER CALL";
                case SupportResult.NotUnlocked: return Refusal(CallRefusal.Locked);
                case SupportResult.InvalidTarget: return "NEGATIVE: UNUSABLE AIM — MOVE THE AIM";
                case SupportResult.NoMarkTarget: return "NEGATIVE: NO CONTACT — MARK A UNIT";
                case SupportResult.StaleIntel: return "NEGATIVE: STALE INTEL — CALL RADAR SCAN FIRST";
                case SupportResult.OutOfRange: return Refusal(CallRefusal.OutOfRange);
                case SupportResult.NotAirborne: return "NEGATIVE: NO AIRCRAFT — SPAWN AN AIRCRAFT";
                case SupportResult.InsufficientAllocation: return "NEGATIVE: LOW CREDIT — EARN CR OR TRY A LIGHT CALL";
                case SupportResult.NoStock: return "NEGATIVE: NO STOCK — TRY ANOTHER CALL";
                case SupportResult.Cooldown: return "NEGATIVE: COOLDOWN — STAND BY";
                case SupportResult.Busy:
                case SupportResult.RateLimited: return Refusal(CallRefusal.Busy);
                case SupportResult.Duplicate: return "NEGATIVE: ALREADY HANDLED — CHECK THE CALL STATUS";
                case SupportResult.CapabilityUnavailable: return "NEGATIVE: UNAVAILABLE ON THIS MAP — TRY ANOTHER CALL";
                case SupportResult.SpawnFailed: return "NEGATIVE: DELIVERY FAILED — TRY AGAIN";
                default: return Refusal(CallRefusal.Unavailable);
            }
        }

        public static string TierWord(CallTier tier) =>
            tier == CallTier.Strategic ? "STRATEGIC" : tier == CallTier.Heavy ? "HEAVY" : "LIGHT";
    }
}
