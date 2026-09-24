using BoscaliSummer.Features.Progression.Domain;

namespace BoscaliSummer.Tests.Features.Progression
{
    /// <summary>Pins the ace-hunt widget's line composition.</summary>
    internal static class AceHuntHudCopyTests
    {
        public static void Run()
        {
            TestAssert.That(AceHuntHudCopy.Text("VULTURE", "3RD WING") == "ACE HUNT · VULTURE",
                "the ace name leads the line");
            TestAssert.That(AceHuntHudCopy.Text(null, "3RD WING") == "ACE HUNT · 3RD WING" &&
                AceHuntHudCopy.Text(null, null) == "ACE HUNT · UNKNOWN",
                "an unnamed ace falls back to the wing, then to unknown");

            TestAssert.That(AceHuntHudCopy.Detail(3, 3, 4, "HUNTING") == "TIER 3 · 3/4 UP · HUNTING",
                "tier, strength and status join on one line");
            TestAssert.That(AceHuntHudCopy.Detail(2, 0, 0, null) == "TIER 2",
                "a wing with no size drops the strength and status parts");
            TestAssert.That(AceHuntHudCopy.Bar(3, 4) == 0.75f && AceHuntHudCopy.Bar(1, 0) == 0f,
                "the bar is the share of the wing still up");
        }
    }
}
