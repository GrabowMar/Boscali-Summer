using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum SentinelPhase : byte { Launch, Ingress, Brake, Hover, Drop }

    /// <summary>
    /// GLAIVE-2A SENTINEL: hovering air-defence sentry (design/vanguard-remake/MECHANICS.md, GLAIVE-2A).
    /// Boost, fly to the designated point, brake and unfold rotors 1.2 km out, hold 220 m above the ground there
    /// and shoot slow air threats with a belly turret until dry or out of hover time, then fold and drop.
    /// </summary>
    internal static class SentinelProfile
    {
        public const float BoostSeconds = 3f;
        public const float CruiseSpeed = 110f;
        public const float BrakeRange = 1200f;    // horizontal range to the park point: Ingress -> Brake
        public const float MaxReach = 8000f;      // a far target is clamped so ingress + hover fit the 300 s lifetime
        public const float FallbackRange = 3000f; // no target: park this far ahead of the launch
        public const float HoverHeight = 220f;    // above the ground under the drone
        public const float HoverSeconds = 150f;
        public const float SlideSpeed = 40f;      // a designation moves the park point at most this fast
        public const float ParkRange = 150f, ParkSpeed = 6f, ParkDwell = 1f;

        // Position hold = two cascaded P loops per axis: error -> velocity command -> acceleration (clamped). The vertical
        // command also follows a stopping-distance curve, so a sentry that arrives high drops fast without sinking through.
        public const float ReachGain = .35f, MaxClosure = 40f, ClimbGain = .5f, MaxClimb = 40f, VertBrake = 3f;
        public const float AccelGain = .8f, MaxHorizAccel = 6f, MaxVertAccel = 6f;

        public const float GunRange = 1200f, GunMinRange = 30f, GuardRadius = 1500f;
        public const float SlowAircraft = 140f, MinAirAgl = 10f, MinClosing = 20f;
        public const float PitchMin = -90f, PitchMax = 20f; // belly turret: nothing above +20 deg (climb over it)
        public const float YawRate = 160f, PitchRate = 120f, AlignCos = .995f, SpinGate = .9f;
        public const int Rounds = 300, BurstRounds = 12;
        public const float ShotInterval = .05f, BurstCooldown = 1.1f, Spread = .5f, BulletLife = 1.4f;
        public const float UnfoldSeconds = 1.2f, SpinUpSeconds = 1.5f, SpinDownSeconds = 2f, RotorRate = 2400f;
        public const float DropFuse = 8f, GazeRate = 6f;

        public static SentinelPhase Next(SentinelPhase phase, float age, float flatRange, float dwell, int rounds, float hoverTime)
        {
            switch (phase)
            {
                case SentinelPhase.Launch: return age >= BoostSeconds ? SentinelPhase.Ingress : phase;
                case SentinelPhase.Ingress: return flatRange <= BrakeRange ? SentinelPhase.Brake : phase;
                case SentinelPhase.Brake: return dwell >= ParkDwell ? SentinelPhase.Hover : phase;
                case SentinelPhase.Hover: return rounds <= 0 || hoverTime >= HoverSeconds ? SentinelPhase.Drop : phase;
                default: return phase;
            }
        }

        /// <summary>Park dwell: close to the park point and moving with it (the point itself may slide).</summary>
        public static float NextDwell(float dwell, float flatError, float relativeSpeed, float dt) =>
            flatError <= ParkRange && relativeSpeed <= ParkSpeed ? dwell + dt : 0f;

        /// <summary>Acceleration to hold station; gravity is cancelled by the caller.</summary>
        public static Vector3 HoldAccel(Vector3 toHold, Vector3 velocity)
        {
            Vector3 want = Vector3.ClampMagnitude(new Vector3(toHold.x, 0f, toHold.z) * ReachGain, MaxClosure);
            float error = Mathf.Abs(toHold.y);
            want.y = Mathf.Sign(toHold.y) * Mathf.Min(error * ClimbGain, Mathf.Sqrt(2f * VertBrake * error), MaxClimb);
            Vector3 accel = (want - velocity) * AccelGain;
            Vector3 result = Vector3.ClampMagnitude(new Vector3(accel.x, 0f, accel.z), MaxHorizAccel);
            result.y = Mathf.Clamp(accel.y, -MaxVertAccel, MaxVertAccel);
            return result;
        }

        /// <summary>One tick of the park point toward the designation (horizontal, at most SlideSpeed).</summary>
        public static Vector3 Slide(Vector3 toDesired, float dt) =>
            Vector3.ClampMagnitude(new Vector3(toDesired.x, 0f, toDesired.z), SlideSpeed * dt);

        /// <summary>Park point relative to the launch: the target clamped to MaxReach, or FallbackRange along the bearing.</summary>
        public static Vector3 ParkOffset(Vector3 toTarget, bool hasTarget, Vector3 bearing) =>
            hasTarget ? Vector3.ClampMagnitude(new Vector3(toTarget.x, 0f, toTarget.z), MaxReach) : bearing * FallbackRange;

        public static float Throttle(float speed) => Mathf.Clamp(.4f + (CruiseSpeed - speed) * .025f, 0f, 1f);

        /// <summary>Air threats only: slow airborne aircraft, or a missile whose friendly target is inside GuardRadius.</summary>
        public static bool Engageable(bool aircraft, float speed, float agl, bool missile, float guardedRange) =>
            aircraft ? speed < SlowAircraft && agl >= MinAirAgl : missile && guardedRange <= GuardRadius;

        /// <summary>Priority key, lower first: missiles by time to their target, aircraft by time to reach the drone.</summary>
        public static float TimeToImpact(float distance, float closingSpeed) => distance / Mathf.Max(closingSpeed, MinClosing);

        /// <summary>Sticky lock: only a clearly more urgent candidate takes the gun.</summary>
        public static bool Replaces(float currentTti, float candidateTti) => candidateTti < currentTti * .8f;

        public static bool InArc(float pitchDeg) => pitchDeg >= PitchMin && pitchDeg <= PitchMax;

        public static bool CanFire(float distance, float pitchDeg, float alignmentCos, bool visible, float spin, int rounds) =>
            distance >= GunMinRange && distance <= GunRange && InArc(pitchDeg) && alignmentCos >= AlignCos &&
            visible && spin >= SpinGate && rounds > 0;

        /// <summary>Delay after the shot that brought the total to <paramref name="shotsFired"/>: bursts of BurstRounds.</summary>
        public static float ShotDelay(uint shotsFired) => shotsFired % BurstRounds == 0 ? BurstCooldown : ShotInterval;

        /// <summary>Ballistic lead: where to point so a round at <paramref name="muzzleVelocity"/> meets the target.</summary>
        public static Vector3 Lead(Vector3 toTarget, Vector3 relativeVelocity, float muzzleVelocity, Vector3 gravity)
        {
            float t = Mathf.Clamp(toTarget.magnitude / Mathf.Max(muzzleVelocity, 1f), 0f, 1.5f);
            return toTarget + relativeVelocity * t - gravity * (.5f * t * t);
        }

        /// <summary>Turret yaw (deg, + = right) and pitch (deg, + = up) for a direction in the drone's local frame.</summary>
        public static Vector2 Angles(Vector3 local) => new Vector2(
            Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg,
            Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg);

        /// <summary>Arm deployment 0..1, <paramref name="t"/> seconds after braking started.</summary>
        public static float Unfold(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / UnfoldSeconds));

        /// <summary>Rotor spin 0..1: spools up once the arms are out.</summary>
        public static float Spin(float sinceDeploy) => Mathf.Clamp01((sinceDeploy - UnfoldSeconds) / SpinUpSeconds);
    }
}
