using BoscaliSummer.Features.Autopilot.Domain;
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

            LandingEnergy.Result stall = LandingEnergy.Compute(20f, 40f, 40f, 0.9f, 80f, false, false, false, 0f);
            TestAssert.That(stall.Throttle >= 0.55f && stall.Brake == 0f, "below stall must add power");
            LandingEnergy.Result hot = LandingEnergy.Compute(90f, 40f, 40f, 0.9f, 80f, false, false, false, 0f);
            TestAssert.That(hot.Throttle == 0f && hot.Brake == 1f, "hot and high must idle and board");
            LandingEnergy.Result roll = LandingEnergy.Compute(10f, 0f, 40f, 0.9f, 2f, false, true, false, 0f);
            TestAssert.That(roll.Throttle == 0f && roll.Brake == 1f, "rollout on the deck is full brake");
            LandingEnergy.Result flare = LandingEnergy.Compute(42f, 40f, 40f, 0.9f, 20f, true, false, true, 0f);
            TestAssert.That(flare.Throttle <= 0.28f, "carrier flare caps throttle");
            TestAssert.That(LandingEnergy.SpeedScale(0f) == 1f &&
                System.Math.Abs(LandingEnergy.SpeedScale(1f) - 0.85f) < 0.001f,
                "damage slows the book landing speed");
            TestAssert.That(System.Math.Abs(LandingEnergy.BankLimit(100f, 1f) - 60f) < 0.001f, "full damage cuts bank to 60%");
            TestAssert.That(LandingEnergy.Compute(float.NaN, 40f, 40f, 0.9f, 80f, false, false, false, 0f).Throttle == 0f,
                "invalid speed fails closed");

            TestAssert.That(AirframeSeverity.FromParts(0, 0f, 0f, 0) == 0f, "no parts is undamaged");
            TestAssert.That(System.Math.Abs(AirframeSeverity.FromParts(4, 40f, 100f, 0) - 0.42f) < 0.001f,
                "HP loss is 70% of severity");
            TestAssert.That(System.Math.Abs(AirframeSeverity.FromParts(4, 100f, 100f, 4) - 0.3f) < 0.001f,
                "all detached is 30%");
            TestAssert.That(AirframeSeverity.FromParts(2, float.NaN, 10f, 0) == 0f, "invalid HP is zero");

            TestAssert.That(CarrierPattern.Next(CarrierLeg.Upwind) == CarrierLeg.Crosswind &&
                CarrierPattern.Next(CarrierLeg.Base) == CarrierLeg.Final &&
                CarrierPattern.Next(CarrierLeg.Final) == CarrierLeg.Final,
                "pattern legs advance once and stop at final");
            TestAssert.That(CarrierPattern.Word(CarrierLeg.Downwind) == "DOWNWIND", "leg word is the HUD phase");
            TestAssert.That(CarrierPattern.PatternSide(-10f) == -1f && CarrierPattern.PatternSide(1f) == 1f,
                "pattern side follows the aircraft's current offset");
            TestAssert.That(CarrierPattern.DirectFinal(1000f, 10f) && !CarrierPattern.DirectFinal(4000f, 10f),
                "already on final skips the rectangle");
            TestAssert.That(CarrierPattern.ShouldAdvance(100f, 10f, 1f), "inside the gate with heading aligned advances");
            TestAssert.That(CarrierPattern.ShouldAdvance(2000f, 90f, 46f), "stuck-leg timeout advances");
            TestAssert.That(!CarrierPattern.ShouldAdvance(2000f, 90f, 10f), "far and unaligned stays on the leg");
            CarrierPattern.Gate(CarrierLeg.Downwind, 0f, 1f, 1f, out float gx, out float gy, out float gz);
            TestAssert.That(gy == 300f && gx > 1000f && gz < 0f, "downwind is offset right and behind the deck");
        }
    }
}
