using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>Leading and trailing edge throttle of the overlay rebuild (annex 05 P4, gate tests).</summary>
    internal static class RebuildGateTests
    {
        public static void Run()
        {
            TheSequenceOfTheAnnex();
            ANoChangeNeverFires();
            AHeldChangeFiresOnceWhenTheIntervalEnds();
            ResetForgetsTheLastBuild();
            AZeroIntervalFiresOnEveryChange();
            ARevisionThatMovesBackIsAChange();
            AClockThatGoesBackwardsNeverFreezesTheGate();
        }

        private static void TheSequenceOfTheAnnex()
        {
            var gate = new RebuildGate(0.066f);
            TestAssert.That(gate.ShouldRebuild(1, 0f), "the first revision fires at once (leading edge)");
            TestAssert.That(!gate.ShouldRebuild(2, 0.03f), "a change inside the interval is held back");
            TestAssert.That(gate.ShouldRebuild(2, 0.07f), "and fires when the interval has ended (trailing edge)");
            TestAssert.That(!gate.ShouldRebuild(2, 0.2f), "an unchanged revision does not fire again");
        }

        private static void ANoChangeNeverFires()
        {
            var gate = new RebuildGate(0.066f);
            gate.ShouldRebuild(7, 0f);
            for (int i = 1; i <= 50; i++)
                TestAssert.That(!gate.ShouldRebuild(7, i * 0.5f), "revision 7 stays quiet at " + i * 0.5f);
        }

        private static void AHeldChangeFiresOnceWhenTheIntervalEnds()
        {
            var gate = new RebuildGate(0.1f);
            gate.ShouldRebuild(1, 0f);
            TestAssert.That(!gate.ShouldRebuild(2, 0.02f) && !gate.ShouldRebuild(3, 0.05f) && !gate.ShouldRebuild(4, 0.09f), "a burst of changes inside the interval is held");
            TestAssert.That(!gate.ShouldRebuild(4, 0.0999f), "just short of the interval it is still held");
            TestAssert.That(gate.ShouldRebuild(4, 0.1f), "at the interval the newest revision fires");
            TestAssert.That(!gate.ShouldRebuild(4, 0.11f) && !gate.ShouldRebuild(4, 5f), "and only once");
            TestAssert.That(!gate.ShouldRebuild(5, 0.15f), "a change right after a fire waits for its own interval");
            TestAssert.That(gate.ShouldRebuild(5, 0.2f), "and then fires");
        }

        private static void ResetForgetsTheLastBuild()
        {
            var gate = new RebuildGate(0.5f);
            gate.ShouldRebuild(3, 10f);
            TestAssert.That(!gate.ShouldRebuild(3, 11f), "revision 3 was built");
            gate.Reset();
            TestAssert.That(gate.ShouldRebuild(3, 11.01f), "after a reset the same revision builds again at once");
            TestAssert.That(!gate.ShouldRebuild(3, 11.02f), "and then is quiet");
        }

        private static void AZeroIntervalFiresOnEveryChange()
        {
            var gate = new RebuildGate(0f);
            TestAssert.That(gate.ShouldRebuild(1, 0f) && gate.ShouldRebuild(2, 0f) && gate.ShouldRebuild(3, 0f), "with no interval every change fires, even in one instant");
            TestAssert.That(!gate.ShouldRebuild(3, 1f), "an unchanged revision still does not");
            var negative = new RebuildGate(-1f);
            TestAssert.That(negative.ShouldRebuild(1, 0f) && negative.ShouldRebuild(2, 0f), "a negative interval behaves as zero");
        }

        private static void ARevisionThatMovesBackIsAChange()
        {
            var gate = new RebuildGate(0.05f);
            gate.ShouldRebuild(5, 0f);
            TestAssert.That(gate.ShouldRebuild(2, 1f), "a revision that went down (a rebuilt source) is still a change");
            TestAssert.That(gate.ShouldRebuild(int.MinValue, 2f) && gate.ShouldRebuild(int.MaxValue, 3f), "and so is a wrapped one");
        }

        private static void AClockThatGoesBackwardsNeverFreezesTheGate()
        {
            var gate = new RebuildGate(0.5f);
            gate.ShouldRebuild(1, 100f);
            TestAssert.That(gate.ShouldRebuild(2, 3f), "a change after the clock restarted fires instead of waiting for the old time to come round");
            TestAssert.That(!gate.ShouldRebuild(2, 3.1f), "and the new clock is the one it measures from");
            TestAssert.That(gate.ShouldRebuild(3, 3.6f), "so the next change fires one interval later");
            TestAssert.That(!gate.ShouldRebuild(4, float.NaN), "a clock that is not a number fires nothing");
        }
    }
}
