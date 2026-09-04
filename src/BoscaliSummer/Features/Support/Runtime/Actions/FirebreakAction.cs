using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Combat Engineering / Firebreak Support: clears procedural forest and suppresses
    /// active fire sites without spawning synthetic fortification geometry.
    /// </summary>
    internal sealed class FirebreakAction : ISupportAction
    {
        private const float FirebreakRadius = 150f;

        private readonly IFireSuppressionService fireSuppression;

        public FirebreakAction(IFireSuppressionService fires) => fireSuppression = fires;

        public float BaseCost(in SupportContext context) =>
            context.Settings.FortifyCost.Value * 0.75f * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            if (!SupportTargeting.TryGround(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;

            if (SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
            {
                if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                    return SupportResult.OutOfRange;
            }

            if (fireSuppression != null)
            {
                fireSuppression.ExtinguishInRadius(context.Target, FirebreakRadius);
                fireSuppression.ClearForestInRadius(context.Target, FirebreakRadius);
            }

            context.Logger.LogInfo($"[Support] Firebreak established at {context.Target} (cleared radius {FirebreakRadius}m).");
            return SupportResult.Accepted;
        }
    }
}
