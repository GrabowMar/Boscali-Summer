using System;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    /// <summary>Pure rules for the local ownship autopilot landing: input yield thresholds,
    /// mass-adjusted approach speed and touchdown/stopped gates. No Unity types so the test
    /// project can compile it.</summary>
    internal static class AutopilotLandPolicy
    {
        public const float ManualAxisThreshold = 0.25f;
        public const float VirtualJoystickTravel = 150f;
        public const float AlignmentDegrees = 10f;
        public const float StopSpeed = 2.5f;
        public const float StopAltitude = 5f;

        public static bool ManualAxisOverride(float pitch, float roll, float yaw, float threshold)
        {
            if (float.IsNaN(pitch) || float.IsNaN(roll) || float.IsNaN(yaw)) return true;
            return Math.Abs(pitch) > threshold || Math.Abs(roll) > threshold || Math.Abs(yaw) > threshold;
        }

        public static bool VirtualJoystickOverride(float travel, float threshold) =>
            !float.IsNaN(travel) && Math.Abs(travel) > VirtualJoystickTravel * threshold;

        public static float AdjustedLandingSpeed(float mass, float maxWeight, float landingSpeed)
        {
            if (float.IsNaN(mass) || float.IsNaN(maxWeight) || !(mass > 0f) || !(maxWeight > 0f))
                return landingSpeed;
            return (float)Math.Sqrt(mass / maxWeight) * landingSpeed;
        }

        public static float ApproachThrottle(float speed, float targetSpeed, float cruiseThrottle)
        {
            if (float.IsNaN(speed) || float.IsNaN(targetSpeed) || float.IsNaN(cruiseThrottle)) return 0f;
            return Clamp(0.5f - (speed - targetSpeed) * 0.1f, 0f, cruiseThrottle);
        }

        public static float BrakeRamp(float secondsOnGround)
        {
            if (float.IsNaN(secondsOnGround)) return 0f;
            return Clamp(secondsOnGround * 2f, 0f, 1f);
        }

        public static bool Stopped(float speed, float radarAlt) =>
            !float.IsNaN(speed) && !float.IsNaN(radarAlt) &&
            speed < StopSpeed && radarAlt < StopAltitude;

        public static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;
    }
}
