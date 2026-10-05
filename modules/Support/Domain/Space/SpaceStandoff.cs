using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// Core spec §10: a rod never lands near a friendly player. Friendly standoff = clamp(1.5 km x S, 0.8, 3 km) with
    /// S = clamp(map diagonal / 150 km, 0.4, 2.5). The rule is horizontal and pure; the runtime supplies the aircraft.
    /// </summary>
    internal static class SpaceStandoff
    {
        public const float ReferenceDiagonal = 150000f, BaseMeters = 1500f, MinimumMeters = 800f, MaximumMeters = 3000f;

        public static float Scale(float mapDiagonal) =>
            !SpaceRules.Finite(mapDiagonal) || mapDiagonal <= 0f ? 1f : Math.Max(.4f, Math.Min(2.5f, mapDiagonal / ReferenceDiagonal));

        public static float Radius(float mapDiagonal) =>
            Math.Max(MinimumMeters, Math.Min(MaximumMeters, BaseMeters * Scale(mapDiagonal)));

        /// <summary>
        /// True when the friendly is on or inside the standoff circle of the impact. An unreadable coordinate fails closed:
        /// the fire is refused rather than risking a friendly.
        /// </summary>
        public static bool Violates(float impactX, float impactZ, float radius, float friendlyX, float friendlyZ)
        {
            if (!SpaceRules.Finite(impactX) || !SpaceRules.Finite(impactZ) || !SpaceRules.Finite(radius) ||
                !SpaceRules.Finite(friendlyX) || !SpaceRules.Finite(friendlyZ)) return true;
            double dx = (double)friendlyX - impactX, dz = (double)friendlyZ - impactZ;
            return dx * dx + dz * dz <= (double)radius * radius;
        }
    }
}
