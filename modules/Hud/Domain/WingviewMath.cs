using System;

namespace BoscaliSummer.Features.Hud.Domain
{
    /// <summary>
    /// Pure 3D vector for deterministic camera math without UnityEngine, matching the project's
    /// established pattern (<c>AceVec2</c> in Autopilot/Domain). World-up convention: Y is up.
    /// </summary>
    internal readonly struct WVVec3
    {
        public readonly float X, Y, Z;
        public WVVec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly WVVec3 Zero = new WVVec3(0f, 0f, 0f);
        public static readonly WVVec3 Up = new WVVec3(0f, 1f, 0f);
        /// <summary>Fallback heading used whenever a direction cannot be derived (zero-length
        /// input, degenerate cross product): straight ahead on the horizon.</summary>
        public static readonly WVVec3 Forward = new WVVec3(0f, 0f, 1f);

        public static WVVec3 operator +(WVVec3 a, WVVec3 b) => new WVVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static WVVec3 operator -(WVVec3 a, WVVec3 b) => new WVVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static WVVec3 operator -(WVVec3 a) => new WVVec3(-a.X, -a.Y, -a.Z);
        public static WVVec3 operator *(WVVec3 a, float s) => new WVVec3(a.X * s, a.Y * s, a.Z * s);

        public float LengthSq => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(LengthSq);

        public bool IsFinite =>
            !float.IsNaN(X) && !float.IsInfinity(X) &&
            !float.IsNaN(Y) && !float.IsInfinity(Y) &&
            !float.IsNaN(Z) && !float.IsInfinity(Z);

        /// <summary>Unit vector, or <paramref name="fallback"/> when this vector is too short or
        /// non-finite to normalize safely. Never produces NaN.</summary>
        public WVVec3 Normalized(WVVec3 fallback)
        {
            if (!IsFinite) return fallback;
            float len = Length;
            if (len < 1e-6f) return fallback;
            return new WVVec3(X / len, Y / len, Z / len);
        }

        public static WVVec3 Lerp(WVVec3 a, WVVec3 b, float t) => a + (b - a) * t;
    }

    /// <summary>
    /// Pure pose math for the Wingview third-person camera
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Camera"). No
    /// UnityEngine dependency: <see cref="Runtime.WingviewCameraState"/> feeds live transforms in
    /// as <see cref="WVVec3"/> and applies the resulting pose to the real camera. Every function
    /// here is deterministic and NaN-safe so the frame-rate-independence and clamp behaviour can
    /// be asserted directly against fixed inputs.
    /// </summary>
    internal static class WingviewMath
    {
        public const float VelocityWeight = 0.6f;
        public const float PitchWeight = 0.5f;
        public const float MinSpeedForCourse = 1f;

        public const float LookAheadGain = 0.35f;
        public const float LookAheadClampDeg = 12f;
        public const float YawRateSmoothingRate = 6f;

        public const float PositionSmoothingRate = 6f;
        public const float RotationSmoothingRate = 8f;

        public const float RecentreIdleSeconds = 1.5f;
        public const float RecentreTau = 0.6f;

        public const float EyeUpFraction = 0.28f;
        public const float LookDistanceFraction = 3f;
        public const float LookUpFraction = 0.12f;

        /// <summary>
        /// Exponential smoothing factor for one step of size <paramref name="dt"/> at
        /// <paramref name="rate"/> (1/seconds). Composing two steps -- (1-t1)(1-t2) -- always
        /// equals the single-step factor for dt1+dt2 exactly (both are exp(-rate*dt)), which is
        /// what makes <see cref="ExpSmooth(float,float,float,float)"/> frame-rate independent for
        /// a fixed target regardless of how the same total time is subdivided.
        /// </summary>
        public static float SmoothingAlpha(float rate, float dt)
        {
            if (rate <= 0f || dt <= 0f) return 0f;
            double a = 1.0 - Math.Exp(-(double)rate * dt);
            if (double.IsNaN(a) || double.IsInfinity(a)) return 1f;
            return (float)Math.Min(1.0, Math.Max(0.0, a));
        }

        public static float ExpSmooth(float current, float target, float rate, float dt) =>
            current + (target - current) * SmoothingAlpha(rate, dt);

