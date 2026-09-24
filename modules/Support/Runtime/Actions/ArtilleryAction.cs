using System.Collections;
using BoscaliSummer.Features.Support.Domain.Orbital;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// "Rod from God": one high-velocity projectile per online magazine, up to loaded rods.
    /// Each shot uses the verified low-yield vanilla missile seam and its own orbit scatter.
    /// </summary>
    internal sealed class ArtilleryAction : ISupportAction
    {
        private const float ReleaseAltitude = 20000f;
        private const float ReleaseSpeed = 2500f;

        public float BaseCost(in SupportContext context) =>
            context.Settings.ArtilleryCost.Value * context.Settings.CostMultiplier.Value;

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
            if (SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
            {
                if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                    return SupportResult.OutOfRange;
            }
            OrbitalPlatform platform = context.PlatformAccess(PlatformAbility.RodStrike, out PlatformDenial denial);
            if (platform == null) return SupportContext.Refusal(denial);
            if (!context.Host.TryReserve(SupportPool.Strike)) return SupportResult.Busy;

            double now = context.Host.OrbitNow;
            int shots = platform.RodSalvoCount(now);
            var targets = new Vector3[shots];
            for (int i = 0; i < shots; i++)
            {
                Vector2 miss = Random.insideUnitCircle * platform.RodScatter(now);
                Vector3 aim = ground + new Vector3(miss.x, 0f, miss.y);
                targets[i] = SupportTargeting.TryMapPoint(aim.ToGlobalPosition(), out Vector3 scattered)
                    ? scattered : ground;
            }
            platform.Consume(PlatformAbility.RodStrike, now, shots);
            context.Logger.LogInfo("[Support] Rod from God: " + shots + " rod(s) released by " +
                                   OrbitalPlatform.Callsign + " using " + definition.jsonKey +
                                   "; " + platform.Rods + " rod(s) left.");
            context.Host.Run(Strike(context.Host, context.Player, context.Owner, definition, targets,
                SupportNaming.Unique("Rod", context)));
            return SupportResult.Accepted;
        }

        private static IEnumerator Strike(
            ISupportHost host, Player player, FactionHQ owner, MissileDefinition definition,
            Vector3[] targets, string unique)
        {
            try
            {
                Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                if (spawner == null || owner == null) yield break;
                string guide = player != null && player.Aircraft != null
                    ? player.Aircraft.UniqueName
                    : string.Empty;
                var missiles = new Missile[targets.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    Vector3 target = targets[i];
                    Missile missile = spawner.SpawnSavedMissile(
                        definition.unitPrefab,
                        (target + Vector3.up * ReleaseAltitude).ToGlobalPosition(),
                        Quaternion.LookRotation(Vector3.down), owner, string.Empty, guide,
                        Vector3.down * ReleaseSpeed, unique + ":" + i);
                    missiles[i] = missile;
                    if (missile != null)
                    {
                        missile.SetAimpoint(target.ToGlobalPosition(), Vector3.zero);
                        missile.Arm();
                        Visuals.KineticRodStrikeVisuals.Track(missile, target);
                    }
                    if (i + 1 < targets.Length) yield return new WaitForSeconds(0.35f);
                }
                float deadline = Time.time + 30f;
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
                        Object.Destroy(missiles[i].gameObject); // Expiry is not an impact.
            }
            finally
            {
                host.Release(SupportPool.Strike);
            }
        }
    }
}
