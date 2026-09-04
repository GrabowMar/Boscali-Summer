using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Reinforces the garrison of a controlled zone. Crosses into Urban Combat only through
    /// <see cref="IZoneFortificationService"/>, which now returns false unless it has verified
    /// it can actually place defenders — so the player is never charged for a fortification
    /// that silently did nothing.
    /// </summary>
    internal sealed class FortifyAction : ISupportAction
    {
        private const float MinimumZoneRadius = 650f;

        private readonly IZoneFortificationService fortifications;

        public FortifyAction(IZoneFortificationService service) => fortifications = service;

        public float BaseCost(in SupportContext context) =>
            context.Settings.FortifyCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            if (fortifications == null) return SupportResult.CapabilityUnavailable;

            Vector3 target = context.Target.ToLocalPosition();
            Airbase zone = SupportTargeting.NearestOwnedAirbase(context.Player, target, out float distance);
            if (zone == null || distance > Mathf.Max(zone.GetRadius() * 1.5f, MinimumZoneRadius))
                return SupportResult.InvalidTarget;

            return fortifications.TryFortify(zone, context.Owner, context.Player)
                ? SupportResult.Accepted
                : SupportResult.SpawnFailed;
        }
    }
}
