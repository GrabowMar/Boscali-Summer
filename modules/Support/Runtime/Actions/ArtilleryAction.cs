using System.Collections;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// "Rod from God": one high-velocity projectile.
    /// The shot uses the verified low-yield vanilla missile seam with a small fixed scatter.
    /// </summary>
    internal sealed class ArtilleryAction : ISupportAction
    {
        private const float ReleaseAltitude = 20000f;
        private const float ReleaseSpeed = 2500f;
        private const float RodScatterMeters = 15f;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition =
                context.Host.Vanilla.Artillery(context.Settings.ArtilleryDefinitionKey.Value);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning(
                    "[Support] Rod from God is unavailable: no non-nuclear missile definition resolved.");
                return SupportResult.CapabilityUnavailable;
            }
            if (NetworkSceneSingleton<Spawner>.i == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            // A claimed TASKED call aims at its post's fixed MARK point, already confirmed by the host from a reveal
            // window, so the pilot-range and fresh-intel gates of a free-aimed rod do not apply to it.
            TaskedLaunchJob tasked = context.Tasked;
            if (tasked == null)
            {
                if (SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
                {
                    if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                        return SupportResult.OutOfRange;
                }
                if (!SupportTargeting.IntelFreshAt(context.Owner, ground, context.Settings.IntelFreshSeconds.Value, context.Settings.IntelGateRadius.Value))
                {
                    context.Logger.LogInfo("[Support] Rod from God refused: stale intel at the grid.");
                    return SupportResult.StaleIntel;
                }
            }
            int scene = context.Host.SceneGeneration;
            if (!context.Host.TryReserve(context.Owner, SupportPool.Strike)) return SupportResult.Busy;

            Missile missile = null;
            bool launched = false;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            try
            {
                if (context.SpaceTask == null || !context.SpaceTask.CanLaunch) return SupportResult.UplinkDown;
                if (tasked != null && !tasked.CanLaunch) return SupportResult.UplinkDown;
                // Task 5 swaps this STANDARD scatter for SpaceFireControl.TrySample(tasked.Aim ...) on a confirmed MARK.
                Vector2 miss = Random.insideUnitCircle * RodScatterMeters;
                Vector3 aim = ground + new Vector3(miss.x, 0f, miss.y);
                Vector3 target = SupportTargeting.TryMapPoint(aim.ToGlobalPosition(), out Vector3 scattered)
                    ? scattered : ground;
                string guide = context.Player?.Aircraft != null ? context.Player.Aircraft.UniqueName : string.Empty;
                missile = spawner.SpawnSavedMissile(definition.unitPrefab,
                    (target + Vector3.up * ReleaseAltitude).ToGlobalPosition(), Quaternion.LookRotation(Vector3.down),
                    context.Owner, string.Empty, guide, Vector3.down * ReleaseSpeed, SupportNaming.Unique("Rod", context) + ":0");
                if (missile == null) return SupportResult.SpawnFailed;
                missile.SetAimpoint(target.ToGlobalPosition(), Vector3.zero);
                missile.Arm();
                // The receipt is the only thing that makes this a launch. A refused receipt deletes the spawn below.
                // TASKED settles the board, escrow and bird together; a direct rod commits the bird alone.
                if (tasked != null) { if (!tasked.ReportSpawn()) return SupportResult.SpawnFailed; }
                else if (!context.SpaceTask.ReportPhysicalLaunch()) return SupportResult.UplinkDown;
                launched = true;
                // After the receipt, cosmetic failures cannot refund an already-live physical weapon.
                try { Visuals.KineticRodStrikeVisuals.Track(missile, target); }
                catch (System.Exception e) { context.Logger.LogWarning("[Support] Rod visual unavailable: " + e.Message); }
                try { context.Host.Run(Flight(context.Host, context.Owner, missile, spawner, scene)); }
                catch (System.Exception e)
                {
                    if (context.Host.SceneGeneration == scene) context.Host.Release(context.Owner, SupportPool.Strike);
                    context.Logger.LogWarning("[Support] Native rod launched without cleanup coroutine: " + e.Message);
                }
                return SupportResult.Accepted;
            }
            finally
            {
                if (!launched)
                {
                    try
                    {
                        if (missile != null)
                        {
                            // Verified native disabled state prevents motor, collision, damage and detonation.
                            try
                            {
                                missile.Networkdisabled = true;
                                missile.gameObject.SetActive(false);
                                if (spawner != null && spawner.IsServer && spawner.ServerObjectManager != null)
                                    spawner.ServerObjectManager.Destroy(missile.gameObject);
                            }
                            finally { if (missile != null) UnityEngine.Object.Destroy(missile.gameObject); }
                        }
                    }
                    finally { if (context.Host.SceneGeneration == scene) context.Host.Release(context.Owner, SupportPool.Strike); }
                }
            }
        }

        private static IEnumerator Flight(ISupportHost host, FactionHQ owner, Missile missile, Spawner spawner, int scene)
        {
            try
            {
                float deadline = SupportManager.MissionNow() + 30f;
                while (SupportManager.MissionNow() < deadline && missile != null && !missile.disabled) yield return null;
                if (missile != null && !missile.disabled && spawner != null && spawner.IsServer)
                    spawner.ServerObjectManager.Destroy(missile.gameObject); // Expiry is not an impact.
            }
            finally
            {
                if (host.SceneGeneration == scene) host.Release(owner, SupportPool.Strike);
            }
        }
    }
}
