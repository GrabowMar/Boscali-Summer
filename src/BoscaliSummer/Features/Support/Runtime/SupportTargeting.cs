using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal static class SupportTargeting
    {
        /// <summary>
        /// Resolves a map coordinate to a usable piece of ground.
        /// The previous rule also treated <c>StaticsMask</c> as a blocker in the clearance
        /// sphere, so the terrain the ray had just hit rejected its own impact point on any
        /// slope and almost every pick came back InvalidTarget. Clearance now looks only for
        /// units, ships and exclusion zones, and the slope limit is a usable 35 degrees.
        /// </summary>
        public static bool TryGround(GlobalPosition target, out Vector3 ground)
        {
            ground = default;
            Vector3 local = target.ToLocalPosition();
            Vector3 origin = new Vector3(local.x, Mathf.Max(local.y, Datum.LocalSeaY) + 3000f, local.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 6000f,
                (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask))
                return false;
            if (hit.point.y <= Datum.LocalSeaY + 2f) return false;
            if (Vector3.Angle(hit.normal, Vector3.up) > 35f) return false;

            int blockers = (int)PhysicsLayers.DefaultMask | (int)PhysicsLayers.ShipsMask |
                (int)PhysicsLayers.ExclusionZonesMask;
            if (Physics.CheckSphere(hit.point + Vector3.up * 12f, 6f, blockers)) return false;
            ground = hit.point;
            return true;
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
