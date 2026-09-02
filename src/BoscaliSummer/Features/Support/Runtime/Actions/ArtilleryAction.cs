using System.Collections;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// A short salvo of low-yield vanilla ordnance onto the designated grid. Stays default-off
    /// and inert until an explicitly configured non-nuclear definition with yield &lt;= 200
    /// resolves, per the project's artillery gate.
    /// </summary>
    internal sealed class ArtilleryAction : ISupportAction
    {
        private const int Rounds = 4;
        private const float RoundInterval = 1.25f;
        private const float ScatterRadius = 45f;
        private const float ReleaseAltitude = 1000f;
        private const float ReleaseSpeed = 220f;

        public float BaseCost(in SupportContext context) =>
            context.Settings.ArtilleryCost.Value * context.Settings.CostMultiplier.Value;

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition =
                context.Host.Vanilla.Artillery(context.Settings.ArtilleryDefinitionKey.Value);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning(
                    "[Support] Artillery is unavailable: set Support/ArtilleryDefinitionKey to a " +
                    "vanilla missile with a yield of 200 or less.");
                return SupportResult.CapabilityUnavailable;
            }
            if (NetworkSceneSingleton<Spawner>.i == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
                return SupportResult.NotAirborne;
            if (!SupportTargeting.TryGround(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                return SupportResult.OutOfRange;
            if (!context.Host.TryReserve(SupportPool.Artillery)) return SupportResult.Busy;

            context.Host.Run(Salvo(context.Host, context.Player, context.Owner, definition, ground,
                SupportNaming.Unique("Artillery", context)));
            return SupportResult.Accepted;
        }

        private static IEnumerator Salvo(
            ISupportHost host, Player player, FactionHQ owner, MissileDefinition definition,
            Vector3 target, string unique)
        {
            try
            {
                for (int round = 0; round < Rounds; round++)
                {
                    Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                    if (spawner == null || owner == null) yield break;
                    Vector2 scatter = Random.insideUnitCircle * ScatterRadius;
                    Vector3 impact = target + new Vector3(scatter.x, 0f, scatter.y);
                    string guide = player != null && player.Aircraft != null
                        ? player.Aircraft.UniqueName
                        : string.Empty;
                    Missile missile = spawner.SpawnSavedMissile(
                        definition.unitPrefab,
                        (impact + Vector3.up * ReleaseAltitude).ToGlobalPosition(),
                        Quaternion.LookRotation(Vector3.down), owner, string.Empty, guide,
                        Vector3.down * ReleaseSpeed, unique + ":" + round);
                    if (missile != null)
                    {
                        missile.SetAimpoint(impact.ToGlobalPosition(), Vector3.zero);
                        missile.Arm();
                    }
                    yield return new WaitForSecondsRealtime(RoundInterval);
                }
            }
            finally
            {
                host.Release(SupportPool.Artillery);
            }
        }
    }
}
