using System;
using System.Collections;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Parachutes one vanilla ground vehicle onto a designated grid. The same executor serves
    /// the armour and air-defence requisitions — the role is a constructor parameter, not a
    /// second class.
    /// </summary>
    internal sealed class AirdropAction : ISupportAction
    {
        private const float DropAltitude = 550f;
        private const float DropDescent = 12f;
        private const float DropSlotTimeout = 90f;

        private readonly bool airDefence;

        public AirdropAction(bool airDefence) => this.airDefence = airDefence;

        public float BaseCost(in SupportContext context)
        {
            VehicleDefinition definition = Definition(context);
            return definition == null ? 0f : definition.value * context.Settings.CostMultiplier.Value;
        }

        public SupportResult Execute(in SupportContext context)
        {
            VehicleDefinition definition = Definition(context);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning(
                    "[Support] No " + (airDefence ? "air-defence" : "armour") +
                    " vehicle with a parachute system is available for an airdrop.");
                return SupportResult.CapabilityUnavailable;
            }

            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
                return SupportResult.NotAirborne;
            if (!SupportTargeting.TryGround(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                return SupportResult.OutOfRange;
            if (context.Owner.GetUnitSupply(definition) <= 0) return SupportResult.NoStock;
            if (!context.Host.HasVehicleCapacity(1)) return SupportResult.Busy;
            if (!context.Host.TryReserve(SupportPool.Drop)) return SupportResult.Busy;

            context.Owner.ModifyUnitSupply(definition, -1);
            try
            {
                GroundVehicle vehicle = spawner.SpawnVehicle(
                    definition.unitPrefab,
                    (ground + Vector3.up * DropAltitude).ToGlobalPosition(),
                    Quaternion.identity,
                    Vector3.down * DropDescent,
                    context.Owner,
                    SupportNaming.Unique("Drop", context),
                    1f, false, null);
                if (vehicle == null) throw new InvalidOperationException("Spawner returned no vehicle.");
                context.Host.TrackVehicle(vehicle);
                context.Host.Run(ReleaseWhenLanded(context.Host, vehicle));
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Owner.ModifyUnitSupply(definition, 1);
                context.Host.Release(SupportPool.Drop);
                context.Logger.LogWarning("[Support] Airdrop failed and stock was refunded: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }

        private VehicleDefinition Definition(in SupportContext context) => airDefence
            ? context.Host.Vanilla.AirDefence(context.Settings.AirDefenceDefinitionKey.Value)
            : context.Host.Vanilla.Armour(context.Settings.VehicleDefinitionKey.Value);

        /// <summary>
        /// Holds the drop slot until the vehicle is down, gone, or the timeout expires. The
        /// finally block is what stops a stopped coroutine from leaking the slot and locking
        /// every later airdrop into Busy.
        /// </summary>
        private static IEnumerator ReleaseWhenLanded(ISupportHost host, GroundVehicle vehicle)
        {
            try
            {
                float until = Time.unscaledTime + DropSlotTimeout;
                while (vehicle != null && !vehicle.disabled && Time.unscaledTime < until &&
                    vehicle.transform.position.y > Datum.LocalSeaY + 8f)
                    yield return new WaitForSecondsRealtime(1f);
            }
            finally
            {
                host.Release(SupportPool.Drop);
            }
        }
    }
}
