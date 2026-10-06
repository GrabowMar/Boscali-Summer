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
    /// <summary>Engine-free consequences of a wing doctrine. Callers supply the live aircraft.</summary>
    internal static class WingDoctrineRules
    {

        public static float EngageRange(EngagementReach reach) =>
            reach == EngagementReach.Long ? WingTuning.ReachLongMetres : WingTuning.ReachSlotMetres;

        public static DoctrineAllow StandingAllow(TargetPolicy targets)
        {
            switch (targets)
            {
                case TargetPolicy.Air: return DoctrineAllow.AirOnly;
                case TargetPolicy.Ground: return DoctrineAllow.GroundOnly;
                case TargetPolicy.Both: return DoctrineAllow.AirAndGround;
                default: return DoctrineAllow.None;
            }
        }
    }
}
