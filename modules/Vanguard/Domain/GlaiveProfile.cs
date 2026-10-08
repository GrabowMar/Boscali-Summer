using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Parachute gun-pod envelope, shared by host logic and offline tests.</summary>
    internal static class GlaiveProfile
    {
        public const float DeploymentHeight = 650f;
        public const float DeploymentRange = 400f;
        public const float DescentSpeed = 5f;
        public const float RetireHeight = 30f;
        public const float GunRange = 1200f;
        public const float BrakeSeconds = 1.8f;
        public const float ShotInterval = 1f / 9f;
        public const int Rounds = 240;

        public static float Height(float range) => Mathf.Clamp(range * .10f, DeploymentHeight, 2500f);
        public static bool ShouldDeploy(float range, float agl, float age) =>
            age > 3f && range < DeploymentRange && agl > 350f && agl < 1400f;
        public static float Inflation(float age) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / BrakeSeconds));
        public static bool Burst(float age) => age >= BrakeSeconds && age % 2.3f < .75f;
        public static bool CanFire(float distance, float heightBelow, bool hostile, bool visible) =>
            hostile && visible && distance > 5f && distance <= GunRange && heightBelow > 25f;
        public static bool Retire(float agl, int rounds, float age) =>
            agl <= RetireHeight || rounds <= 0 || age >= 180f;
    }
}
