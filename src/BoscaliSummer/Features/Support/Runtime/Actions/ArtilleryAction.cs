using System.Collections;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    /// <summary>
    /// "Rod from God": one high-velocity kinetic projectile dropped from high altitude onto
    /// the designated grid. Reuses the verified low-yield vanilla missile seam - the rod is
    /// a single very fast shot, not a salvo.
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
            if (!context.Host.TryReserve(SupportPool.Strike)) return SupportResult.Busy;

            context.Logger.LogInfo("[Support] Rod from God using " + definition.jsonKey);
            context.Host.Run(Strike(context.Host, context.Player, context.Owner, definition, ground,
                SupportNaming.Unique("Rod", context)));
            return SupportResult.Accepted;
        }

        private static IEnumerator Strike(
            ISupportHost host, Player player, FactionHQ owner, MissileDefinition definition,
            Vector3 target, string unique)
        {
            try
            {
                Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                if (spawner == null || owner == null) yield break;
                string guide = player != null && player.Aircraft != null
                    ? player.Aircraft.UniqueName
                    : string.Empty;
                Missile missile = spawner.SpawnSavedMissile(
                    definition.unitPrefab,
                    (target + Vector3.up * ReleaseAltitude).ToGlobalPosition(),
                    Quaternion.LookRotation(Vector3.down), owner, string.Empty, guide,
                    Vector3.down * ReleaseSpeed, unique);
                if (missile != null)
                {
                    missile.SetAimpoint(target.ToGlobalPosition(), Vector3.zero);
                    missile.Arm();
                    Visuals.KineticRodStrikeVisuals.Track(missile, target);
                }
            }
            finally
            {
                host.Release(SupportPool.Strike);
            }
        }
    }
}
