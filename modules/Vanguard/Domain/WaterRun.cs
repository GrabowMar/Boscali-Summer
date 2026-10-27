using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>ORCA underwater run: supercavitating speed, a depth spring and a snake search when it loses the ship.</summary>
    internal static class WaterRun
    {
        public const float Speed = 90f;
        public const float Accel = 30f;          // m/s^2 from a standstill (a woken sleeper) up to Speed
        // Below the ocean underlay, which OceanDressing sinks up to 12.5 m in a gale: shallower runs show through the sea and break the troughs.
        public const float Depth = -14f;
        public const float EntryRange = 5000f;   // glide to the water this far out, clear of the ship's point defence
        public const float StopLeadRange = 300f; // the last stretch runs straight at the hull: hard turns can beat it
        public const float LostLimit = 3f;       // seconds without a track before the snake starts
        public const float SearchTime = 10f;     // snake duration before the torpedo gives up (re-sleeps or scuttles)
        public const float SnakeAmplitude = 30f;
        public const float SnakePeriod = 4f;

        /// <summary>Run depth over this seabed: Depth in open water, 3 m off the bottom in the shallows (a seabed hit detonates it).</summary>
        public static float RunDepth(float seabedY) => Mathf.Min(-2f, Mathf.Max(Depth, seabedY + 3f));

        public static float VerticalAccel(float globalY, float vy, float depth = Depth) => (depth - globalY) * 4f - vy * 3f;

        /// <summary>Run speed this tick: entries faster than Speed clamp at once, a standing start ramps up.</summary>
        public static float RunSpeed(float flatSpeed, float dt) => Mathf.Min(Speed, flatSpeed + Accel * dt);

        /// <summary>Only a live ship gets an attack run; anything else becomes a sleeper.</summary>
        public static bool Accepts(bool isShip) => isShip;

        /// <summary>Under-keel fuze: the run depth passes beneath shallow hulls, so fire when under the ship.</summary>
        public static bool UnderKeel(float flatDistance, float shipRadius) => flatDistance < Mathf.Max(12f, shipRadius * 0.5f);

        public static float SnakeYaw(float t, float seed = 0f) =>
            (seed > 0.5f ? -1f : 1f) * SnakeAmplitude * Mathf.Sin(2f * Mathf.PI * t / SnakePeriod + seed * 6.283185f);
    }
}
