using BoscaliSummer.Modules.Support.Domain.Fronts;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// What a front's standing is worth to a perk. <see cref="Power"/> scales a perk's radius, duration, strikes, damage and odds
    /// (1 = the table value) and shortens its cooldown above 1. Perks unlock by the pilot's skill grade (CallGate), not here.
    /// The live front state replaces <see cref="DefaultFrontPower"/> once the fronts are attached.
    /// </summary>
    internal interface IFrontPower
    {
        /// <summary>0.6 .. 1.5 (<see cref="FrontSuperiority.Power"/>): what the front's superiority is worth to a perk.</summary>
        float Power(FactionHQ hq, Front front);

        /// <summary>The faction's own counts on the front as the host's 5 s world read left them (birds up, nodes held, camps live, teams afield); empty when unknown.</summary>
        FrontSides Facts(FactionHQ hq);
    }

    /// <summary>The stand-in until the fronts attach: neutral POWER, no facts.</summary>
    internal sealed class DefaultFrontPower : IFrontPower
    {
        public float Power(FactionHQ hq, Front front) => 1f;
        public FrontSides Facts(FactionHQ hq) => default;
    }
}