        public static WVVec3 ExpSmooth(WVVec3 current, WVVec3 target, float rate, float dt) =>
            WVVec3.Lerp(current, target, SmoothingAlpha(rate, dt));

        /// <summary>
        /// Blends the nose direction with the flight-path (velocity) direction 40/60, then halves
        /// the resulting pitch (asin of the vertical component) before re-levelling the up
        /// vector to world up, so a vertical dive or climb never flips the camera over the top.
        /// Falls back to <paramref name="nose"/> (then <see cref="WVVec3.Forward"/>) for any
        /// degenerate input; the output is always a finite unit vector.
        /// </summary>
        public static WVVec3 BlendDirection(WVVec3 nose, WVVec3 velocity, bool hasVelocity)
        {
            WVVec3 noseDir = nose.Normalized(WVVec3.Forward);
            WVVec3 courseDir = hasVelocity && velocity.LengthSq > MinSpeedForCourse * MinSpeedForCourse
                ? velocity.Normalized(noseDir)
                : noseDir;

            WVVec3 blend = WVVec3.Lerp(noseDir, courseDir, VelocityWeight).Normalized(noseDir);

            float horiz = (float)Math.Sqrt(blend.X * blend.X + blend.Z * blend.Z);
            float azimuth = horiz < 1e-6f && Math.Abs(blend.Y) < 1e-6f
                ? 0f
                : (float)Math.Atan2(blend.X, blend.Z);

            float clampedY = Math.Min(1f, Math.Max(-1f, blend.Y));
            float pitch = (float)Math.Asin(clampedY) * PitchWeight;

            float cosPitch = (float)Math.Cos(pitch);
            float sinPitch = (float)Math.Sin(pitch);
            var result = new WVVec3(
                (float)Math.Sin(azimuth) * cosPitch,
                sinPitch,
                (float)Math.Cos(azimuth) * cosPitch);
            return result.Normalized(WVVec3.Forward);
        }

        /// <summary>Yaw rate (deg/s) scaled to a lead angle, clamped to ±<see cref="LookAheadClampDeg"/>.</summary>
        public static float LookAheadYaw(float smoothedYawRateDegPerSec)
        {
            if (float.IsNaN(smoothedYawRateDegPerSec) || float.IsInfinity(smoothedYawRateDegPerSec)) return 0f;
            float lead = smoothedYawRateDegPerSec * LookAheadGain;
            return Math.Min(LookAheadClampDeg, Math.Max(-LookAheadClampDeg, lead));
        }

        /// <summary>Rotates <paramref name="dir"/> around world up by <paramref name="yawDeg"/>
        /// degrees, leaving its pitch (Y) untouched -- a pure heading offset.</summary>
        public static WVVec3 ApplyYawOffset(WVVec3 dir, float yawDeg)
        {
            double rad = yawDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            return new WVVec3(dir.X * cos + dir.Z * sin, dir.Y, -dir.X * sin + dir.Z * cos);
        }

        /// <summary>Vanilla's own orbit distance: (1 + maxRadius*(1+viewDistAdjust)) * 2.</summary>
        public static float FollowDistance(float followingMaxRadius, float viewDistAdjust) =>
            (1f + Math.Max(0f, followingMaxRadius) * (1f + viewDistAdjust)) * 2f;

        public readonly struct Pose
        {
            public readonly WVVec3 Eye, LookTarget;
            public Pose(WVVec3 eye, WVVec3 lookTarget) { Eye = eye; LookTarget = lookTarget; }
        }

        /// <summary>
        /// Aircraft in the lower third, aim region at screen centre: eye sits behind and above
        /// along -dir, the look target well ahead and slightly above along +dir.
        /// </summary>
        public static Pose ComputePose(WVVec3 aircraftPosition, WVVec3 dir, WVVec3 up, float distance)
        {
            float d = float.IsNaN(distance) || float.IsInfinity(distance) ? 0f : Math.Max(0f, distance);
            WVVec3 eye = aircraftPosition - dir * d + up * (EyeUpFraction * d);
            WVVec3 look = aircraftPosition + dir * (LookDistanceFraction * d) + up * (LookUpFraction * d);
            return new Pose(eye, look);
        }
    }
}
