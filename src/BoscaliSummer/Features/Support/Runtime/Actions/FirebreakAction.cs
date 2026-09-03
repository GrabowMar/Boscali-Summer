using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Combat Engineering / Firebreak Support: Clears procedural forest, suppresses active fire sites,
    /// and erects physical concrete barrier revetments to stop advancing fire fronts.
    /// Fully abstracted via Framework.Contracts to adhere to module architecture boundaries.
    /// </summary>
    internal sealed class FirebreakAction : ISupportAction
    {
        private const float FirebreakRadius = 150f;

        private readonly IFireSuppressionService fireSuppression;
        private readonly IZoneFortificationService fortifications;

        public FirebreakAction(IFireSuppressionService fires, IZoneFortificationService forts)
        {
            fireSuppression = fires;
            fortifications = forts;
        }

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

            // 1. Extinguish active fires and clear flammable trees in radius
            if (fireSuppression != null)
            {
                fireSuppression.ExtinguishInRadius(context.Target, FirebreakRadius);
                fireSuppression.ClearForestInRadius(context.Target, FirebreakRadius);
            }

            // 2. Deploy physical barrier revetments to visually establish the firebreak perimeter
            if (fortifications != null)
            {
                fortifications.TryDeployFirebreak(ground, FirebreakRadius);
            }

            context.Logger.LogInfo($"[Support] Firebreak established at {context.Target} (cleared radius {FirebreakRadius}m).");
            return SupportResult.Accepted;
        }
    }
}
