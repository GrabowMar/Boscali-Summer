using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Reinforces the garrison of a controlled zone, or — inside a SPEC OPS safehouse's reach —
    /// occupies buildings around the mark in any ground. Crosses into Urban Combat only through
    /// <see cref="IZoneFortificationService"/>, which reports false or zero unless it placed
    /// defenders, so the player is never charged for a fortification that silently did nothing.
    /// The detachment's best team rank sets how many positions one order occupies.
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
            SpecOpsDetachment detachment = context.Host.Space.DetachmentFor(context.Owner);
            int shells = detachment != null ? detachment.GroundReadiness : 1;
            Airbase zone = SupportTargeting.NearestOwnedAirbase(context.Player, target, out float distance);
            if (zone != null && distance <= Mathf.Max(zone.GetRadius() * 1.5f, MinimumZoneRadius))
                return fortifications.TryFortify(zone, context.Owner, context.Player, shells)
                    ? SupportResult.Accepted
                    : SupportResult.SpawnFailed;

            // Outside owned ground only a held safehouse lets the order through.
            GlobalPosition mark = context.Target;
            if (detachment == null || !detachment.Enabled ||
                detachment.Covering(FieldMission.Seize, mark.x, mark.z) < 0)
                return SupportResult.InvalidTarget;
            return fortifications.TrySeize(mark.x, mark.z, FieldCatalog.SeizeRadius, context.Owner, shells) > 0
                ? SupportResult.Accepted
                : SupportResult.SpawnFailed;
        }
    }
}
