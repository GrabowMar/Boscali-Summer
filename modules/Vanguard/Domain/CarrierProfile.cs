using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>GLAIVE glide profile: a shallow slope to the target, 40 m in the last 1.5 km, release over it.</summary>
    internal static class CarrierProfile
    {
        public const float ReleaseHeight = 40f;
        public const float FinalRange = 1500f;
        public const float ReleaseRange = 300f;
        public const float ReleaseMaxAgl = 120f;
        public const int PayloadCount = 2;

        /// <summary>Metres above the target point to aim at, at this flat range.</summary>
        public static float Height(float range) =>
            range > FinalRange ? Mathf.Clamp(range * 0.08f, 150f, 3000f) : ReleaseHeight;

        public static bool ShouldRelease(float range, float agl) => range < ReleaseRange && agl < ReleaseMaxAgl;
    }
}
