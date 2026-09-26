using System;

namespace BoscaliSummer.Features.Immersion.Domain
{
    /// <summary>
    /// Pure curves behind the cockpit feel. Nothing here touches Unity, so the tests can pin them.
    /// The head model is a mass on a spring with a damper per rotation axis, as in the MIT-licensed
    /// MSFS Physics Camera (github.com/martijns/martijns-msfs2024-physics-camera), driven by the
    /// specific force the pilot feels instead of the sim's acceleration channels.
    /// </summary>
    public static class ImmersionMath
    {
        /// <summary>Natural frequency (rad/s) and damping ratio of the neck: quick, barely overshooting.</summary>
        public const float HeadOmega = 9f;
        public const float HeadDamping = 0.75f;

        public const float MaxPitchDeg = 4.5f;
        public const float MaxRollDeg = 4f;
        public const float MaxYawDeg = 3f;

        /// <summary>
        /// Where the head wants to rest (degrees; +pitch looks down, +roll tilts right, +yaw looks right)
        /// for a specific force in the cockpit frame, in G (at rest: 0, 1, 0), and a body rotation
        /// rate in deg/s (pitch, yaw, roll about x, y, z). Positive load pushes the head down; a skid's
        /// side force tilts it; the head leads a little into a roll and a yaw, like a pilot looking
        /// where they are turning.
        /// </summary>
        public static (float pitch, float yaw, float roll) HeadTarget(
            float forceX, float forceY, float forceZ, float rollRateDeg, float yawRateDeg, float strength)
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

        /// <summary>One semi-implicit Euler step of a critically-ish damped spring towards
        /// <paramref name="target"/>. Sub-steps long frames so a hitch cannot blow it up.</summary>
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
        /// Camera shake added by one round leaving the gun, from its momentum (kg·m/s): a 20 mm round
        /// (~0.1 kg at 1000 m/s) is a buzz, a 30 mm round a thump. Returns vanilla's low/high
        /// frequency shake units (they clamp to 1 and decay on their own).
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
        /// Steady runway rumble level (low, high) for a ground speed in m/s. The caller feeds it in
        /// proportion to the frame time so vanilla's own decay settles on this level.
        /// </summary>
        public static (float low, float high) GroundRumble(float groundSpeed, float strength)
        {
            if (groundSpeed < 2f || strength <= 0f) return (0f, 0f);
            float t = Clamp((groundSpeed - 2f) / 70f, 0f, 1f);
            return (0.18f * t * strength, 0.3f * t * strength);
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
        /// Pilot breathing: a slow asymmetric pitch sway in degrees. Exhale is longer than inhale
        /// (the 2:3 duty comes from the second harmonic), so it reads as breath, not a metronome.
        /// </summary>
        public static float BreathingOffset(float timeSec, float breathsPerMin, float amplitudeDeg)
        {
            if (amplitudeDeg <= 0f || breathsPerMin <= 0f) return 0f;
            double phase = 2.0 * Math.PI * breathsPerMin / 60.0 * timeSec;
            return amplitudeDeg * (float)(Math.Sin(phase) - 0.25 * Math.Sin(2.0 * phase + 0.6));
        }

        /// <summary>
        /// Airframe creak volume 0-1 from the specific-force jerk in G/s: silence below the
        /// threshold, then a quick ramp to a low ceiling. Creaks season the cockpit, never lead it.
        /// </summary>
        public static float CreakVolume(float jerkGPerSec, float thresholdGPerSec = 2.5f)
        {
            if (jerkGPerSec < thresholdGPerSec) return 0f;
            return Clamp((jerkGPerSec - thresholdGPerSec) / 20f, 0.15f, 0.5f);
        }

        /// <summary>
        /// MFD glass brightness multiplier from ambient light 0-1: a whisper by day (the tubes are
        /// already bright), up to ~1.5x on a dark night so the panels glow against the cockpit.
        /// </summary>
        public static float MfdBoost(float ambient01)
        {
            return 1.05f + 0.45f * (1f - Clamp(ambient01, 0f, 1f));
        }

        /// <summary>
        /// Environmental canopy/airframe surface wetness 0-1 (inspired by D.I.R.T.):
        /// accumulates with rain rate and cloud density, and shears away at airspeed (> 40 m/s).
        /// </summary>
        public static float SurfaceWetnessStep(float current, float rainRate, float cloudDensity, float airspeedMps, float dt)
        {
            if (dt <= 0f) return current;
            float accum = (rainRate * 0.45f + cloudDensity * 0.25f) * dt;
            float shear = (0.015f + Math.Max(0f, airspeedMps - 40f) * 0.0015f) * dt;
            return Clamp(current + accum - shear * current, 0f, 1f);
        }

        /// <summary>
        /// High-altitude canopy/wing frost 0-1:
        /// standard lapse rate (-6.5°C/km from sea level 15°C) yields freezing above ~2300 m.
        /// Cold air and high altitude accumulate frost; descending into warm low air melts it.
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
        /// Combat scorch / carbon soot 0-1:
        /// spikes upon damage/blast impact or prolonged afterburner, with slow environmental weathering.
        /// </summary>
        public static float SurfaceScorchStep(float current, float damageSpike, float throttle, float dt)
        {
            if (dt <= 0f) return current;
            float afterburnerSoot = throttle > 1.01f ? 0.02f * dt : 0f;
            float weathering = 0.001f * dt;
            return Clamp(current + damageSpike + afterburnerSoot - weathering, 0f, 1f);
        }

        /// <summary>
        /// Runway and low-altitude dirt / dust accumulation 0-1:
        /// kicks up during ground roll or flight < 30 m AGL over terrain, washed away by rain/wetness.
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

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
