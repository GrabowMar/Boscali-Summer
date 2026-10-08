using System;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// RADAR BLIND and SAM NET DOWN: the CYBER package effect (JAM RADAR / SAM NET DOWN) started at the aim for the pilot's faction
    /// against every other faction. Radius and duration follow the CYBER front quality. Needs the faction's CYBER desk.
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
                if (!context.Host.StartCyberPerk(context.Owner, package, context.Target, context.Quality)) return SupportResult.CapabilityUnavailable;
                try
                {
                    if (Domain.Cyber.CyberPackages.TryOfAction(package, out Domain.Cyber.PackageDef def))
                        Visuals.AreaFx.Jam(Visuals.AreaFx.At(context.Target), def.Radius * context.Quality, def.Seconds * context.Quality,
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
