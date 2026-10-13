using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal static class SupportTargeting
    {
        /// <summary>
        /// Map click to a world point. EMP and kinetic strikes need an XZ, not a buildable
        /// pad: slope and nearby units used to reject the first click as InvalidTarget, and
        /// the missile then never left the ground.
        /// </summary>
        public static bool TryMapPoint(GlobalPosition target, out Vector3 point)
        {
            Vector3 local = target.ToLocalPosition();
            Vector3 origin = new Vector3(local.x, Mathf.Max(local.y, Datum.LocalSeaY) + 8000f, local.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 16000f,
                (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask))
            {
                point = hit.point;
                return true;
            }

            point = new Vector3(local.x, Mathf.Max(local.y, Datum.LocalSeaY), local.z);
            return true;
        }

        /// <summary>
        /// The gates every delivered strike (EMP, flare barrage, PRSM) passes in this order: a resolved delivery missile (else <paramref name="unavailable"/> is logged),
        /// a spawner, a map point, the pilot's range and a free Strike slot. Accepted means every gate passed and the slot is reserved (the caller must release it).
        /// </summary>
        public static SupportResult ReserveStrike(in SupportContext context, MissileDefinition definition, string unavailable, out Vector3 ground)
        {
            ground = default;
            if (definition == null || definition.unitPrefab == null)
            {
                context.Logger.LogWarning(unavailable);
                return SupportResult.CapabilityUnavailable;
            }
            if (NetworkSceneSingleton<Spawner>.i == null) return SupportResult.CapabilityUnavailable;
            if (!TryMapPoint(context.Target, out ground)) return SupportResult.InvalidTarget;
            if (TryOrigin(context.Player, out Vector3 origin) && Vector3.Distance(origin, ground) > context.Settings.MaximumRange.Value)
                return SupportResult.OutOfRange;
            return context.Host.TryReserve(context.Owner, SupportPool.Strike) ? SupportResult.Accepted : SupportResult.Busy;
        }

        public static bool TryOrigin(Player player, out Vector3 origin)
        {
            if (player != null && player.Aircraft != null)
            {
                origin = player.Aircraft.transform.position;
                return true;
            }
            origin = default;
            return false;
        }

        /// <summary>Distance to the nearest airbase held by someone else: the front proxy (the same fallback SPACE uses without territory data).</summary>
        public static float FrontDistanceOf(FactionHQ owner, Vector3 point)
        {
            float nearest = float.PositiveInfinity;
            if (FactionRegistry.airbaseLookup != null)
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (airbase == null || airbase.disabled || airbase.center == null || airbase.CurrentHQ == null || airbase.CurrentHQ == owner) continue;
                    Vector3 d = point - airbase.center.position; d.y = 0f;
                    nearest = Mathf.Min(nearest, d.magnitude);
                }
            return float.IsInfinity(nearest) ? 1000000f : nearest;
        }

        public static Airbase NearestOwnedAirbase(Player player, Vector3 local, out float distance)
        {
            Airbase closest = null;
            distance = float.MaxValue;
            if (player == null || player.HQ == null) return null;
            foreach (Airbase airbase in player.HQ.GetAirbases())
            {
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != player.HQ) continue;
                Vector3 center = airbase.center != null ? airbase.center.position : airbase.transform.position;
                float candidate = Vector3.Distance(center, local);
                if (candidate >= distance) continue;
                closest = airbase;
                distance = candidate;
            }
            return closest;
        }
    }
}
