using System.Collections;
using BoscaliSummer.Features.Support.Visuals;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// Tactical Flare Barrage: launches a high-velocity countermeasure delivery missile that
    /// strikes the designated sector and initiates an intensive 15-second pyrotechnic flare barrage
    /// directly at the impact point, completely seducing and misguiding all IR-seeking missiles.
    /// </summary>
    internal sealed class FlareMissileAction : ISupportAction
    {
        private const float ReleaseAltitude = 6000f;
        private const float ReleaseSpeed = 1200f;

        public float BaseCost(in SupportContext context) =>
            context.Settings.FlareBarrageCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition =
                context.Host.Vanilla.Artillery(context.Settings.ArtilleryDefinitionKey.Value);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning("[Support] Flare Barrage unavailable: no missile definition resolved.");
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

            if (!context.Host.TryReserve(SupportPool.Strike)) return SupportResult.Busy;

            float radius = context.Settings.FlareBarrageRadius.Value;
            int count = context.Settings.FlareBarrageCount.Value;
            float duration = context.Settings.FlareBarrageDuration.Value;
            string unique = SupportNaming.Unique("Flare", context);

            context.Logger.LogInfo($"[Support] Flare Barrage using {definition.jsonKey} onto {ground} (radius: {radius:F0}m, duration: {duration:F0}s).");
            context.Host.Run(Launch(context.Host, context.Player, context.Owner, definition, ground, radius, duration, count, unique));
            return SupportResult.Accepted;
        }

        private static IEnumerator Launch(
            ISupportHost host, Player player, FactionHQ owner, MissileDefinition definition,
            Vector3 ground, float radius, float duration, int flareCount, string unique)
        {
            Missile missile = null;
            Vector3 launchPoint = ground + Vector3.up * ReleaseAltitude;
            Vector3 launchVelocity = Vector3.down * ReleaseSpeed;
            Quaternion launchRot = Quaternion.LookRotation(Vector3.down);

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
                        launchPoint.ToGlobalPosition(),
                        launchRot, owner, string.Empty, guide,
                        launchVelocity, unique);

                    if (missile != null)
                    {
                        missile.SetAimpoint(ground.ToGlobalPosition(), Vector3.zero);
                        missile.Arm();
                        FlareMissileFlightTracker.Track(missile, ground, radius, duration, flareCount);
                    }
                }

                // Wait until missile detonates or timeout
                float timeout = Time.time + (ReleaseAltitude / ReleaseSpeed) + 3.0f;
                while (missile != null && !missile.disabled && Time.time < timeout)
                {
                    yield return null;
                }

                if (missile != null && !missile.disabled)
                {
                    try { missile.Detonate(Vector3.up, false, false); } catch { }
                }
            }
            finally
            {
                host.Release(SupportPool.Strike);
            }
        }
    }

    /// <summary>
    /// Tracks the delivery missile to ensure the flare barrage initiates directly at the impact location.
    /// </summary>
    internal sealed class FlareMissileFlightTracker : MonoBehaviour
    {
        public static void Track(Missile missile, Vector3 targetGround, float radius, float duration, int flareCount)
        {
            if (missile == null) return;
            var tracker = missile.gameObject.AddComponent<FlareMissileFlightTracker>();
            tracker.missile = missile;
            tracker.targetGround = targetGround;
            tracker.Radius = radius;
            tracker.Duration = duration;
            tracker.FlareCount = flareCount;
            tracker.timeout = Time.time + 8.5f;
        }

        public float Radius { get; private set; }
        public float Duration { get; private set; }
        public int FlareCount { get; private set; }

        private Missile missile;
        private Vector3 targetGround;
        private float timeout;
        private bool hasDetonated;

        public void MarkDetonated() => hasDetonated = true;

        private void Update()
        {
            if (hasDetonated) return;

            if (missile == null || missile.disabled || Time.time >= timeout)
            {
                TriggerDetonation();
                return;
            }

            // Proximity terminal trigger if very close to the target ground
            float dist = Vector3.Distance(transform.position, targetGround);
            if (dist <= 35f)
            {
                TriggerDetonation();
            }
        }

        public void TriggerDetonation()
        {
            if (hasDetonated) return;
            hasDetonated = true;

            Vector3 impactPos = transform.position;
            if (impactPos.y < Datum.LocalSeaY + 5f)
            {
                impactPos = new Vector3(impactPos.x, Datum.LocalSeaY + 10f, impactPos.z);
            }

            if (missile != null && !missile.disabled)
            {
                try { missile.Detonate(Vector3.up, false, false); } catch { }
            }

            FlareMissileBurstVisuals.TriggerBarrage(impactPos, Radius, Duration, FlareCount);
        }

        private void OnDestroy()
        {
            hasDetonated = true;
        }
    }
}
