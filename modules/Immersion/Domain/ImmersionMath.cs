using System;

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
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Clamp(value, -limit, limit);

        public static float ExposureAudioCutoff(float positive, float negative)
        {
            if (float.IsNaN(positive) || float.IsNaN(negative)) return 22000f;
            float exposure = Clamp(Math.Max(positive, negative * 0.85f), 0f, 1f);
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
            float roll = -forceX * 3.5f + Clamp(rollRateDeg / 90f, -1f, 1f) * 0.8f;
            float yaw = Clamp(yawRateDeg / 20f, -1f, 1f) * 1.5f + Clamp(rollRateDeg / 180f, -1f, 1f) * 0.6f;
            return (Clamp(pitch * strength, -MaxPitchDeg, MaxPitchDeg),
                    Clamp(yaw * strength, -MaxYawDeg, MaxYawDeg),
                    Clamp(roll * strength, -MaxRollDeg, MaxRollDeg));
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
        /// Camera shake added by one round leaving the gun, from its momentum (kg·m/s).
        /// Returns vanilla low/high frequency shake units.
        /// </summary>
        public static (float low, float high) ShotShake(float momentum, float strength)
        {
            if (momentum <= 0f || strength <= 0f) return (0f, 0f);
            float high = Clamp(momentum * 0.00012f, 0.004f, 0.09f);
            float low = Clamp((momentum - 150f) * 0.00005f, 0f, 0.05f);
            return (low * strength, high * strength);
        }

        /// <summary>Touchdown thump (vanilla low-frequency units) from the sink rate in m/s.</summary>
        public static float TouchdownShake(float sinkRate, float strength)
        {
            if (strength <= 0f) return 0f;
            return Clamp(0.12f + Math.Abs(sinkRate) * 0.12f, 0f, 1f) * strength;
        }

        /// <summary>
        /// Steady runway rumble level (low, high) for a ground speed in m/s.
        /// </summary>
        public static (float low, float high) GroundRumble(float groundSpeed, float strength)
        {
            if (groundSpeed < 2f || strength <= 0f) return (0f, 0f);
            float t = Clamp((groundSpeed - 2f) / 70f, 0f, 1f);
            return (0.18f * t * strength, 0.3f * t * strength);
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
        /// Mach number from true airspeed in m/s and altitude in meters.
        /// </summary>
        public static float MachNumber(float speedMps, float altitudeM)
        {
            float c = SpeedOfSound(altitudeM);
            return c > 0f ? Math.Max(0f, speedMps / c) : 0f;
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
            return Clamp(intensity, 0f, 0.5f);
        }

        /// <summary>
        /// Canopy wind rush / aerodynamic slipstream volume (0-1) and pitch multiplier.
        /// </summary>
        public static (float volume, float pitch) WindRush(float airspeedMps, float gLoad, float strength)
        {
            if (strength <= 0f || airspeedMps < 40f) return (0f, 1f);
            float speedNorm = Clamp((airspeedMps - 40f) / 280f, 0f, 1f);
            float gNorm = Clamp(gLoad / 8f, 0f, 0.4f);
            float volume = Clamp((speedNorm * 0.72f + gNorm * 0.28f) * strength, 0f, 0.85f);
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
                float excess = Clamp((forceY - 4f) / 5f, 0f, 1f); // 0 at 4G, 1 at 9G
                float weight = excess;
                float intensity = 0.25f + 0.45f * excess;
                float saturation = -excess * 75f;
                return (weight, intensity, saturation, 0f);
            }
            else if (forceY < -1f)
            {
                float excess = Clamp((-forceY - 1f) / 3f, 0f, 1f); // 0 at -1G, 1 at -4G
                float weight = excess;
                float intensity = 0.2f + 0.35f * excess;
                return (weight, intensity, 0f, excess);
            }

            return (0f, 0f, 0f, 0f);
        }

        /// <summary>
        /// Anti-G Straining Maneuver (AGSM) breathing interval in seconds under high positive G.
        /// Returns 0 when below 4.5 G threshold.
        /// </summary>
        public static float PilotStrainInterval(float forceY)
        {
            if (forceY < 4.5f) return 0f;
            float t = Clamp((forceY - 4.5f) / 4.5f, 0f, 1f);
            return 3.2f - t * 1.0f;
        }

        /// <summary>
        /// How visible the sun flare is: 0 below the horizon, fading in over the first few degrees
        /// of elevation, then scaled by what stands between the eye and the sun (0 clear, 1 blocked).
        /// </summary>
        public static float SunVisibility(float sunElevationDeg, float cloudOcclusion, bool terrainBlocked)
        {
            if (terrainBlocked || sunElevationDeg <= -1f) return 0f;
            float horizon = Clamp((sunElevationDeg + 1f) / 6f, 0f, 1f);
            return horizon * Clamp(1f - cloudOcclusion, 0f, 1f);
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
            return Clamp((jerkGPerSec - thresholdGPerSec) / 20f, 0.15f, 0.5f);
        }

        /// <summary>
        /// MFD glass brightness multiplier from ambient light 0-1: a whisper by day,
        /// up to ~1.5x on a dark night so panels glow against the cockpit.
        /// </summary>
        public static float MfdBoost(float ambient01)
        {
            return 1.05f + 0.45f * (1f - Clamp(ambient01, 0f, 1f));
        }

        /// <summary>
        /// Environmental canopy/airframe surface wetness 0-1: accumulates with rain and cloud,
        /// shears away at airspeed (> 40 m/s).
        /// </summary>
        public static float SurfaceWetnessStep(float current, float rainRate, float cloudDensity, float airspeedMps, float dt)
        {
            if (dt <= 0f) return current;
            float accum = (rainRate * 0.45f + cloudDensity * 0.25f) * dt;
            float shear = (0.015f + Math.Max(0f, airspeedMps - 40f) * 0.0015f) * dt;
            return Clamp(current + accum - shear * current, 0f, 1f);
        }

        /// <summary>
        /// High-altitude canopy/wing frost 0-1: standard lapse rate (-6.5°C/km from sea level 15°C)
        /// yields freezing above ~2300 m. Melts descending into warm air.
        /// </summary>
        public static float SurfaceFrostStep(float current, float altitudeM, float dt)
        {
            if (dt <= 0f) return current;
            float tempC = 15f - 0.0065f * Math.Max(0f, altitudeM);
            if (tempC < 0f)
            {
                float rate = Math.Min(1f, -tempC / 40f) * 0.05f * dt;
                return Clamp(current + rate, 0f, 1f);
            }
            else
            {
                float melt = (tempC / 15f) * 0.15f * dt;
                return Clamp(current - melt, 0f, 1f);
            }
        }

        /// <summary>
        /// Combat scorch / carbon soot 0-1: spikes on damage/blast impact or afterburner,
        /// slow environmental weathering.
        /// </summary>
        public static float SurfaceScorchStep(float current, float damageSpike, float throttle, float dt)
        {
            if (dt <= 0f) return current;
            float afterburnerSoot = throttle > 1.01f ? 0.02f * dt : 0f;
            float weathering = 0.001f * dt;
            return Clamp(current + damageSpike + afterburnerSoot - weathering, 0f, 1f);
        }

        /// <summary>
        /// Runway and low-altitude dirt / dust accumulation 0-1: kicks up during ground roll or
        /// flight &lt; 30 m AGL, washed away by rain/wetness.
        /// </summary>
        public static float SurfaceDirtStep(float current, float radarAltM, float groundSpeedMps, bool onGround, float wetness, float dt)
        {
            if (dt <= 0f) return current;
            float kickup = 0f;
            if (onGround)
            {
                kickup = Math.Min(1f, groundSpeedMps / 45f) * 0.04f * dt;
            }
            else if (radarAltM < 30f && radarAltM > 0f)
            {
                kickup = (1f - radarAltM / 30f) * Math.Min(1f, groundSpeedMps / 100f) * 0.015f * dt;
            }
            float wash = (0.001f + wetness * 0.06f) * dt;
            return Clamp(current + kickup - wash, 0f, 1f);
        }

        /// <summary>
        /// Cutoff frequency (Hz) for the pilot's auditory narrowing under G-load.
        /// When outside the cockpit, returns 22000 Hz (full bandwidth).
        /// In cockpit, high positive G (> 4G) drains blood from the head, muffling high frequencies down to ~800 Hz.
        /// Negative G (&lt; -1G, redout) exerts venous pressure, rolling off to ~1200 Hz.
        /// </summary>
        public static float GAudioCutoffFrequency(float forceY, bool cockpit)
        {
            if (!cockpit) return 22000f;

            const float baseCutoff = 22000f;

            if (forceY > 4f)
            {
                float excess = Clamp((forceY - 4f) / 5f, 0f, 1f);
                return baseCutoff * (float)Math.Pow(800f / baseCutoff, excess);
            }
            else if (forceY < -1f)
            {
                float redout = Clamp((-forceY - 1f) / 3f, 0f, 1f);
                return baseCutoff * (float)Math.Pow(1200f / baseCutoff, redout);
            }

            return baseCutoff;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
