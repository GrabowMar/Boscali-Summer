using BoscaliSummer.Modules.Support.Domain.Calls;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Host verdict to words. Lives in Runtime so the Calls domain never depends on <see cref="SupportResult"/>.</summary>
    internal static class SupportWords
    {
        public static string Refusal(SupportResult result)
        {
            switch (result)
            {
                case SupportResult.Accepted: return "";
                case SupportResult.OutOfCoverage: return "NEGATIVE: OUTSIDE COVERAGE — AIM INSIDE SUPPORT AREA";
                case SupportResult.Disabled: return "NEGATIVE: CALL DISABLED — TRY ANOTHER CALL";
                case SupportResult.NotUnlocked: return CallWords.Refusal(CallRefusal.Locked);
                case SupportResult.InvalidTarget: return "NEGATIVE: UNUSABLE AIM — MOVE THE AIM";
                case SupportResult.NoMarkTarget: return "NEGATIVE: NO CONTACT — MARK A UNIT";
                case SupportResult.StaleIntel: return "NEGATIVE: STALE INTEL — CALL RADAR SCAN FIRST";
                case SupportResult.OutOfRange: return CallWords.Refusal(CallRefusal.OutOfRange);
                case SupportResult.NotAirborne: return "NEGATIVE: NO AIRCRAFT — SPAWN AN AIRCRAFT";
                case SupportResult.InsufficientAllocation: return "NEGATIVE: LOW CREDIT — EARN CR OR TRY A LIGHT CALL";
                case SupportResult.NoStock: return "NEGATIVE: NO STOCK — TRY ANOTHER CALL";
                case SupportResult.Cooldown: return "NEGATIVE: COOLDOWN — STAND BY";
                case SupportResult.Busy:
                case SupportResult.RateLimited: return CallWords.Refusal(CallRefusal.Busy);
                case SupportResult.Duplicate: return "NEGATIVE: ALREADY HANDLED — CHECK THE CALL STATUS";
                case SupportResult.CapabilityUnavailable: return "NEGATIVE: UNAVAILABLE ON THIS MAP — TRY ANOTHER CALL";
                case SupportResult.UplinkDown: return "NEGATIVE: UPLINK DOWN — RESTORE THE SITE";
                case SupportResult.BirdNotReady: return "NEGATIVE: BIRD BUSY — WAIT FOR THE NEXT TASK";
                case SupportResult.SpawnFailed: return "NEGATIVE: DELIVERY FAILED — TRY AGAIN";
                default: return CallWords.Refusal(CallRefusal.Unavailable);
            }
        }
    }
}
