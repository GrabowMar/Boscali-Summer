using System;
using BoscaliSummer.Features.Support.Domain.Orbital;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// ELINT sweep. The faction's station, overhead with a SIGINT array, listens around the
    /// mark; the host reveals enemy ground and ship units whose radar is switched on and
    /// working (at most 48, native tracking RPC, vanilla decay). A radar that is off, broken
    /// or airborne is not heard. The footprint grows with the orbit band and a neighbouring
    /// relay; the sweep spends station energy and starts its recharge.
    /// </summary>
    internal sealed class ElintAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) =>
            context.Settings.ElintCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            OrbitalPlatform platform = context.PlatformAccess(PlatformAbility.Elint, out PlatformDenial denial);
            if (platform == null) return SupportContext.Refusal(denial);

            try
            {
                double now = context.Host.OrbitNow;
                int contacts = ReconAction.Reveal(context.Owner, context.Target,
                    context.Settings.ElintRadius.Value * platform.ElintScale(now), context.Logger, RevealFilter.Emitters);
                platform.Consume(PlatformAbility.Elint, now);
                context.Host.ReportContacts(context.RequestId, contacts);
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] ELINT sweep failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
