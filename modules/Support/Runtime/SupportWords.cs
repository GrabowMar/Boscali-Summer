using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Host verdict to words. Lives in Runtime so the Calls domain never depends on <see cref="SupportResult"/>.</summary>
    internal static class SupportWords
    {
        /// <summary>The geostationary-footprint refusal: the aim lies outside the footprint of the bird the perk needs.</summary>
        public static string OutsideFootprint(string bird) => "TARGET OUTSIDE " + bird + " FOOTPRINT · RELOCATE";

        public static string Refusal(SupportResult result)
        {
            switch (result)
            {
                case SupportResult.Accepted: return "";
                case SupportResult.OutOfCoverage: return "NEGATIVE: " + OutsideFootprint("SATELLITE");
                case SupportResult.Disabled: return "NEGATIVE: CALL DISABLED — TRY ANOTHER CALL";
                case SupportResult.NotUnlocked: return CallWords.Refusal(CallRefusal.Locked);
                case SupportResult.InvalidTarget: return "NEGATIVE: UNUSABLE AIM — MOVE THE AIM";
                case SupportResult.NoMarkTarget: return "NEGATIVE: NO CONTACT — MARK A UNIT";
                case SupportResult.NoCamp: return "NEGATIVE: NO LIVE CAMP — RESTORE A SOF CAMP";
                case SupportResult.NoAnchor: return "NEGATIVE: NO ENEMY ANCHOR NEAR THE AIM — MOVE THE AIM";
                case SupportResult.NeedsReadiness: return "NEGATIVE: FRONT READINESS TOO LOW FOR THIS PERK";
                case SupportResult.OutOfRange: return CallWords.Refusal(CallRefusal.OutOfRange);
                case SupportResult.NotAirborne: return "NEGATIVE: NO AIRCRAFT — SPAWN AN AIRCRAFT";
                case SupportResult.InsufficientAllocation: return "NEGATIVE: LOW ALLOCATION — EARN MORE OR TRY A CHEAPER PERK";
                case SupportResult.NoStock: return "NEGATIVE: NO STOCK — TRY ANOTHER CALL";
                case SupportResult.Cooldown: return "NEGATIVE: COOLDOWN — STAND BY";
                case SupportResult.Busy:
                case SupportResult.RateLimited: return CallWords.Refusal(CallRefusal.Busy);
                case SupportResult.Duplicate: return "NEGATIVE: ALREADY HANDLED — CHECK THE CALL STATUS";
                case SupportResult.CapabilityUnavailable: return "NEGATIVE: UNAVAILABLE ON THIS MAP — TRY ANOTHER CALL";
                case SupportResult.UplinkDown: return "NEGATIVE: UPLINK DOWN — RESTORE THE SITE";
                case SupportResult.BirdNotReady: return "NEGATIVE: BIRD BUSY — WAIT FOR THE NEXT TASK";
                case SupportResult.OpticalNight: return SpaceFeedRules.OpticalRefusal(OpticalVerdict.NightUnavailable);
                case SupportResult.SkyUnknown: return SpaceFeedRules.OpticalRefusal(OpticalVerdict.SkyUnknown);
                case SupportResult.FriendlyNear: return "NEGATIVE: FRIENDLY TOO CLOSE TO THE IMPACT — MOVE THE AIM";
                case SupportResult.SpawnFailed: return "NEGATIVE: DELIVERY FAILED — TRY AGAIN";
                default: return CallWords.Refusal(CallRefusal.Unavailable);
            }
        }
    }
}
