using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>ALE-X tow geometry: where the decoy rides and when the fiber parts.</summary>
    internal static class TowedTrail
    {
        public const float Length = 100f;
        public const float Drop = 8f;
        internal static float SnapG = 7f; // the nomodkit sim raises it: its AI host pulls ~7 g after spawn
        public const float MinAgl = 50f;

        public static Vector3 Offset(Vector3 forward, Vector3 up) => -forward * Length - up * Drop;

        /// <summary>Where the decoy rides: 100 m behind and 8 m below its pylon anchor.</summary>
        public static Vector3 TrailPoint(Vector3 anchor, Vector3 forward, Vector3 up) => anchor + Offset(forward, up);

        public static bool Snaps(float gForce, float agl) => gForce > SnapG || agl < MinAgl;

        /// <summary>Manoeuvre load in g from a velocity change over dt (gravity excluded, as vanilla gForce).</summary>
        public static float GLoad(Vector3 previousVelocity, Vector3 velocity, float dt) =>
            dt > 0f ? (velocity - previousVelocity).magnitude / (dt * 9.81f) : 0f;
    }
}
