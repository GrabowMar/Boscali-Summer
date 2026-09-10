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
            if (!TryMapPoint(target, out Vector3 hit) || hit.y <= Datum.LocalSeaY + 2f)
                return false;
            if (!Physics.Raycast(
                    new Vector3(hit.x, hit.y + 4f, hit.z), Vector3.down, out RaycastHit sample, 16f,
                    (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask))
                return false;
            if (Vector3.Angle(sample.normal, Vector3.up) > 35f) return false;

            int blockers = (int)PhysicsLayers.DefaultMask | (int)PhysicsLayers.ShipsMask |
                (int)PhysicsLayers.ExclusionZonesMask;
            if (Physics.CheckSphere(hit + Vector3.up * 12f, 6f, blockers)) return false;
            ground = hit;
            return true;
        }

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
