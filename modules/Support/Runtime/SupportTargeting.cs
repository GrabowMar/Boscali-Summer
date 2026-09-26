using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
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
