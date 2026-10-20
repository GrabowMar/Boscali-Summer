namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Engine-free consequences of a wing doctrine. Callers supply the live aircraft.</summary>
    internal static class WingDoctrineRules
    {
        public static float IntervalClose = 0.75f, IntervalOpen = 1.5f;

        /// <summary>INTERVAL's multiple of the formation's spacing preset (the shape still clamps it to its minimum).</summary>
        public static float SpacingScale(FormationInterval interval) =>
            interval == FormationInterval.Close ? IntervalClose : interval == FormationInterval.Open ? IntervalOpen : 1f;

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
