using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    /// <summary>
    /// Pure, deterministic formulas for cockpit head motion, camera shake, sun visibility,
    /// dynamic MFD glow, airframe stress audio, surface shaders, G-force audio attenuation,
    /// transonic Mach buffeting, canopy wind rush, and physiological G-vignette parameters.
    /// Pure C# only (no Unity types), testable in isolation.
    /// </summary>
    internal static class ImmersionMath
    {
        public const float MaxPitchDeg = 12f;
        public const float MaxYawDeg = 15f;
        public const float MaxRollDeg = 10f;

        private const float HeadOmega = 22f; // rad/s natural frequency (~3.5 Hz)
        private const float HeadDamping = 0.85f; // critically-ish damped; quick settle

        public static (float pitch, float yaw, float roll) ComposeMotion(float pitch, float yaw,
            float roll, float strength, bool comfort)
        {
            if (strength <= 0f || float.IsNaN(strength) || float.IsInfinity(strength)) return (0f, 0f, 0f);
            float ceiling = Math.Min(strength, 2f);
            float scale = comfort ? 0.25f : 1f;
            return (FiniteClamp(pitch, 2f * ceiling) * scale,
                FiniteClamp(yaw, 1.2f * ceiling) * scale, FiniteClamp(roll, 2f * ceiling) * scale);
        }

        private static float FiniteClamp(float value, float limit) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Scalar.Clamp(value, -limit, limit);

        public static float ExposureAudioCutoff(float positive, float negative)
        {
            if (float.IsNaN(positive) || float.IsNaN(negative)) return 22000f;
            float exposure = Scalar.Clamp(Math.Max(positive, negative * 0.85f), 0f, 1f);
            return 22000f * (float)Math.Pow(1200f / 22000f, exposure);
        }

        /// <summary>
        /// Cockpit head target rotation in degrees (pitch, yaw, roll) from local specific force in G
        /// and body angular rates in deg/s.
        /// </summary>
        public static (float pitch, float yaw, float roll) HeadTarget(
            float forceX, float forceY, float forceZ,
            float rollRateDeg, float yawRateDeg, float strength)
        {
            if (strength <= 0f) return (0f, 0f, 0f);
            float load = forceY - 1f;
            // Compressed so 9 G is not three times 3 G: the neck braces.
            float pitch = Math.Sign(load) * 1.6f * (float)Math.Log(1f + Math.Abs(load)) - forceZ * 0.8f;
            float roll = -forceX * 3.5f + Scalar.Clamp(rollRateDeg / 90f, -1f, 1f) * 0.8f;
            float yaw = Scalar.Clamp(yawRateDeg / 20f, -1f, 1f) * 1.5f + Scalar.Clamp(rollRateDeg / 180f, -1f, 1f) * 0.6f;
            return (Scalar.Clamp(pitch * strength, -MaxPitchDeg, MaxPitchDeg),
                    Scalar.Clamp(yaw * strength, -MaxYawDeg, MaxYawDeg),
                    Scalar.Clamp(roll * strength, -MaxRollDeg, MaxRollDeg));
        }

        /// <summary>
        /// One semi-implicit Euler step of a damped spring towards <paramref name="target"/>.
        /// Sub-steps long frames so a hitch cannot blow it up.
        /// </summary>
        public static (float position, float velocity) SpringStep(float position, float velocity, float target, float dt)
        {
            if (dt <= 0f) return (position, velocity);
            float k = HeadOmega * HeadOmega;
            float c = 2f * HeadDamping * HeadOmega;
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.01f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                velocity += (k * (target - position) - c * velocity) * h;
                position += velocity * h;
            }
            return (position, velocity);
        }

        /// <summary>
        /// Speed of sound in m/s as a function of altitude in meters using standard atmosphere lapse.
        /// </summary>
        public static float SpeedOfSound(float altitudeM)
        {
            float tempC = 15f - 0.0065f * Math.Max(0f, altitudeM);
            float tempK = Math.Max(180f, tempC + 273.15f);
            return (float)(20.05 * Math.Sqrt(tempK));
        }

        /// <summary>
        /// Transonic aerodynamic buffeting shake between Mach 0.86 and 1.12, peaking around Mach 0.98.
        /// </summary>
        public static float MachBuffet(float machNumber, float strength)
        {
            if (strength <= 0f) return 0f;
            float dist = Math.Abs(machNumber - 0.98f);
            if (dist >= 0.12f) return 0f;
            float norm = 1f - dist / 0.12f;
            float intensity = norm * norm * 0.28f * strength;
            return Scalar.Clamp(intensity, 0f, 0.5f);
        }

        /// <summary>
        /// Canopy wind rush / aerodynamic slipstream volume (0-1) and pitch multiplier.
        /// </summary>
        public static (float volume, float pitch) WindRush(float airspeedMps, float gLoad, float strength)
        {
            if (strength <= 0f || airspeedMps < 40f) return (0f, 1f);
            float speedNorm = Scalar.Clamp((airspeedMps - 40f) / 280f, 0f, 1f);
            float gNorm = Scalar.Clamp(gLoad / 8f, 0f, 0.4f);
            float volume = Scalar.Clamp((speedNorm * 0.72f + gNorm * 0.28f) * strength, 0f, 0.85f);
            float pitch = 0.85f + speedNorm * 0.5f + gNorm * 0.2f;
            return (volume, pitch);
        }

        /// <summary>
        /// Physiological G-vignette parameters (weight, vignette intensity, saturation drop, redout factor).
        /// Positive G (&gt; 4G) produces greyout / tunnel vision.
        /// Negative G (&lt; -1G) produces redout.
        /// </summary>
        public static (float weight, float intensity, float saturation, float redout) GVignette(float forceY, bool cockpit)
        {
            if (!cockpit) return (0f, 0f, 0f, 0f);

            if (forceY > 4f)
            {
                float excess = Scalar.Clamp((forceY - 4f) / 5f, 0f, 1f); // 0 at 4G, 1 at 9G
                float weight = excess;
                float intensity = 0.25f + 0.45f * excess;
                float saturation = -excess * 75f;
                return (weight, intensity, saturation, 0f);
            }
            else if (forceY < -1f)
            {
                float excess = Scalar.Clamp((-forceY - 1f) / 3f, 0f, 1f); // 0 at -1G, 1 at -4G
                float weight = excess;
                float intensity = 0.2f + 0.35f * excess;
                return (weight, intensity, 0f, excess);
            }

            return (0f, 0f, 0f, 0f);
        }

        /// <summary>
        /// How visible the sun flare is: 0 below the horizon, fading in over the first few degrees
        /// of elevation, then scaled by what stands between the eye and the sun (0 clear, 1 blocked).
        /// </summary>
        public static float SunVisibility(float sunElevationDeg, float cloudOcclusion, bool terrainBlocked)
        {
            if (terrainBlocked || sunElevationDeg <= -1f) return 0f;
            float horizon = Scalar.Clamp((sunElevationDeg + 1f) / 6f, 0f, 1f);
            return horizon * Scalar.Clamp(1f - cloudOcclusion, 0f, 1f);
        }

        /// <summary>
        /// Pilot breathing: a slow asymmetric pitch sway in degrees. Exhale is longer than inhale.
        /// </summary>
        public static float BreathingOffset(float timeSec, float breathsPerMin, float amplitudeDeg)
        {
            if (amplitudeDeg <= 0f || breathsPerMin <= 0f) return 0f;
            double phase = 2.0 * Math.PI * breathsPerMin / 60.0 * timeSec;
            return amplitudeDeg * (float)(Math.Sin(phase) - 0.25 * Math.Sin(2.0 * phase + 0.6));
        }

        /// <summary>
        /// Airframe creak volume 0-1 from the specific-force jerk in G/s: silence below the
        /// threshold, then a quick ramp to a low ceiling.
        /// </summary>
        public static float CreakVolume(float jerkGPerSec, float thresholdGPerSec = 2.5f)
        {
            if (jerkGPerSec < thresholdGPerSec) return 0f;
            return Scalar.Clamp((jerkGPerSec - thresholdGPerSec) / 20f, 0.15f, 0.5f);
        }
    }
}
