using System;
using BoscaliSummer.Modules.Support.Domain.Fronts;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// OVERWATCH (SPACE R2): the RADAR bird finds the moving enemy ground units nearest the aim within 3 km x the front's POWER and follows up to round(4 x power) of them on the faction's map for
    /// 60 s x power. The host keeps refreshing their sighting as they move through the same observation path RECON PASS uses (<see cref="SpaceObservations.Follow"/>). No mover near the aim refuses,
    /// and the price goes back with every refusal.
    /// </summary>
    internal sealed class OverwatchAction : ISupportAction
    {
        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                float radius = PerkPower.OverwatchRadius(context.Power);
                SupportResult result = context.Host.StartOverwatch(context.Owner, context.Target, radius, PerkPower.OverwatchUnits(context.Power),
                    PerkPower.OverwatchSeconds(context.Power), out int followed);
                if (result != SupportResult.Accepted) return result;
                context.Host.ReportContacts(context.RequestId, followed);
                try { Visuals.AreaFx.Sweep(Visuals.AreaFx.At(context.Target), radius, 6f, Visuals.AreaFx.ReconTint, context.Owner); }
                catch (Exception e) { context.Logger.LogDebug("[Support] Overwatch sweep visual skipped: " + e.Message); }
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] OVERWATCH failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
