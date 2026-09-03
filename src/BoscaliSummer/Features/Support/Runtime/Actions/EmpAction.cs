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
        private const float DischargeDelay = 3f;
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
            if (!SupportTargeting.TryGround(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;

            if (SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
            {
                if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                    return SupportResult.OutOfRange;
            }

            if (!context.Host.TryReserve(SupportPool.Strike)) return SupportResult.Busy;

            context.Host.Run(Discharge(context.Host, context.Player, context.Owner, definition, ground,
                context.Settings.EmpRadius.Value, SupportNaming.Unique("Emp", context)));
            return SupportResult.Accepted;
        }

        private static IEnumerator Discharge(
            ISupportHost host, Player player, FactionHQ owner, MissileDefinition definition,
            Vector3 target, float radius, string unique)
        {
            Missile missile = null;
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

                yield return new WaitForSecondsRealtime(DischargeDelay);

                // High-altitude atmospheric EMP detonation point
                Vector3 burstPoint = missile != null
                    ? missile.transform.position
                    : (target + Vector3.up * Mathf.Max(4000f, ReleaseAltitude * 0.45f));

                if (missile != null && !missile.disabled)
                {
                    try { missile.Detonate(Vector3.up, false, false); } catch { }
                }

                // Trigger spectacular custom EMP visual and audio effects
                Visuals.EmpVisualEffect.Trigger(burstPoint, radius);

                var units = UnitRegistry.allUnits;
                if (units != null)
                {
                    float radiusSquared = radius * radius;
                    for (int i = 0; i < units.Count; i++)
                    {
                        Unit unit = units[i];
                        if (unit == null || unit.disabled) continue;
                        if ((unit.transform.position - target).sqrMagnitude > radiusSquared) continue;

                        unit.Jam(new Unit.JamEventArgs
                        {
                            jammingUnit = player != null ? player.Aircraft : null,
                            jamAmount = JamAmount
                        });

                        // Check if this unit is an aircraft and disruption should apply
                        if (unit is Aircraft ac)
                        {
                            Visuals.CockpitEmpDisruption.TriggerForPlayer(ac, 1f);
                        }
                    }
                }

                // Check local player aircraft
                if (GameManager.GetLocalPlayer<Player>(out Player localPlayer) && localPlayer != null && localPlayer.Aircraft != null)
                {
                    if ((localPlayer.Aircraft.transform.position - target).sqrMagnitude <= radius * radius)
                    {
                        Visuals.CockpitEmpDisruption.TriggerForPlayer(localPlayer.Aircraft, 1.2f);
                    }
                }
            }
            finally
            {
                host.Release(SupportPool.Strike);
            }
        }
    }
}
