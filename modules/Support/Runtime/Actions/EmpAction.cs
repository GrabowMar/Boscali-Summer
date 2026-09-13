using System.Collections;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// EMP shock: a burst from high altitude that blinds radars across a wide area. The
    /// missile is a delivery visual; the effect is a vanilla <c>Unit.Jam</c> on every unit in
    /// the radius - friendly and hostile alike.
    /// </summary>
    internal sealed class EmpAction : ISupportAction
    {
        private const float ReleaseAltitude = 15000f;
        private const float ReleaseSpeed = 2000f;
        private const float DischargeDelay = SupportEffectPolicy.EmpDelay;
        private const float JamAmount = 1000f;

        public float BaseCost(in SupportContext context) =>
            context.Settings.EmpCost.Value * context.Settings.CostMultiplier.Value;

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

            if (!context.HasCoverage(SatelliteRole.Ew))
                return SupportResult.OutOfCoverage;

            if (!context.Host.TryReserve(SupportPool.Strike)) return SupportResult.Busy;

            context.Logger.LogInfo("[Support] EMP shock using " + definition.jsonKey +
                                   " at " + ground.y.ToString("F0") + " m AGL-local, release +" +
                                   ReleaseAltitude.ToString("F0") + " m");
            context.Host.Run(Discharge(context.Host, context.Player, context.Owner, definition, ground,
                context.Settings.EmpRadius.Value, SupportEffectPolicy.EmpName(SupportNaming.Unique("Emp", context), context.Settings.EmpRadius.Value)));
            return SupportResult.Accepted;
        }

        private static IEnumerator Discharge(
            ISupportHost host, Player player, FactionHQ owner, MissileDefinition definition,
            Vector3 target, float radius, string unique)
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
                // missile has wandered. A terrain-following prefab used to put this on
                // the deck; the visual and the jam stay at altitude either way.
                Vector3 burstPoint = target + Vector3.up * Mathf.Max(4000f, ReleaseAltitude * 0.45f);

                if (missile != null && !missile.disabled)
                {
                    missile.transform.position = burstPoint;
                    if (missile.rb != null) missile.rb.position = burstPoint;
                    missile.Detonate(Vector3.up, false, false);
                }


                float radiusSquared = radius * radius;
                float duration = SupportEffectPolicy.EmpDuration;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    target = targetGlobal.ToLocalPosition();
                    var units = UnitRegistry.allUnits;
                    if (units != null)
                    {
                        for (int i = 0; i < units.Count; i++)
                        {
                            Unit unit = units[i];
                            if (unit == null || unit.disabled) continue;

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
                        Visuals.CockpitEmpDisruption.CheckLocalDisruption(burstPoint, radius);
                    }

                    yield return new WaitForSeconds(0.25f);
                    elapsed += 0.25f;
                }
            }
            finally
            {
                host.Release(SupportPool.Strike);
            }
        }
    }
}
