using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Pins the theater widget's effort line, phase words and countdown.</summary>
    internal static class TheaterOpsHudCopyTests
    {
        public static void Run()
        {
            TestAssert.That(TheaterOpsHudCopy.Effort(true, "HILL 402") == "MAIN EFFORT · HILL 402",
                "a set main effort reads by name");
            TestAssert.That(TheaterOpsHudCopy.Effort(false, null) == "MAIN EFFORT · NONE" &&
                TheaterOpsHudCopy.Effort(true, null) == "MAIN EFFORT · NONE",
                "no set effort reads none, never a guessed label");

            TestAssert.That(TheaterOpsHudCopy.PhaseWord(TheaterOperationPhase.Mustering) == "MUSTERING" &&
                TheaterOpsHudCopy.PhaseWord(TheaterOperationPhase.AwaitingTarget) == "AWAITING TARGET" &&
                TheaterOpsHudCopy.PhaseWord(TheaterOperationPhase.Assault) == "ASSAULT" &&
                TheaterOpsHudCopy.PhaseWord(TheaterOperationPhase.Holding) == "HOLDING",
                "every offensive phase has its own word");

            TestAssert.That(TheaterOpsHudCopy.Detail("BREAKWATER", TheaterOperationPhase.Launching, 90f) ==
                "OP BREAKWATER · LAUNCHING · T-1:30",
                "name, phase and countdown join on one line");
            TestAssert.That(TheaterOpsHudCopy.Detail("BREAKWATER", TheaterOperationPhase.Planning, -1f) ==
                "OP BREAKWATER · PLANNING",
                "a plan with no countdown drops the clock");
            TestAssert.That(TheaterOpsHudCopy.Detail(null, TheaterOperationPhase.Assault, 0f) == null,
                "no operation is no detail line at all");
            TestAssert.That(TheaterOpsHudCopy.Clock(45f) == "T-45s" && TheaterOpsHudCopy.Clock(0f) == "",
                "the countdown matches the module's own clock copy");
            TestAssert.That(TheaterOpsHudCopy.Bar(0.5f) == 0.5f && TheaterOpsHudCopy.Bar(2f) == 1f,
                "the bar is the clamped offensive progress");
        }
    }
}
