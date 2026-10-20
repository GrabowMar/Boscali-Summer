using System;
using BoscaliSummer.Modules.Support.Domain.Fronts;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// RADAR BLIND and SAM NET DOWN: the CYBER package effect (JAM RADAR / SAM NET DOWN) started at the aim for the pilot's faction
    /// against every other faction. Radius and duration follow the CYBER front's POWER and a jam deepens with it; SAM NET DOWN also lasts 15 % longer for every enemy node the faction holds (at most x1.6).
    /// Needs the faction's CYBER desk.
    /// </summary>
    internal sealed class CyberPerkAction : ISupportAction
    {
        private readonly SupportActionId package;

        public CyberPerkAction(SupportActionId package) => this.package = package;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the allocation price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                float bonus = package == SupportActionId.CyberSamNetDown ? PerkPower.SamNetDuration(1f, context.Host.Facts(context.Owner).HeldEnemyNodes) : 1f;
                if (!context.Host.StartCyberPerk(context.Owner, package, context.Target, context.Power, bonus)) return SupportResult.CapabilityUnavailable;
                try
                {
                    if (Domain.Cyber.CyberPackages.TryOfAction(package, out Domain.Cyber.PackageDef def))
                        Visuals.AreaFx.Jam(Visuals.AreaFx.At(context.Target), def.Radius * context.Power, def.Seconds * context.Power * bonus,
                            package == SupportActionId.CyberSamNetDown ? Visuals.AreaFx.SamTint : Visuals.AreaFx.BlindTint, context.Owner);
                }
                catch (Exception e) { context.Logger.LogDebug("[Support] Jam dome visual skipped: " + e.Message); }
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] CYBER perk failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
