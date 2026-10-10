using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>Pins the simulated ILS geometry the APPROACH page's feed is built from.</summary>
    internal static class IlsGuidanceTests
    {
        public static void Run()
        {
            TestAssert.That(IlsGuidance.ClampSlope(5f) == 5f &&
                IlsGuidance.ClampSlope(2f) == IlsGuidance.MinSlope &&
                IlsGuidance.ClampSlope(20f) == IlsGuidance.MaxSlope &&
                IlsGuidance.ClampSlope(float.NaN) == IlsGuidance.DefaultSlope,
                "glideslope stays inside 3–8°");
            float locDeg = IlsGuidance.LocalizerDegrees(100f, 100f);
            TestAssert.That(locDeg > 40f && locDeg < 50f, "equal cross and along is a 45° cut");
            TestAssert.That(IlsGuidance.LocalizerDegrees(-100f, 100f) < 0f, "left of the line reads negative");
            TestAssert.That(IlsGuidance.GlideslopeErrorDegrees(100f, 100f, 45f) < 1f &&
                IlsGuidance.GlideslopeErrorDegrees(100f, 100f, 45f) > -1f,
                "on-slope height matches a 45° path");
            TestAssert.That(IlsGuidance.GlideslopeErrorDegrees(200f, 100f, 45f) > 0f, "above the slope reads positive (high)");
        }
    }
}
