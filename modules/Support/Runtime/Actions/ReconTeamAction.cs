using System;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>RECON TEAM: the SOF RECON effect at the aim (reveal of enemy ground units within 2 km for 300 s). No team micromanagement; it needs a live camp.</summary>
    internal sealed class ReconTeamAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) => 1f; // availability flag; the allocation price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                SupportResult result = context.Host.StartSofRecon(context.Owner, context.Target, context.Quality);
                if (result == SupportResult.Accepted)
                    try { Visuals.AreaFx.Sweep(Visuals.AreaFx.At(context.Target), SofService.PerkReconRadius * context.Quality, 6f, Visuals.AreaFx.TeamTint, context.Owner); }
                    catch (Exception e) { context.Logger.LogDebug("[Support] Recon team visual skipped: " + e.Message); }
                return result;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] RECON TEAM failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
