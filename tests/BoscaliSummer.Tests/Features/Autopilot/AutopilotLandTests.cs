using BoscaliSummer.Features.Autopilot.Runtime;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    internal static class AutopilotLandTests
    {
        public static void Run()
        {
            const float threshold = AutopilotLandPolicy.ManualAxisThreshold;
            TestAssert.That(!AutopilotLandPolicy.ManualAxisOverride(0.2f, -0.2f, 0.2f, threshold),
                "Small stick movement must not release the landing autopilot");
            TestAssert.That(AutopilotLandPolicy.ManualAxisOverride(0.3f, 0f, 0f, threshold) &&
                AutopilotLandPolicy.ManualAxisOverride(0f, -0.3f, 0f, threshold) &&
                AutopilotLandPolicy.ManualAxisOverride(0f, 0f, 0.3f, threshold),
                "Deliberate pitch, roll or yaw must release control");
            TestAssert.That(!AutopilotLandPolicy.ManualAxisOverride(threshold, 0f, 0f, threshold),
                "The threshold itself stays with the autopilot");
            TestAssert.That(AutopilotLandPolicy.ManualAxisOverride(float.NaN, 0f, 0f, threshold),
                "Invalid axes fail toward releasing control");

            TestAssert.That(!AutopilotLandPolicy.VirtualJoystickOverride(
                    AutopilotLandPolicy.VirtualJoystickTravel * threshold, threshold),
                "Virtual joystick inside the deadzone must not release");
            TestAssert.That(AutopilotLandPolicy.VirtualJoystickOverride(
                    AutopilotLandPolicy.VirtualJoystickTravel * threshold + 1f, threshold),
                "Virtual joystick beyond the deadzone must release");

            TestAssert.That(AutopilotLandPolicy.AdjustedLandingSpeed(1000f, 1000f, 30f) == 30f,
                "Full weight uses the book landing speed");
            TestAssert.That(System.Math.Abs(
                    AutopilotLandPolicy.AdjustedLandingSpeed(250f, 1000f, 30f) - 15f) < 0.001f,
                "Landing speed scales with the square root of mass");
            TestAssert.That(AutopilotLandPolicy.AdjustedLandingSpeed(0f, 0f, 30f) == 30f &&
                AutopilotLandPolicy.AdjustedLandingSpeed(float.NaN, 1000f, 30f) == 30f,
                "Unknown mass falls back to the book landing speed");

            TestAssert.That(AutopilotLandPolicy.ApproachThrottle(100f, 100f, 0.9f) == 0.5f,
                "On-speed approach holds half throttle");
            TestAssert.That(AutopilotLandPolicy.ApproachThrottle(0f, 200f, 0.9f) == 0.9f,
                "Slow approach clamps to cruise throttle");
            TestAssert.That(AutopilotLandPolicy.ApproachThrottle(300f, 100f, 0.9f) == 0f,
                "Fast approach clamps to idle");
            TestAssert.That(AutopilotLandPolicy.ApproachThrottle(float.NaN, 100f, 0.9f) == 0f,
                "Invalid speed fails closed to idle");

            TestAssert.That(AutopilotLandPolicy.BrakeRamp(0f) == 0f &&
                AutopilotLandPolicy.BrakeRamp(10f) == 1f &&
                AutopilotLandPolicy.BrakeRamp(-1f) == 0f &&
                AutopilotLandPolicy.BrakeRamp(float.NaN) == 0f,
                "Brake ramps from zero to full and rejects invalid time");

            TestAssert.That(AutopilotLandPolicy.Stopped(2.4f, 4.9f), "Slow and low is stopped");
            TestAssert.That(!AutopilotLandPolicy.Stopped(2.6f, 4.9f), "Rolling speed is not stopped");
            TestAssert.That(!AutopilotLandPolicy.Stopped(2.4f, 5.1f), "Altitude above the ground is not stopped");
            TestAssert.That(!AutopilotLandPolicy.Stopped(float.NaN, 0f), "Invalid speed never reports stopped");
        }
    }
}
