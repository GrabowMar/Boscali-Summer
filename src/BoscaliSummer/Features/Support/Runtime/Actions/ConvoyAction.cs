using System;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Requisitions a small ground convoy at the friendly airbase nearest the designated grid.
    /// ponytail: this deliberately does not build a vanilla <c>Faction.ConvoyGroup</c> and hand
    /// it to <c>FactionHQ.AddConvoy</c>. It reuses the already-verified
    /// <c>Spawner.SpawnVehicle</c> seam instead, so it adds no new game API. Ceiling: no convoy
    /// formation, route or escort behaviour — each vehicle behaves exactly like an airdropped
    /// one. Upgrade path: construct a real ConvoyGroup once the spawn point and pathing of
    /// <c>FactionHQ.DeployVehicles</c> have been verified in a live mission.
    /// </summary>
    internal sealed class ConvoyAction : ISupportAction
    {
        private const float Spacing = 25f;

        public float BaseCost(in SupportContext context)
        {
            VehicleDefinition definition = Definition(context);
            if (definition == null) return 0f;
            return definition.value * Count(context) * context.Settings.CostMultiplier.Value;
        }

        public SupportResult Execute(in SupportContext context)
        {
            VehicleDefinition definition = Definition(context);
            if (definition == null || definition.unitPrefab == null)
                return SupportResult.CapabilityUnavailable;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return SupportResult.CapabilityUnavailable;

            Vector3 target = context.Target.ToLocalPosition();
            Airbase muster = SupportTargeting.NearestOwnedAirbase(context.Player, target, out float distance);
            if (muster == null || distance > context.Settings.MaximumRange.Value)
                return SupportResult.InvalidTarget;

            int count = Count(context);
            if (context.Owner.GetUnitSupply(definition) < count) return SupportResult.NoStock;
            if (!context.Host.HasVehicleCapacity(count)) return SupportResult.Busy;

            Vector3 origin = muster.center != null ? muster.center.position : muster.transform.position;
            Vector3 heading = target - origin;
            heading.y = 0f;
            heading = heading.sqrMagnitude > 0.01f ? heading.normalized : Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(heading, Vector3.up);

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 slot = origin - heading * (Spacing * i);
                if (!SupportTargeting.TryGround(slot.ToGlobalPosition(), out Vector3 ground)) continue;
                context.Owner.ModifyUnitSupply(definition, -1);
                try
                {
                    GroundVehicle vehicle = spawner.SpawnVehicle(
                        definition.unitPrefab, ground.ToGlobalPosition(), rotation, Vector3.zero,
                        context.Owner, SupportNaming.Unique("Convoy", context, i), 1f, false, null);
                    if (vehicle == null) throw new InvalidOperationException("Spawner returned no vehicle.");
                    context.Host.TrackVehicle(vehicle);
                    spawned++;
                }
                catch (Exception e)
                {
                    context.Owner.ModifyUnitSupply(definition, 1);
                    context.Logger.LogWarning("[Support] Convoy vehicle failed to spawn: " + e.Message);
                }
            }
            return spawned > 0 ? SupportResult.Accepted : SupportResult.SpawnFailed;
        }

        private static VehicleDefinition Definition(in SupportContext context) =>
            context.Host.Vanilla.Convoy(context.Settings.ConvoyDefinitionKey.Value);

        private static int Count(in SupportContext context) =>
            Mathf.Clamp(context.Settings.ConvoyVehicles.Value, 1, 6);
    }
}
