using System;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// MTI sweep. The faction's station, overhead with a spy imager, runs its radar in
    /// moving-target-indication mode around the mark; the host reveals hostile ground units
    /// moving faster than the SAR smear threshold (at most 48, native tracking RPC, vanilla
    /// decay). Stationary contacts blend into the ground return and are not heard — that is
    /// the RADAR SCAN's half of the scene. One radar, two modes: MTI spends the same
    /// tasking as RADAR SCAN, so the operator picks one per recharge window.
    /// </summary>
    internal sealed class MtiAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                int contacts = ReconAction.Reveal(context.Owner, context.Target,
                    context.Settings.SarSceneRadius.Value, context.Logger, RevealFilter.Ground,
                    minimumSpeed: ReconAction.StationaryThreshold);
                context.Host.ReportContacts(context.RequestId, contacts);
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] MTI sweep failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
