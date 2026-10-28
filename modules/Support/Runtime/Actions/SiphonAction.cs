using System;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// SIPHON (CYBER R3): funds move from the strongest enemy's front budgets into the faction's CYBER budget, (40 + 15 per enemy node held) x the front's POWER at most, capped by what that enemy has.
    /// Host-only economics in <see cref="FrontService.Siphon"/>; the aim is ignored. Under the minimum it refuses (NOTHING TO SIPHON) and the price goes back.
    /// </summary>
    internal sealed class SiphonAction : ISupportAction
    {
        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                SupportResult result = context.Host.StartSiphon(context.Owner, context.Power, out int taken);
                if (result != SupportResult.Accepted) return result;
                context.Host.ReportContacts(context.RequestId, taken);
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] SIPHON failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
