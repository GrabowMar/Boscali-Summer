using System.Collections;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// PRSM strike: one offboard ballistic missile onto the mark. Gated by allocation,
    /// cooldown, intel freshness and range — never by a station. Spawns synchronously so
    /// the reply carries the live missile's time of flight.
    /// </summary>
    internal sealed class PrsmAction : ISupportAction
    {
        private const float ReleaseAltitude = 12000f;
        private const float ReleaseSpeed = 1500f;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition =
                context.Host.Vanilla.Prsm(context.Settings.PrsmDefinitionKey.Value);
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning(
                    "[Support] PRSM is unavailable: no non-nuclear ballistic missile definition resolved.");
                return SupportResult.CapabilityUnavailable;
            }
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null) return SupportResult.CapabilityUnavailable;
            if (!SupportTargeting.TryMapPoint(context.Target, out Vector3 ground))
                return SupportResult.InvalidTarget;
            if (SupportTargeting.TryOrigin(context.Player, out Vector3 origin))
            {
                if (Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                    return SupportResult.OutOfRange;
            }
            if (!SupportTargeting.IntelFreshAt(context.Owner, ground, context.Settings.IntelFreshSeconds.Value, context.Settings.IntelGateRadius.Value))
            {
                context.Logger.LogInfo("[Support] PRSM refused: stale intel at the grid.");
                return SupportResult.StaleIntel;
            }
            if (!context.Host.TryReserve(context.Owner, SupportPool.Strike)) return SupportResult.Busy;

            string unique = SupportNaming.Unique("Prsm", context);
            string guide = context.Player != null && context.Player.Aircraft != null
                ? context.Player.Aircraft.UniqueName
                : string.Empty;
            Missile missile = spawner.SpawnSavedMissile(
                definition.unitPrefab,
                (ground + Vector3.up * ReleaseAltitude).ToGlobalPosition(),
                Quaternion.LookRotation(Vector3.down), context.Owner, string.Empty, guide,
                Vector3.down * ReleaseSpeed, unique);
            if (missile == null)
            {
                context.Host.Release(context.Owner, SupportPool.Strike);
                return SupportResult.SpawnFailed;
            }
            missile.SetAimpoint(ground.ToGlobalPosition(), Vector3.zero);
            missile.Arm();
            float tti = StrikeBallistics.TimeOfFlight(
                Vector3.Distance(missile.transform.position, ground),
                missile.GetTopSpeed(ReleaseAltitude, ground.y));
            context.Host.ReportTti(context.RequestId, tti);
            context.Logger.LogInfo("[Support] PRSM released by " + definition.jsonKey +
                (tti >= 0f ? "; time of flight ~" + Mathf.RoundToInt(tti) + " s." : "."));
            context.Host.Run(Expire(context.Host, spawner, context.Owner, missile, 60f));
            return SupportResult.Accepted;
        }

        private static IEnumerator Expire(
            ISupportHost host, Spawner spawner, FactionHQ owner, Missile missile, float seconds)
        {
            try
            {
                float deadline = Time.time + seconds;
                while (Time.time < deadline)
                {
                    if (missile == null || missile.disabled) break;
                    yield return null;
                }
                if (missile != null && !missile.disabled)
                    spawner.ServerObjectManager.Destroy(missile.gameObject); // Expiry is not an impact.
            }
            finally
            {
                host.Release(owner, SupportPool.Strike);
            }
        }
    }
}
