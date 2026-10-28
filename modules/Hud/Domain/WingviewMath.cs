using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// Pure pose math for the Wingview third-person camera
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Camera"). No
    /// UnityEngine dependency: <see cref="Runtime.WingviewCameraState"/> feeds live transforms in
    /// as <see cref="Vec3"/> and applies the resulting pose to the real camera. Every function
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
            if (!double.IsFinite(a)) return 1f;
            return (float)Math.Min(1.0, Math.Max(0.0, a));
        }

        public static float ExpSmooth(float current, float target, float rate, float dt) =>
            current + (target - current) * SmoothingAlpha(rate, dt);

        public static Vec3 ExpSmooth(Vec3 current, Vec3 target, float rate, float dt) =>
            Vec3.Lerp(current, target, SmoothingAlpha(rate, dt));

        /// <summary>Unit vector, or <paramref name="fallback"/> when <paramref name="v"/> is too short or non-finite to normalize safely. Never produces NaN.</summary>
        private static Vec3 Unit(Vec3 v, Vec3 fallback)
        {
            if (!(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z))) return fallback;
            float len = v.Length;
            return len < 1e-6f ? fallback : new Vec3(v.X / len, v.Y / len, v.Z / len);
        }

        /// <summary>
        /// Blends the nose direction with the flight-path (velocity) direction 40/60, then halves
        /// the resulting pitch (asin of the vertical component) before re-levelling the up
        /// vector to world up, so a vertical dive or climb never flips the camera over the top.
        /// Falls back to <paramref name="nose"/> (then <see cref="Vec3.Forward"/>) for any
        /// degenerate input; the output is always a finite unit vector.
        /// </summary>
        public static Vec3 BlendDirection(Vec3 nose, Vec3 velocity, bool hasVelocity)
        {
            Vec3 noseDir = Unit(nose, Vec3.Forward);
            Vec3 courseDir = hasVelocity && velocity.SqrLength > MinSpeedForCourse * MinSpeedForCourse
                ? Unit(velocity, noseDir)
                : noseDir;

            Vec3 blend = Unit(Vec3.Lerp(noseDir, courseDir, VelocityWeight), noseDir);

            float horiz = (float)Math.Sqrt(blend.X * blend.X + blend.Z * blend.Z);
            float azimuth = horiz < 1e-6f ? 0f : (float)Math.Atan2(blend.X, blend.Z);

            float clampedY = Math.Min(1f, Math.Max(-1f, blend.Y));
            float pitch = (float)Math.Asin(clampedY) * PitchWeight;

            float cosPitch = (float)Math.Cos(pitch);
            float sinPitch = (float)Math.Sin(pitch);
            var result = new Vec3(
                (float)Math.Sin(azimuth) * cosPitch,
                sinPitch,
                (float)Math.Cos(azimuth) * cosPitch);
            return Unit(result, Vec3.Forward);
        }

        /// <summary>Yaw rate (deg/s) scaled to a lead angle, clamped to ±<see cref="LookAheadClampDeg"/>.</summary>
        public static float LookAheadYaw(float smoothedYawRateDegPerSec)
        {
            if (!float.IsFinite(smoothedYawRateDegPerSec)) return 0f;
            float lead = smoothedYawRateDegPerSec * LookAheadGain;
            return Math.Min(LookAheadClampDeg, Math.Max(-LookAheadClampDeg, lead));
        }

        /// <summary>Rotates <paramref name="dir"/> around world up by <paramref name="yawDeg"/>
        /// degrees, leaving its pitch (Y) untouched -- a pure heading offset.</summary>
        public static Vec3 ApplyYawOffset(Vec3 dir, float yawDeg)
        {
            double rad = yawDeg * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            return new Vec3(dir.X * cos + dir.Z * sin, dir.Y, -dir.X * sin + dir.Z * cos);
        }

        /// <summary>Pitches <paramref name="dir"/> by <paramref name="pitchDeg"/> (positive = up) keeping its
        /// heading, clamped to +-80 deg so it can never flip over the pole.</summary>
        public static Vec3 ApplyPitchOffset(Vec3 dir, float pitchDeg)
        {
            if (!float.IsFinite(pitchDeg)) return dir;
            Vec3 d = Unit(dir, Vec3.Forward);
            double horiz = Math.Sqrt(d.X * d.X + d.Z * d.Z);
            double pitch = Math.Atan2(d.Y, horiz) + pitchDeg * Math.PI / 180.0;
            double lim = 80.0 * Math.PI / 180.0;
            pitch = Math.Max(-lim, Math.Min(lim, pitch));
            double az = horiz < 1e-6 ? 0.0 : Math.Atan2(d.X, d.Z);
            return new Vec3((float)(Math.Sin(az) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(Math.Cos(az) * Math.Cos(pitch)));
        }

        /// <summary>Vanilla's own orbit distance: (1 + maxRadius*(1+viewDistAdjust)) * 2.</summary>
        public static float FollowDistance(float followingMaxRadius, float viewDistAdjust) =>
            (1f + Math.Max(0f, followingMaxRadius) * (1f + viewDistAdjust)) * 2f;

        public readonly struct Pose
        {
            public readonly Vec3 Eye, LookTarget;
            public Pose(Vec3 eye, Vec3 lookTarget) { Eye = eye; LookTarget = lookTarget; }
        }

        /// <summary>
        /// Aircraft in the lower third, aim region at screen centre: eye sits behind and above
        /// along -dir, the look target well ahead and slightly above along +dir.
        /// </summary>
        public static Pose ComputePose(Vec3 aircraftPosition, Vec3 dir, Vec3 up, float distance)
        {
            float d = !float.IsFinite(distance) ? 0f : Math.Max(0f, distance);
            Vec3 eye = aircraftPosition - dir * d + up * (EyeUpFraction * d);
            Vec3 look = aircraftPosition + dir * (LookDistanceFraction * d) + up * (LookUpFraction * d);
            return new Pose(eye, look);
        }
    }
}
