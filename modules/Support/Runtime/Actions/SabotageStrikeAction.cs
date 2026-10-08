using System;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>SABOTAGE STRIKE: the SOF SABOTAGE effect on the nearest enemy anchor within 1 km of the aim. It needs a live camp.</summary>
    internal sealed class SabotageStrikeAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) => 1f; // availability flag; the allocation price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                var before = new System.Collections.Generic.List<Unit>(4);
                Visuals.StagedBlastFx.DownAnchors(context.Owner, context.Target, before);
                SupportResult result = context.Host.StartSofSabotage(context.Owner, context.Target);
                if (result == SupportResult.Accepted)
                    try { Visuals.StagedBlastFx.PlayOnNewlyDown(context.Owner, context.Target, before); }
                    catch (Exception e) { context.Logger.LogDebug("[Support] Sabotage visual skipped: " + e.Message); }
                return result;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] SABOTAGE STRIKE failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
