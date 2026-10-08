using BoscaliSummer.Modules.Support.Domain.Fronts;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// What a front's build-up lets pilots do. A perk of rung n needs <see cref="Readiness"/> >= n; <see cref="Quality"/> scales a perk's radius and
    /// duration (1 = the table value). S1 replaces <see cref="DefaultFrontReadiness"/> with the live front state.
    /// </summary>
    internal interface IFrontReadiness
    {
        /// <summary>1..5.</summary>
        int Readiness(FactionHQ hq, Front front);

        /// <summary>About 0.75..1.25; multiplies radius and duration where an action has them.</summary>
        float Quality(FactionHQ hq, Front front);
    }

    /// <summary>S0 stand-in: every front is fully built and neutral.</summary>
    internal sealed class DefaultFrontReadiness : IFrontReadiness
    {
        public int Readiness(FactionHQ hq, Front front) => 5;
        public float Quality(FactionHQ hq, Front front) => 1f;
    }
}
