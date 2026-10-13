using System.Collections;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// PRSM strike: one offboard ballistic missile onto the mark. Gated by allocation,
    /// cooldown and range — never by a station. Spawns synchronously so
    /// the reply carries the live missile's time of flight.
    /// </summary>
    internal sealed class PrsmAction : ISupportAction
    {
        private const float ReleaseAltitude = 12000f;
        private const float ReleaseSpeed = 1500f;

        public float BaseCost(in SupportContext context) => 1f; // availability flag; the allocation price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            MissileDefinition definition =
                context.Host.Vanilla.Prsm(context.Settings.PrsmDefinitionKey.Value);
            SupportResult gate = SupportTargeting.ReserveStrike(context, definition,
                "[Support] PRSM is unavailable: no non-nuclear ballistic missile definition resolved.", out Vector3 ground);
            if (gate != SupportResult.Accepted) return gate;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;

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
