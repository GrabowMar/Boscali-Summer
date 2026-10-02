using System.Collections;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// EMP shock: a burst from high altitude that blinds radars across a wide area. The
    /// missile is a delivery visual; the effect is a vanilla <c>Unit.Jam</c> on every hostile
    /// unit in the radius. Friendly units keep their radars.
    /// </summary>
    internal sealed class EmpAction : ISupportAction
    {
        private const float ReleaseAltitude = 15000f;
        private const float ReleaseSpeed = 2000f;
        private const float DischargeDelay = SupportEffectPolicy.EmpDelay;
        private const float JamAmount = 1000f;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition =
                context.Host.Vanilla.Artillery(context.Settings.ArtilleryDefinitionKey.Value);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning("[Support] EMP shock is unavailable: no missile definition resolved.");
                return SupportResult.CapabilityUnavailable;
            }
            if (NetworkSceneSingleton<Spawner>.i == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;

            if (SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
            {
                if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                    return SupportResult.OutOfRange;
            }

            if (!SupportTargeting.IntelFreshAt(context.Owner, ground, context.Settings.IntelFreshSeconds.Value, context.Settings.IntelGateRadius.Value))
            {
                context.Logger.LogInfo("[Support] EMP shock refused: stale intel at the grid.");
                return SupportResult.StaleIntel;
            }
            if (!context.Host.TryReserve(context.Owner, SupportPool.Strike)) return SupportResult.Busy;
            float radius = Mathf.Min(context.Settings.EmpRadius.Value, SupportEffectPolicy.MaxEmpRadius);
            float duration = SupportEffectPolicy.EmpDuration;
            context.Logger.LogInfo("[Support] EMP airburst using " + definition.jsonKey +
                                   " at " + ground.y.ToString("F0") + " m AGL-local, burst +" +
                                   SupportEffectPolicy.EmpBurstAltitude.ToString("F0") + " m");
            // Bound to the ceiling the replicated name can carry, or peers fall back to the default visual.
            context.Host.Run(Discharge(context.Host, context.Player, context.Owner, definition, ground,
                radius, duration, SupportEffectPolicy.EmpName(SupportNaming.Unique("Emp", context), radius)));
            return SupportResult.Accepted;
        }

        private static IEnumerator Discharge(
            ISupportHost host, Player player, FactionHQ owner, MissileDefinition definition,
            Vector3 target, float radius, float duration, string unique)
        {
            Missile missile = null;
            GlobalPosition targetGlobal = target.ToGlobalPosition();
            Vector3 dropPoint = target + Vector3.up * ReleaseAltitude;

            try
            {
                Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                if (spawner != null && owner != null)
                {
                    string guide = player != null && player.Aircraft != null
                        ? player.Aircraft.UniqueName
                        : string.Empty;
                    missile = spawner.SpawnSavedMissile(
                        definition.unitPrefab,
                        dropPoint.ToGlobalPosition(),
                        Quaternion.LookRotation(Vector3.down), owner, string.Empty, guide,
                        Vector3.down * ReleaseSpeed, unique);
                    if (missile != null)
                    {
                        missile.SetAimpoint(target.ToGlobalPosition(), Vector3.zero);
                        missile.Arm();
                    }
                }

                yield return new WaitForSeconds(DischargeDelay);
                target = targetGlobal.ToLocalPosition();

                // The burst is an airburst over the mark, not wherever the delivery
                // missile has wandered. The gamma-deposition band puts the prompt pulse
                // in the 20-40 km stratosphere; the visual and the jam stay at altitude.
                Vector3 burstPoint = target + Vector3.up * SupportEffectPolicy.EmpBurstAltitude;

                if (missile != null && !missile.disabled)
                {
                    missile.transform.position = burstPoint;
                    if (missile.rb != null) missile.rb.position = burstPoint;
                    missile.Detonate(Vector3.up, false, false);
                }


                float radiusSquared = radius * radius;
                float elapsed = 0f;
                int cursor = 0;

                while (elapsed < duration)
                {
                    target = targetGlobal.ToLocalPosition();
                    var units = UnitRegistry.allUnits;
                    if (units != null)
                    {
                        // Fair rolling work bound: crowded missions cannot turn a support pulse into a frame spike.
                        int count = units.Count;
                        int examined = Mathf.Min(128, count);
                        for (int i = 0; i < examined; i++)
                        {
                            if (cursor >= count) cursor = 0;
                            Unit unit = units[cursor++];
                            if (unit == null || unit.disabled) continue;
                            if (unit.NetworkHQ == null || unit.NetworkHQ == owner) continue;

                            float dx = unit.transform.position.x - target.x;
                            float dz = unit.transform.position.z - target.z;
                            if (dx * dx + dz * dz > radiusSquared) continue;
                            if (unit.transform.position.y > Datum.LocalSeaY + 25000f) continue;

                            unit.Jam(new Unit.JamEventArgs
                            {
                                jammingUnit = player != null ? player.Aircraft : null,
                                jamAmount = JamAmount
                            });

                            if (unit is Aircraft ac && elapsed < 0.5f)
                            {
                                Visuals.CockpitEmpDisruption.TriggerForPlayer(ac, 1f);
                            }
                        }
                    }

                    if (elapsed < 0.5f)
                    {
                        Visuals.CockpitEmpDisruption.CheckLocalDisruption(burstPoint, radius, owner);
                    }

                    yield return new WaitForSeconds(0.25f);
                    elapsed += 0.25f;
                }
            }
            finally
            {
                host.Release(owner, SupportPool.Strike);
            }
        }
    }
}
