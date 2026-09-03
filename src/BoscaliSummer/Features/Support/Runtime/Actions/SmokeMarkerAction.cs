using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// CAS / Artillery Smoke Designation: Drops a signaling smoke marker at the target coordinates
    /// to visually designate targets, landing zones, or frontline boundaries.
    /// </summary>
    internal sealed class SmokeMarkerAction : ISupportAction
    {
        private readonly IFireSuppressionService fireService;

        public SmokeMarkerAction(IFireSuppressionService fires)
        {
            fireService = fires;
        }

        public float BaseCost(in SupportContext context) =>
            Mathf.Max(30f, context.Settings.FortifyCost.Value * 0.25f);

        public SupportResult Execute(in SupportContext context)
        {
            if (!SupportTargeting.TryGround(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;

            if (SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
            {
                if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                    return SupportResult.OutOfRange;
            }

            if (fireService != null)
            {
                fireService.DeploySmokeMarker(ground.ToGlobalPosition());
            }

            context.Logger.LogInfo($"[Support] Smoke designation marker deployed at {context.Target}.");
            return SupportResult.Accepted;
        }
    }
}
