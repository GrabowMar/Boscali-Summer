using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// Reinforces the garrison of a controlled zone. Crosses into Urban Combat only through
    /// <see cref="IZoneFortificationService"/>, which reports false or zero unless it placed
    /// defenders, so the player is never charged for a fortification that silently did nothing.
    /// </summary>
    internal sealed class FortifyAction : ISupportAction
    {
        private const float MinimumZoneRadius = 650f;

        private readonly IZoneFortificationService fortifications;

        public FortifyAction(IZoneFortificationService service) => fortifications = service;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the allocation price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            if (fortifications == null) return SupportResult.CapabilityUnavailable;

            Vector3 target = context.Target.ToLocalPosition();
            int shells = 1;
            Airbase zone = SupportTargeting.NearestOwnedAirbase(context.Player, target, out float distance);
            if (zone != null && distance <= Mathf.Max(zone.GetRadius() * 1.5f, MinimumZoneRadius))
                return fortifications.TryFortify(zone, context.Owner, context.Player, shells)
                    ? SupportResult.Accepted
                    : SupportResult.SpawnFailed;

            // Outside owned ground there is nothing to fortify.
            return SupportResult.InvalidTarget;
        }
    }
}
