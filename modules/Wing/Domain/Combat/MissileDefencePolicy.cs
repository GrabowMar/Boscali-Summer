using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    internal static class MissileDefencePolicy
    {
        /// <summary>Saturation only yields inside the existing defensive reaction window, or to a
        /// close missile whose velocity may still be changing rapidly after launch.</summary>
        public static bool InterruptsSaturation(float distance, float closingSpeed) =>
            distance <= 1000f || (closingSpeed > 0f && distance / closingSpeed <= WingTuning.ChaffWindowSeconds);

        public static bool PreferInterception(MissileResponse response, MissileGuard guard, string seeker,
            bool armed, float impactSeconds, bool hasCover, float playerDistance, float leashRadius) =>
            WingDoctrineRules.PreferInterception(response, guard, seeker, armed, impactSeconds,
                hasCover, playerDistance, leashRadius);
    }
}
