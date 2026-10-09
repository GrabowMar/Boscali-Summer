using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum GlaivePhase : byte { Launch, Ingress, Orbit, Depart }

    /// <summary>Launch-assisted, wingborne suppression drone. No hover or parachute phase.</summary>
    internal static class GlaiveProfile
    {
        public const float LaunchSeconds = 3f;
        public const float CruiseSpeed = 70f;
        public const float OrbitHeight = 160f;
        public const float OrbitRadius = 300f;
        public const float CaptureRange = 650f;
        public const float GunRange = 550f;
        public const float RetireHeight = 35f;
        public const float Endurance = 180f;
        public const float ShotInterval = .15f;
        public const int Rounds = 180;
        public const float RecoilImpulse = 7.8f;
        public const float AimRate = 55f;

        public static float Height(float range) => Mathf.Clamp(range*.04f,OrbitHeight,1500f);
        public static bool Capture(float range,float agl,float age,float speed) =>
            age > LaunchSeconds+2f && range < CaptureRange && agl > 70f && speed > 45f && speed < 135f;
        public static bool Burst(float age) => age >= 1f && age%2.4f < .60f;
        public static bool CanFire(float distance,float heightBelow,float speed,float alignment,bool hostile,bool visible) =>
            hostile && visible && distance > 20f && distance <= GunRange && heightBelow > 30f &&
            speed >= 45f && speed <= 100f && alignment >= .999f;
        public static bool Retire(float agl,int rounds,float age) => (age > 5f && agl <= RetireHeight) || rounds <= 0 || age >= Endurance;
        public static float Throttle(float speed) => Mathf.Clamp(.4f+(CruiseSpeed-speed)*.025f,0f,1f);

        /// <summary>Tangent pursuit with radial correction; finite even at the orbit centre.</summary>
        public static Vector3 OrbitOffset(Vector3 radial,Vector3 forward)
        {
            radial.y=0f; forward.y=0f;
            float radius=radial.magnitude;
            Vector3 normal=radius > 1f ? radial/radius : (forward.sqrMagnitude > .01f ? forward.normalized : Vector3.forward);
            Vector3 tangent=new Vector3(-normal.z,0f,normal.x);
            float correction=Mathf.Clamp((OrbitRadius-radius)/OrbitRadius,-1f,1f);
            return (tangent+normal*correction).normalized*220f;
        }
    }
}
