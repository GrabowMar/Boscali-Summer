using System.Collections;
using BepInEx.Logging;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// "Rod from God": one high-velocity projectile.
    /// The shot uses the verified low-yield vanilla missile seam. A free-aimed (STANDARD) rod lands within a 120 m CEP of the
    /// pick; a claimed TASKED rod lands on its confirmed MARK (25 m, SAR-only 30 m). Both refuse to land near a friendly player.
    /// A TASKED rod warns the target faction at least 15 s before impact: it waits out max(0, 15 - flight) in a short
    /// host dwell, then revalidates and releases.
    /// </summary>
    internal sealed class ArtilleryAction : ISupportAction
    {
        private const float DwellCheckSeconds = .5f;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        /// <summary>Everything one launch needs, copied out of the context so a delayed launch can outlive the call.</summary>
        private sealed class Shot
        {
            public ISupportHost Host;
            public ManualLogSource Logger;
            public FactionHQ Owner;
            public MissileDefinition Definition;
            public SpaceActionTransaction SpaceTask;
            public TaskedLaunchJob Tasked;
            public GlobalPosition Impact;
            public string Name, Guide;
            public int Scene;
            public bool Warned;
            private bool released;

            /// <summary>Releases the host's Strike pool slot exactly once, and only inside the scene that took it.</summary>
            public void ReleasePool()
            {
                if (released) return;
                released = true;
                if (Host.SceneGeneration == Scene) Host.Release(Owner, SupportPool.Strike);
            }
        }

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
            // The impact is chosen once: TASKED from its fixed confirmed MARK, STANDARD with the 120 m CEP around the pick.
            // Aimed in global coordinates so a floating-origin shift during a dwell cannot move it.
            SpaceImpact impact;
            bool sampled = tasked != null
                ? SpaceFireControl.TrySampleAim(tasked.Aim, Random.value, Random.value, out impact)
                : SpaceFireControl.TrySample(context.Target.x, context.Target.z, false, false, Random.value, Random.value, out impact);
            if (!sampled) return SupportResult.InvalidTarget;
            if (!SupportTargeting.TryMapPoint(new GlobalPosition(impact.X, context.Target.y, impact.Z), out Vector3 landing))
                return SupportResult.InvalidTarget;
            GlobalPosition impactPoint = landing.ToGlobalPosition();

            int scene = context.Host.SceneGeneration;
            if (!context.Host.TryReserve(context.Owner, SupportPool.Strike)) return SupportResult.Busy;

            var shot = new Shot
            {
                Host = context.Host, Logger = context.Logger, Owner = context.Owner, Definition = definition,
                SpaceTask = context.SpaceTask, Tasked = tasked, Impact = impactPoint, Scene = scene,
                Name = SupportNaming.Unique("Rod", context) + ":0",
                Guide = context.Player?.Aircraft != null ? context.Player.Aircraft.UniqueName : string.Empty
            };
            bool poolOwned = true; // until a launch or a dwell takes over releasing the slot
            try
            {
                if (context.SpaceTask == null || !context.SpaceTask.CanLaunch) return SupportResult.UplinkDown;
                if (tasked != null && !tasked.CanLaunch) return TaskedRefusal(tasked);
                // Refused before anything is warned or spent: no friendly may be inside the standoff of the impact.
                if (RodGuard.FriendlyNear(context.Owner, impactPoint))
                {
                    context.Logger.LogInfo("[Support] Rod from God refused: a friendly player is inside the standoff.");
                    return SupportResult.FriendlyNear;
                }
                if (tasked != null)
                {
                    float now = SupportManager.MissionNow();
                    RodSchedule plan = RodTiming.Plan(now, RodGuard.Flight.Seconds);
                    if (!plan.Valid) return SupportResult.SpawnFailed;
                    int told = RodGuard.Warn(context.Owner, RodGuard.InboundText(plan.LeadSeconds));
                    shot.Warned = true;
                    context.Logger.LogInfo("[Support] Rod warning sent to " + told + " faction(s): lead " + plan.LeadSeconds.ToString("0.0") +
                        " s, dwell " + (plan.LaunchAt - now).ToString("0.0") + " s, flight estimate " + RodGuard.Flight.Seconds.ToString("0.0") + " s.");
                    if (plan.LaunchAt > now)
                    {
                        // The job stays exclusive (LAUNCHING), the escrow stays refundable and the bird stays reserved while the dwell runs.
                        if (!tasked.Defer()) return CancelWarned(shot, SupportResult.SpawnFailed);
                        try { context.Host.Run(Dwell(shot, plan.LaunchAt)); }
                        catch (System.Exception e)
                        {
                            context.Logger.LogWarning("[Support] Rod dwell could not start: " + e.Message);
                            return CancelWarned(shot, SupportResult.SpawnFailed);
                        }
                        poolOwned = false; // the dwell routine releases the slot from here
                        return SupportResult.Accepted;
                    }
                }
                poolOwned = false;
                SupportResult result = Release(shot);
                return result == SupportResult.Accepted ? result : CancelWarned(shot, result);
            }
            finally { if (poolOwned) shot.ReleasePool(); }
        }

        /// <summary>A launch that fails after the warning went out tells the warned factions the strike is off.</summary>
        private static SupportResult CancelWarned(Shot shot, SupportResult result)
        {
            if (shot.Warned && shot.Host.SceneGeneration == shot.Scene) RodGuard.Warn(shot.Owner, RodGuard.CancelledText);
            return result;
        }

        private static SupportResult TaskedRefusal(TaskedLaunchJob job)
        {
            switch (job.WhyNot)
            {
                case TaskedOutcome.MarkExpired:
                case TaskedOutcome.TimedOut: return SupportResult.InvalidTarget;
                default: return SupportResult.UplinkDown;
            }
        }

        /// <summary>
        /// The physical launch: every gate is checked again immediately before the native spawn, and nothing but a refused or
        /// absent spawn can leave the pool slot held. Owns the pool slot from here on.
        /// </summary>
        private static SupportResult Release(Shot shot)
        {
            Missile missile = null;
            bool launched = false;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            try
            {
                if (spawner == null) return SupportResult.CapabilityUnavailable;
                if (shot.Host.SceneGeneration != shot.Scene) return SupportResult.SpawnFailed;
                if (shot.SpaceTask == null || !shot.SpaceTask.CanLaunch) return SupportResult.UplinkDown;
                if (shot.Tasked != null && !shot.Tasked.CanLaunch) return TaskedRefusal(shot.Tasked);
                if (RodGuard.FriendlyNear(shot.Owner, shot.Impact))
                {
                    shot.Logger.LogInfo("[Support] Rod from God cancelled before release: a friendly player entered the standoff.");
                    return SupportResult.FriendlyNear;
                }
                Vector3 target = shot.Impact.ToLocalPosition();
                missile = spawner.SpawnSavedMissile(shot.Definition.unitPrefab,
                    (target + Vector3.up * RodTiming.ReleaseAltitude).ToGlobalPosition(), Quaternion.LookRotation(Vector3.down),
                    shot.Owner, string.Empty, shot.Guide, Vector3.down * RodTiming.ReleaseSpeed, shot.Name);
                if (missile == null) return SupportResult.SpawnFailed;
                missile.SetAimpoint(shot.Impact, Vector3.zero);
                missile.Arm();
                // The receipt is the only thing that makes this a launch. A refused receipt deletes the spawn below.
                // TASKED settles the board, escrow and bird together; a direct rod commits the bird alone.
                if (shot.Tasked != null) { if (!shot.Tasked.ReportSpawn()) return SupportResult.SpawnFailed; }
                else if (!shot.SpaceTask.ReportPhysicalLaunch()) return SupportResult.UplinkDown;
                launched = true;
                // After the receipt, cosmetic failures cannot refund an already-live physical weapon.
                try { Visuals.KineticRodStrikeVisuals.Track(missile, target); }
                catch (System.Exception e) { shot.Logger.LogWarning("[Support] Rod visual unavailable: " + e.Message); }
                try { shot.Host.Run(Flight(shot, missile, spawner)); }
                catch (System.Exception e)
                {
                    shot.ReleasePool();
                    shot.Logger.LogWarning("[Support] Native rod launched without cleanup coroutine: " + e.Message);
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
                    finally { shot.ReleasePool(); }
                }
            }
        }

        /// <summary>
        /// The prelaunch dwell of a warned TASKED rod, counted in mission time. Every half second the launch is revalidated
        /// (scene, job, bird, MARK deadline, friendly standoff); any failure refunds through the job and cancels the warning.
        /// </summary>
        private static IEnumerator Dwell(Shot shot, float launchAt)
        {
            TaskedLaunchJob job = shot.Tasked;
            SupportResult result = SupportResult.SpawnFailed;
            bool released = false;
            try
            {
                float nextCheck = 0f;
                bool abort = false;
                while (!abort && SupportManager.MissionNow() < launchAt)
                {
                    float now = SupportManager.MissionNow();
                    if (now >= nextCheck)
                    {
                        nextCheck = now + DwellCheckSeconds;
                        if (shot.Host.SceneGeneration != shot.Scene || job.Final) abort = true;
                        else if (!job.CanLaunch) { result = TaskedRefusal(job); abort = true; }
                        else if (RodGuard.FriendlyNear(shot.Owner, shot.Impact)) { result = SupportResult.FriendlyNear; abort = true; }
                    }
                    if (!abort) yield return null;
                }
                if (!abort)
                {
                    released = true; // Release owns the pool slot from here
                    result = Release(shot);
                }
            }
            finally
            {
                if (!released) shot.ReleasePool();
                if (result != SupportResult.Accepted) AbortDwell(shot, job, result);
            }
        }

        private static void AbortDwell(Shot shot, TaskedLaunchJob job, SupportResult result)
        {
            try
            {
                CancelWarned(shot, result);
                if (job.Final) return;
                TaskedOutcome why = result == SupportResult.FriendlyNear ? TaskedOutcome.FriendlyNear : job.WhyNot;
                job.ReportFailure(why == TaskedOutcome.None ? TaskedOutcome.DeliveryFailed : why);
            }
            catch (System.Exception e) { shot.Logger.LogWarning("[Support] Rod dwell cleanup failed: " + e.Message); }
        }

        private static IEnumerator Flight(Shot shot, Missile missile, Spawner spawner)
        {
            try
            {
                float launched = SupportManager.MissionNow();
                float deadline = launched + 30f;
                while (SupportManager.MissionNow() < deadline && missile != null && !missile.disabled) yield return null;
                if (missile != null && !missile.disabled && spawner != null && spawner.IsServer)
                    spawner.ServerObjectManager.Destroy(missile.gameObject); // Expiry is not an impact.
                else if (shot.Host.SceneGeneration == shot.Scene) // a scene teardown is not a landing
                {
                    float flight = SupportManager.MissionNow() - launched;
                    if (RodGuard.Flight.Record(flight))
                        shot.Logger.LogInfo("[Support] Rod flight measured: " + flight.ToString("0.0") +
                            " s (planning estimate now " + RodGuard.Flight.Seconds.ToString("0.0") + " s).");
                }
            }
            finally { shot.ReleasePool(); }
        }
    }
}
