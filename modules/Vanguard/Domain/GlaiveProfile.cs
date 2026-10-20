using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum GlaivePhase : byte { Launch, Ingress, Orbit, Depart }

    /// <summary>
    /// GLAIVE-2S SPECTRE: launch-assisted, wingborne gunship drone. Left-hand (counter-clockwise from above) pylon
    /// turn around a ground point with a port-side gun; a new designation slides the orbit centre (HAND-OFF).
    /// Angles are degrees in the drone frame: yaw + = starboard, pitch + = depression.
    /// </summary>
    internal static class GlaiveProfile
    {
        public const float LaunchSeconds = 3f;
        public const float CruiseSpeed = 90f;
        public const float OrbitRadius = 450f;
        public const float OrbitHeight = 350f;        // above the orbit centre
        public const float MinOrbitAgl = 150f;        // terrain floor under the drone while orbiting
        public const float CaptureRange = 900f;
        public const float Lookahead = 250f;          // orbit waypoint distance
        public const float SlideSpeed = 60f;          // HAND-OFF: orbit centre speed toward the new target
        public const float HandOffShown = 50f;        // HUD reads HAND-OFF while the centre is farther than this
        public const float Reach = 350f;              // gun engages ground units within this of the centre
        public const float GunRange = 900f;
        public const float RetireHeight = 35f;
        public const float Endurance = 210f;          // seconds on station
        public const float ShotInterval = .2f;
        public const float BurstPeriod = 3f;
        public const float BurstLength = 1f;
        public const int Rounds = 240;
        public const float RecoilImpulse = 12f;
        public const float AimRate = 55f;
        public const float OrbitBank = 30f;           // visual roll added on station
        public const float BankRate = 25f;
        public const float YawMin = -170f, YawMax = -10f;   // port side only
        public const float PitchMin = -30f, PitchMax = 80f;

        public static float Height(float range) => Mathf.Clamp(range*.04f,OrbitHeight,1500f);
        public static bool Capture(float range,float agl,float age,float speed) =>
            age > LaunchSeconds+2f && range < CaptureRange && agl > 70f && speed > 45f && speed < 160f;
        /// <summary>One-second bursts every three seconds, the first one second after orbit entry.</summary>
        public static bool Burst(float onStation) => onStation >= 1f && (onStation-1f)%BurstPeriod < BurstLength;
        /// <summary>Gun gate for a hostile target already in reach; line of sight is checked by the caller.</summary>
        public static bool CanFire(float distance,float heightBelow,float speed,float alignment) =>
            distance > 20f && distance <= GunRange && heightBelow > 30f && speed >= 45f && speed <= 150f && alignment >= .998f;
        public static bool Retire(int rounds,float onStation) => rounds <= 0 || onStation >= Endurance;
        public static bool Crashing(float agl,float age) => age > 5f && agl <= RetireHeight;
        public static float Throttle(float speed) => Mathf.Clamp(.4f+(CruiseSpeed-speed)*.025f,0f,1f);
        public static float Bank(GlaivePhase phase) => phase == GlaivePhase.Orbit ? OrbitBank : 0f;

        /// <summary>Counter-clockwise tangent pursuit with radial correction; finite even at the orbit centre.</summary>
        public static Vector3 OrbitOffset(Vector3 radial,Vector3 forward)
        {
            radial.y=0f; forward.y=0f;
            float radius=radial.magnitude;
            Vector3 normal=radius > 1f ? radial/radius : (forward.sqrMagnitude > .01f ? forward.normalized : Vector3.forward);
            Vector3 tangent=new Vector3(-normal.z,0f,normal.x);
            float correction=Mathf.Clamp((OrbitRadius-radius)/OrbitRadius,-1f,1f);
            return (tangent+normal*correction).normalized*Lookahead;
        }

        /// <summary>HAND-OFF step for one plan tick: horizontal move capped at SlideSpeed, height follows the target.</summary>
        public static Vector3 SlideStep(Vector3 toObjective,float dt)
        {
            Vector3 flat=Vector3.ClampMagnitude(new Vector3(toObjective.x,0f,toObjective.z),SlideSpeed*dt);
            return new Vector3(flat.x,toObjective.y,flat.z);
        }

        /// <summary>Barrel yaw/pitch of a drone-frame direction (unclamped).</summary>
        public static Vector2 GunAngles(Vector3 local) => new Vector2(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,
            Mathf.Atan2(-local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg);
        /// <summary>Drone-frame barrel direction for yaw/pitch (inverse of GunAngles).</summary>
        public static Vector3 Barrel(float yaw,float pitch)
        {
            float y=yaw*Mathf.Deg2Rad, p=pitch*Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(p)*Mathf.Sin(y),-Mathf.Sin(p),Mathf.Cos(p)*Mathf.Cos(y));
        }
        public static Vector2 ClampGun(Vector2 angles) =>
            new Vector2(Mathf.Clamp(angles.x,YawMin,YawMax),Mathf.Clamp(angles.y,PitchMin,PitchMax));

        public static string HudText(GlaivePhase phase,int roundsLeft,float slide)
        {
            switch (phase)
            {
                case GlaivePhase.Depart: return roundsLeft <= 0 ? "SPECTRE WINCHESTER" : "SPECTRE OFF STATION";
                case GlaivePhase.Orbit: return slide >= HandOffShown ? HandOffText(slide) : "SPECTRE IN ORBIT";
                default: return "SPECTRE INBOUND";
            }
        }

        // One string per 0.1 km step (up to 99.9 km), built on first use: the launcher HUD asks for this at 5 Hz.
        private static readonly string[] HandOff = new string[1000];
        private static string HandOffText(float slide)
        {
            int tenths = Mathf.RoundToInt(slide / 100f);
            return tenths >= 0 && tenths < HandOff.Length ? HandOff[tenths] ??= "HAND-OFF " + Km(tenths * 100f) : "HAND-OFF " + Km(slide);
        }

        public static string Km(float metres) => (metres/1000f).ToString("0.0",CultureInfo.InvariantCulture) + " km";
    }
}
