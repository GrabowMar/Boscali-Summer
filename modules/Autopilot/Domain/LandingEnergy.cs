using System;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>
    /// Approach energy: stall floor, hot bleed, flare cap, rollout brakes, damage scale.
    /// Pure so the test runner links it; the controller only feeds speeds and altitudes.
    /// </summary>
    internal static class LandingEnergy
    {
        public readonly struct Result
        {
            public readonly float Throttle;
            public readonly float Brake;

            public Result(float throttle, float brake)
            {
                Throttle = throttle;
                Brake = brake;
            }
        }

        public static Result Compute(
            float speed, float targetSpeed, float landSpeed, float cruise,
            float radarAlt, bool flare, bool rollout, bool carrier, float severity)
        {
            if (float.IsNaN(speed) || float.IsNaN(targetSpeed) || float.IsNaN(landSpeed))
                return new Result(0f, 0f);

            if (rollout && radarAlt < 14f) return new Result(0f, 1f);
            if (radarAlt > 0f && radarAlt < 2f && speed < landSpeed * 1.05f) return new Result(0f, 1f);

            float stall = landSpeed * StallMul(carrier, flare, radarAlt);
            if (speed < stall && radarAlt > 3f)
                return new Result(Clamp(Math.Max(cruise, 0.75f), 0.55f, 1f), 0f);

            float err = speed - targetSpeed;
            if (err > 40f && radarAlt > 8f) return new Result(0f, 1f);

            float throttle = Clamp(0.5f - err * 0.1f, 0f, cruise);
            if (flare) throttle = Math.Min(throttle, carrier ? 0.28f : 0.4f);
            float brake = err > 8f ? Clamp((err - 8f) * 0.04f, 0f, 0.6f) : 0f;
            float damage = Clamp01(severity);
            if (damage > 0f) throttle *= 1f - 0.25f * damage;
            return new Result(throttle, brake);
        }

        /// <summary>Damaged airframes fly a slower final so there is less energy to kill.</summary>
        public static float SpeedScale(float severity) => 1f - 0.15f * Clamp01(severity);

        /// <summary>Bank the native AutoAim is allowed, reduced as parts come off.</summary>
        public static float BankLimit(float baseBank, float severity) =>
            baseBank * (1f - 0.4f * Clamp01(severity));

        public static float StallMul(bool carrier, bool flare, float radarAlt)
        {
            if (carrier) return flare || radarAlt < 55f ? 0.82f : 0.95f;
            return flare || radarAlt < 120f ? 0.9f : 1.05f;
        }

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;

        private static float Clamp01(float value) =>
            float.IsNaN(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
