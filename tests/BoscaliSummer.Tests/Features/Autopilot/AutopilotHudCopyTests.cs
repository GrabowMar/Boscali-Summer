using BoscaliSummer.Features.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>Pins the landing-autopilot widget's line composition.</summary>
    internal static class AutopilotHudCopyTests
    {
        public static void Run()
        {
            TestAssert.That(AutopilotHudCopy.Text("FINAL") == "AUTOPILOT · FINAL",
                "the phase word is appended to the owner");
            TestAssert.That(AutopilotHudCopy.Text(null) == "AUTOPILOT · ENGAGED" &&
                AutopilotHudCopy.Text("") == "AUTOPILOT · ENGAGED",
                "a missing phase still reads as engaged");

            TestAssert.That(AutopilotHudCopy.Detail("RUB 27", "6.2km") == "RUB 27 · 6.2km",
                "target and distance join on one line");
            TestAssert.That(AutopilotHudCopy.Detail("RUB 27", null) == "RUB 27" &&
                AutopilotHudCopy.Detail(null, "6.2km") == "6.2km",
                "a missing part is dropped, never dashed");
            TestAssert.That(AutopilotHudCopy.Detail(null, null) == null,
                "nothing to say is no detail line at all");

            TestAssert.That(IlsGuidance.ClampSlope(5f) == 5f &&
                IlsGuidance.ClampSlope(2f) == IlsGuidance.MinSlope &&
                IlsGuidance.ClampSlope(20f) == IlsGuidance.MaxSlope &&
                IlsGuidance.ClampSlope(float.NaN) == IlsGuidance.DefaultSlope,
                "glideslope stays inside 3–8°");
            TestAssert.That(IlsGuidance.Beam(2.5f, 2.5f) == 1f &&
                IlsGuidance.Beam(-5f, 2.5f) == -1f &&
                IlsGuidance.Beam(0f, 2.5f) == 0f,
                "full-scale localizer saturates at ±1");
            float locDeg = IlsGuidance.LocalizerDegrees(100f, 100f);
            TestAssert.That(locDeg > 40f && locDeg < 50f, "equal cross and along is a 45° cut");
            TestAssert.That(IlsGuidance.GlideslopeErrorDegrees(100f, 100f, 45f) < 1f &&
                IlsGuidance.GlideslopeErrorDegrees(100f, 100f, 45f) > -1f,
                "on-slope height matches a 45° path");
            TestAssert.That(IlsGuidance.InsideRadius(100f, 200f) && !IlsGuidance.InsideRadius(201f, 200f),
                "inside the field ring is a hard radius");
            TestAssert.That(IlsGuidance.LocWord(-0.4f) == "LOC L" && IlsGuidance.GsWord(0.4f) == "GS HIGH" &&
                IlsGuidance.LocWord(0f) == "LOC",
                "needle words name the side, never colour alone");
            TestAssert.That(IlsGuidance.Caution(0.6f, 0f) && !IlsGuidance.Caution(0.1f, 0.1f),
                "half-scale deflection is a caution");

            TestAssert.That(IlsHudCopy.Text(false) == "ILS" && IlsHudCopy.Text(true) == "ILS · CORRECT",
                "off-beam ILS asks for a correction");
            TestAssert.That(IlsHudCopy.Detail("LOC L", "GS HIGH", true, "1.2km") ==
                "LOC L · GS HIGH · INSIDE · 1.2km",
                "ILS detail carries words and the ring");
            TestAssert.That(IlsHudCopy.Bar(0f) == 1f && IlsHudCopy.Bar(1f) == 0f && IlsHudCopy.Bar(-0.5f) == 0.5f,
                "on-course fills the bar");
        }
    }
}
