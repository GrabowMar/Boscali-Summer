using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>ORCA underwater run: supercavitating speed, a depth spring and a snake search when it loses the ship.</summary>
    internal static class WaterRun
    {
        public const float Speed = 90f;
        public const float Depth = -6f;
        public const float EntryRange = 5000f;   // glide to the water this far out, clear of the ship's point defence
        public const float StopLeadRange = 300f; // the last stretch runs straight at the hull: hard turns can beat it
        public const float LostLimit = 3f;       // seconds without a track before the snake starts
        public const float SearchTime = 10f;     // snake duration before the torpedo scuttles
        public const float SnakeAmplitude = 30f;
        public const float SnakePeriod = 4f;

        public static float VerticalAccel(float globalY, float vy) => (Depth - globalY) * 4f - vy * 3f;

        public static bool Accepts(bool isShip) => isShip;

        /// <summary>Under-keel fuze: the run depth passes beneath shallow hulls, so fire when under the ship.</summary>
        public static bool UnderKeel(float flatDistance, float shipRadius) => flatDistance < Mathf.Max(12f, shipRadius * 0.5f);

        public static float SnakeYaw(float t, float seed = 0f) =>
            (seed > 0.5f ? -1f : 1f) * SnakeAmplitude * Mathf.Sin(2f * Mathf.PI * t / SnakePeriod + seed * 6.283185f);
    }
}
