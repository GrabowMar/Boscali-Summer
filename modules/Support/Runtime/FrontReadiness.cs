using BoscaliSummer.Modules.Support.Domain.Fronts;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// What a front's build-up lets pilots do. A perk of rung n needs <see cref="Readiness"/> >= n; <see cref="Power"/> scales a perk's radius, duration, strikes, damage and odds
    /// (1 = the table value) and shortens its cooldown above 1. S1 replaces <see cref="DefaultFrontReadiness"/> with the live front state.
    /// </summary>
    internal interface IFrontReadiness
    {
        /// <summary>1..5.</summary>
        int Readiness(FactionHQ hq, Front front);

        /// <summary>0.6 .. 1.5 (<see cref="FrontSuperiority.Power"/>): what the front's superiority is worth to a perk.</summary>
        float Power(FactionHQ hq, Front front);

        /// <summary>The faction's own counts on the front as the host's 5 s world read left them (birds up, nodes held, camps live, teams afield); empty when unknown.</summary>
        FrontSides Facts(FactionHQ hq);
    }

    /// <summary>S0 stand-in: every front is fully built and neutral.</summary>
    internal sealed class DefaultFrontReadiness : IFrontReadiness
    {
        public int Readiness(FactionHQ hq, Front front) => 5;
        public float Power(FactionHQ hq, Front front) => 1f;
        public FrontSides Facts(FactionHQ hq) => default;
    }
}
