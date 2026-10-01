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

        /// <summary>
        /// FIRES intel gate: a hostile (or unresolvable) HQ track near the grid spotted
        /// within the window. Bounded scan with early exit; unknown owner fails closed.
        /// </summary>
        public static bool IntelFreshAt(FactionHQ owner, Vector3 local, float windowSeconds, float radiusMeters)
        {
            if (owner == null || owner.trackingDatabase == null) return false;
            if (windowSeconds <= 0f || radiusMeters <= 0f) return false;
            if (!float.IsFinite(windowSeconds) || !float.IsFinite(radiusMeters)) return false;
            GlobalPosition grid = local.ToGlobalPosition();
            float now = Time.timeSinceLevelLoad;
            float squared = radiusMeters * radiusMeters;
            int examined = 0;
            foreach (TrackingInfo track in owner.trackingDatabase.Values)
            {
                if (track == null) continue;
                if (++examined > IntelGate.MaxTracks) break;
                if (!HostileOrUnknown(owner, track)) continue;
                float age = now - track.lastSpottedTime;
                if (age < 0f) age = 0f;
                if (age > windowSeconds) continue;
                float dx = track.lastKnownPosition.x - grid.x;
                float dz = track.lastKnownPosition.z - grid.z;
                if (dx * dx + dz * dz <= squared) return true;
            }
            return false;
        }

        /// <summary>
        /// Faction picture for the client mirror: cold when no hostile track was spotted
        /// within the window. Advisory only — the server re-checks the grid at commit.
        /// </summary>
        public static bool FactionIntelCold(FactionHQ owner, float windowSeconds)
        {
            if (owner == null || owner.trackingDatabase == null) return true;
            if (windowSeconds <= 0f || !float.IsFinite(windowSeconds)) return true;
            float now = Time.timeSinceLevelLoad;
            int examined = 0;
            foreach (TrackingInfo track in owner.trackingDatabase.Values)
            {
                if (track == null) continue;
                if (++examined > IntelGate.MaxTracks) break;
                if (!HostileOrUnknown(owner, track)) continue;
                float age = now - track.lastSpottedTime;
                if (age < 0f) age = 0f;
                if (age <= windowSeconds) return false;
            }
            return true;
        }

        private static bool HostileOrUnknown(FactionHQ owner, TrackingInfo track)
        {
            Unit unit;
            if (!track.TryGetUnit(out unit) || unit == null) return true;
            return unit.NetworkHQ != null && unit.NetworkHQ != owner;
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
