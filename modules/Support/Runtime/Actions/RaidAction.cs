using System;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// DIRECT ACTION (SOF R5): a ready team (the pilot's own, or one of OVERLORD's) raids the revealed SAM, radar or enemy anchor nearest the aim within 2 km. The raid runs 45 s / POWER on the SOF desk's
    /// SABOTAGE path (<see cref="SofService.PerkDirectAction"/>), then one roll: 0.55 x power + 0.05 per own team afield, 20 % .. 95 %. Success destroys the target; failure wounds the team.
    /// </summary>
    internal sealed class RaidAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) => 1f; // availability flag; the allocation price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                SupportResult result = context.Host.StartDirectAction(context.Player, context.Target, context.Power);
                if (result == SupportResult.Accepted)
                    try { Visuals.AreaFx.Sweep(Visuals.AreaFx.At(context.Target), Domain.Sof.DirectAction.Reach, 6f, Visuals.AreaFx.TeamTint, context.Owner); }
                    catch (Exception e) { context.Logger.LogDebug("[Support] Direct action visual skipped: " + e.Message); }
                return result;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] DIRECT ACTION failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
