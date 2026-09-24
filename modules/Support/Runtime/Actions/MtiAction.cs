using System;
using BoscaliSummer.Features.Support.Domain.Orbital;

namespace BoscaliSummer.Features.Support.Runtime.Actions
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
        public float BaseCost(in SupportContext context) =>
            context.Settings.MtiCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            if (!VanillaSupportCatalog.ReconAvailable) return SupportResult.CapabilityUnavailable;
            OrbitalPlatform platform = context.PlatformAccess(PlatformAbility.RadarScan, out PlatformDenial denial);
            if (platform == null) return SupportContext.Refusal(denial);

            try
            {
                double now = context.Host.OrbitNow;
                int contacts = ReconAction.Reveal(context.Owner, context.Target,
                    context.Settings.SarSceneRadius.Value * platform.ScanScale(now), context.Logger, RevealFilter.Ground,
                    minimumSpeed: ReconAction.StationaryThreshold);
                platform.Consume(PlatformAbility.RadarScan, now);
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
