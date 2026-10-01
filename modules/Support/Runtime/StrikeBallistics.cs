using System.Collections.Generic;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// One waypoint of a strike route: missile position first, then legs. Pure data
    /// so time-of-flight is testable without the game.
    /// </summary>
    internal readonly struct StrikeWaypoint
    {
        public readonly float X;
        public readonly float Z;

        public StrikeWaypoint(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// FIRES ballistics (§6.1). Every quote is a live quotient of a measured length
    /// over the missile's real top speed; anything unknown quotes -1 and the MFD
    /// renders no number. Leg validation mirrors the native cruise-seeker refusal
    /// (a waypoint inside half terminal range of the missile or the target); the
    /// seeker's own check stays authoritative at commit.
    /// </summary>
    internal static class StrikeBallistics
    {
        /// <summary>Most waypoints per strike route, missile position included.</summary>
        public const int MaxWaypoints = 8;

        /// <summary>Most chained legs per cruise strike; missile plus legs plus target fits MaxWaypoints.</summary>
        public const int MaxLegs = 6;

        /// <summary>Seconds for distance at top speed, or -1 when either is unknown.</summary>
        public static float TimeOfFlight(float distanceMeters, float topSpeed)
        {
            if (distanceMeters <= 0f || topSpeed <= 0f) return -1f;
            if (!float.IsFinite(distanceMeters) || !float.IsFinite(topSpeed)) return -1f;
            return distanceMeters / topSpeed;
        }

        /// <summary>Seconds along the waypoint polyline at top speed, or -1.</summary>
        public static float MultiLegTimeOfFlight(IReadOnlyList<StrikeWaypoint> points, float topSpeed)
        {
            if (points == null || points.Count < 2 || points.Count > MaxWaypoints) return -1f;
            if (topSpeed <= 0f || !float.IsFinite(topSpeed)) return -1f;
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float dx = points[i].X - points[i - 1].X;
                float dz = points[i].Z - points[i - 1].Z;
                length += (float)System.Math.Sqrt(dx * dx + dz * dz);
            }
            return TimeOfFlight(length, topSpeed);
        }

        /// <summary>True when the native seeker would refuse the leg. Fails closed.</summary>
        public static bool LegRefusedBySeeker(float missileToWaypoint, float missileToTarget, float terminalRange)
        {
            if (terminalRange <= 0f || !float.IsFinite(terminalRange)) return true;
            float rim = terminalRange * 0.5f;
            return missileToWaypoint <= rim || missileToTarget <= rim;
        }
    }
}
