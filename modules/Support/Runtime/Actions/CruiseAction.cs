using System.Collections;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// Cruise salvo: bounded offboard cruise missiles onto the mark. The seeker's native
    /// formation spacing separates same-faction missiles; the HQ registry counts the live
    /// cap. The first missile spawns synchronously so the reply carries its live time of
    /// flight; the rest stagger out of the expiry watch.
    /// </summary>
    internal sealed class CruiseAction : ISupportAction
    {
        private const float ReleaseAltitude = 2500f;
        private const float Standoff = 8000f;
        private const float LateralStep = 400f;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition =
                context.Host.Vanilla.Cruise(context.Settings.CruiseDefinitionKey.Value);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning(
                    "[Support] Cruise is unavailable: no non-nuclear cruise missile definition resolved.");
                return SupportResult.CapabilityUnavailable;
            }
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            Vector3 origin = ground + Vector3.forward * Standoff;
            if (SupportTargeting.TryOrigin(context.Player, out Vector3 player))
            {
                if (Vector3.Distance(player, ground) > context.Settings.MaximumRange.Value)
                    return SupportResult.OutOfRange;
                origin = player;
            }
            if (!SupportTargeting.IntelFreshAt(context.Owner, ground, context.Settings.IntelFreshSeconds.Value, context.Settings.IntelGateRadius.Value))
            {
                context.Logger.LogInfo("[Support] Cruise refused: stale intel at the grid.");
                return SupportResult.StaleIntel;
            }
            int salvo = Mathf.Clamp(context.Settings.CruiseSalvo.Value, 1, 8);
            int live = CountLive(context.Owner);
            if (live + salvo > Mathf.Max(1, context.Settings.CruiseLiveCap.Value))
            {
                context.Logger.LogInfo("[Support] Cruise refused: " + live + " alive, cap " +
                    context.Settings.CruiseLiveCap.Value + ".");
                return SupportResult.RateLimited;
            }
            if (!context.Host.TryReserve(context.Owner, SupportPool.Strike)) return SupportResult.Busy;

            Vector3 approach = Approach(ground, origin);
            Vector3 lateral = new Vector3(-approach.z, 0f, approach.x);
            string unique = SupportNaming.Unique("Cruise", context);
            string guide = context.Player != null && context.Player.Aircraft != null
                ? context.Player.Aircraft.UniqueName
                : string.Empty;
            Missile first = SpawnOne(spawner, definition, context.Owner, guide, ground, approach, lateral,
                0, salvo, unique);
            if (first == null)
            {
                context.Host.Release(context.Owner, SupportPool.Strike);
                return SupportResult.SpawnFailed;
            }
            float tti = StrikeBallistics.TimeOfFlight(
                Vector3.Distance(first.transform.position, ground),
                first.GetTopSpeed(ReleaseAltitude, ground.y));
            context.Host.ReportTti(context.RequestId, tti);
            context.Host.TrackCruiseStrike(context.RequestId, PlayerIdentity.Of(context.Player),
                context.Owner, ground.ToGlobalPosition(), first);
            context.Logger.LogInfo("[Support] Cruise salvo of " + salvo + " released by " +
                definition.jsonKey + (tti >= 0f ? "; time of flight ~" + Mathf.RoundToInt(tti) + " s." : "."));
            context.Host.Run(Finish(context.Host, spawner, definition, context.Owner, guide, ground,
                approach, lateral, salvo, unique, first, context.RequestId));
            return SupportResult.Accepted;
        }

        private static Vector3 Approach(Vector3 target, Vector3 origin)
        {
            Vector3 approach = target - origin;
            approach.y = 0f;
            if (approach.sqrMagnitude < 1f) approach = Vector3.forward;
            approach.Normalize();
            return approach;
        }

        private static Missile SpawnOne(
            Spawner spawner, MissileDefinition definition, FactionHQ owner, string guide,
            Vector3 target, Vector3 approach, Vector3 lateral, int index, int salvo, string unique)
        {
            Vector3 release = target - approach * Standoff + lateral * ((index - (salvo - 1) * 0.5f) * LateralStep);
            release.y = target.y + ReleaseAltitude;
            Missile missile = spawner.SpawnSavedMissile(
                definition.unitPrefab,
                release.ToGlobalPosition(),
                Quaternion.LookRotation(approach), owner, string.Empty, guide,
                approach * 300f, unique + ":" + index);
            if (missile == null) return null;
            missile.SetAimpoint(target.ToGlobalPosition(), Vector3.zero);
            missile.Arm();
            return missile;
        }

        private static int CountLive(FactionHQ owner)
        {
            if (owner == null) return 0;
            List<Missile> registry = owner.GetCruiseMissiles();
            if (registry == null) return 0;
            int live = 0;
            for (int i = 0; i < registry.Count; i++)
                if (registry[i] != null && !registry[i].disabled) live++;
            return live;
        }

        private static IEnumerator Finish(
            ISupportHost host, Spawner spawner, MissileDefinition definition, FactionHQ owner,
            string guide, Vector3 target, Vector3 approach, Vector3 lateral, int salvo,
            string unique, Missile first, int requestId)
        {
            try
            {
                var missiles = new Missile[salvo];
                missiles[0] = first;
                for (int i = 1; i < salvo; i++)
                {
                    yield return new WaitForSeconds(0.8f);
                    if (spawner == null || owner == null) break;
                    missiles[i] = SpawnOne(spawner, definition, owner, guide, target, approach,
                        lateral, i, salvo, unique);
                    if (missiles[i] != null) host.AddCruiseMissile(requestId, missiles[i]);
                }
                float deadline = Time.time + 180f;
                while (Time.time < deadline)
                {
                    bool flying = false;
                    for (int i = 0; i < missiles.Length; i++)
                        flying |= missiles[i] != null && !missiles[i].disabled;
                    if (!flying) break;
                    yield return null;
                }
                for (int i = 0; i < missiles.Length; i++)
                    if (missiles[i] != null && !missiles[i].disabled)
                        spawner.ServerObjectManager.Destroy(missiles[i].gameObject); // Expiry is not an impact.
            }
            finally
            {
                host.Release(owner, SupportPool.Strike);
            }
        }
    }
}
