using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>ORCA underwater run: supercavitating speed, a depth spring and a snake search when it loses the ship.</summary>
    internal static class WaterRun
    {
        public const float Speed = 90f;
        public const float Depth = -6f;
        public const float EntryRange = 2000f;   // glide to the water this far from the ship
        public const float StopLeadRange = 300f; // the last stretch runs straight at the hull: hard turns can beat it
        public const float LostLimit = 3f;       // seconds without a track before the snake starts
        public const float SearchTime = 10f;     // snake duration before the torpedo scuttles
        public const float SnakeAmplitude = 30f;
        public const float SnakePeriod = 4f;

        public static float VerticalAccel(float globalY, float vy) => (Depth - globalY) * 4f - vy * 3f;

        public static bool Accepts(bool isShip) => isShip;

        public static float SnakeYaw(float t) => SnakeAmplitude * Mathf.Sin(2f * Mathf.PI * t / SnakePeriod);
    }
}
