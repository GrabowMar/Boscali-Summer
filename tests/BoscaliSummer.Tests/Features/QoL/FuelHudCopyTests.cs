using BoscaliSummer.Features.QoL.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.QoL
{
    /// <summary>Pins the fuel/divert widget's copy and tone ladder.</summary>
    internal static class FuelHudCopyTests
    {
        public static void Run()
        {
            TestAssert.That(FuelHudCopy.Fuel(0.62f) == "FUEL 62%", "fuel reads as a whole percent");
            TestAssert.That(FuelHudCopy.Fuel(2f) == "FUEL 100%" && FuelHudCopy.Fuel(-1f) == "FUEL 0%",
                "a bad reading clamps instead of over-reading");

            TestAssert.That(FuelHudCopy.Divert("ALPHA", "34km") == "RTB ALPHA · 34km",
                "a resolved field reads with its range");
            TestAssert.That(FuelHudCopy.Divert("ALPHA", null) == "RTB ALPHA" &&
                FuelHudCopy.Divert(null, "34km") == "RTB UNKNOWN",
                "an unresolvable field reads unknown, never a guessed range");

            TestAssert.That(FuelHudCopy.Tone(0.8f) == HudTone.Info, "high fuel is routine");
            TestAssert.That(FuelHudCopy.Tone(0.2f) == HudTone.Caution, "low fuel cautions");
            TestAssert.That(FuelHudCopy.Tone(0.05f) == HudTone.Warning, "critical fuel warns");
            TestAssert.That(FuelHudCopy.Bar(0.5f) == 0.5f && FuelHudCopy.Bar(2f) == 1f,
                "the bar is the clamped fuel level");
        }
    }
}
