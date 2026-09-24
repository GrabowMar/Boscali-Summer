using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Pins the support widget's cooldown copy and readiness tone.</summary>
    internal static class SupportHudCopyTests
    {
        public static void Run()
        {
            TestAssert.That(SupportHudCopy.Cooldown(12f) == "T-12s", "seconds under a minute");
            TestAssert.That(SupportHudCopy.Cooldown(65f) == "T-1:05", "past a minute reads minutes:seconds");
            TestAssert.That(SupportHudCopy.Cooldown(0f) == "" && SupportHudCopy.Cooldown(-1f) == "",
                "a spent cooldown prints nothing");

            TestAssert.That(SupportHudCopy.Tone(true, 0f) == HudTone.Caution, "a request in flight cautions");
            TestAssert.That(SupportHudCopy.Tone(false, 30f) == HudTone.Caution, "a cooling net cautions");
            TestAssert.That(SupportHudCopy.Tone(false, 0f) == HudTone.Info, "a ready net is routine");

            TestAssert.That(SupportHudCopy.Bar(10f, 30f, 0f) > 0.6f,
                "the bar drains toward ready while cooling");
            TestAssert.That(SupportHudCopy.Bar(0f, 0f, 0.4f) == 0.4f,
                "with no cooldown the bar is the allocation");
            TestAssert.That(SupportHudCopy.Percent(0.25f) == "25%", "allocation reads as a percent");
        }
    }
}
