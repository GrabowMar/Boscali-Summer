using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>ALE-X tow geometry: where the decoy rides and when the fiber parts.</summary>
    internal static class TowedTrail
    {
        public const float Length = 100f;
        public const float Drop = 8f;
        public const float SnapG = 7f;
        public const float MinAgl = 50f;

        public static Vector3 Offset(Vector3 forward, Vector3 up) => -forward * Length - up * Drop;

        public static bool Snaps(float gForce, float agl) => gForce > SnapG || agl < MinAgl;
    }
}
