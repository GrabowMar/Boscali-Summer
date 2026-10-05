using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class PilotPoseTests
    {
        public static void Run()
        {
            TestAssert.That(Math.Abs(PilotPoseMath.ElbowDegrees(1, 1, (float)Math.Sqrt(2)) - 90) < .001f,
                "A reachable arm uses a right angle for equal limbs and sqrt-two reach.");
            TestAssert.That(PilotPoseMath.ElbowDegrees(1, 1, 100) >= 0 && PilotPoseMath.ElbowDegrees(1, 1, 0) <= 175,
                "Unreachable and folded arms remain bounded.");
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                TestAssert.That(PilotPoseMath.ElbowDegrees(bad, 1, 1) == 0 &&
                    PilotPoseMath.Smoothing(bad, 8) == 0 && PilotPoseMath.TorsoDegrees(bad, false) == 0,
                    "Invalid measurements produce finite neutral values.");
            }
            for (int i = -100; i <= 100; i++)
            {
                TestAssert.That(Math.Abs(PilotPoseMath.BreathMetres(i, false)) <= .00301f &&
                    PilotPoseMath.BreathMetres(i, true) == 0 && Math.Abs(PilotPoseMath.TorsoDegrees(i, false)) <= 2 &&
                    Math.Abs(PilotPoseMath.TorsoDegrees(i, true)) <= .5f,
                    "Body motion respects its full and comfort limits.");
            }
            TestAssert.That(PilotPoseMath.Smoothing(0, 8) == 0 && PilotPoseMath.Smoothing(-1, 8) == 0 &&
                PilotPoseMath.Smoothing(2, 8) == 0, "Pause, rewind and long frame gaps cannot advance pose.");
            float twice = 1 - (1 - PilotPoseMath.Smoothing(.01f, 8)) * (1 - PilotPoseMath.Smoothing(.01f, 8));
            TestAssert.That(Math.Abs(twice - PilotPoseMath.Smoothing(.02f, 8)) < .00001f,
                "Pose smoothing is independent of frame subdivision.");
            TestAssert.That(!PilotPoseMath.CaptureDue(0.099, 0) && PilotPoseMath.CaptureDue(.1, 0) &&
                !PilotPoseMath.CaptureDue(double.NaN, 0) && !PilotPoseMath.CaptureDue(-1, 0),
                "Capture never exceeds ten Hz and rejects invalid or reversed clocks.");
        }
    }
}
