using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The last closed operations: a sortie tied to one still credits the wave bank for 120 s (ruling D4, annex 03 C12).</summary>
    internal static class FocusHistoryTests
    {
        public static void Run()
        {
            ALiveOperationIsNotClosed();
            ACloseOpensTheWindowAndTheBoundaryIsInclusive();
            ARedirectChainKeepsTheEarlierOperation();
            TheRingOfFourEvictsTheOldest();
            NothingToStampWhenNothingChanged();
            UnusableIdsAndClocksFailClosed();
            ResetForgetsEverything();
        }

        private static void ALiveOperationIsNotClosed()
        {
            var history = new FocusHistory();
            history.Observe(5, 10f);
            TestAssert.That(!history.Recent(5, 11f), "an operation that is still live is covered by the focus itself, not by the window");
            TestAssert.That(FocusHistory.Capacity == 4 && FocusHistory.LateWindowSeconds == 120f, "four entries and a 120 s window");
        }

        private static void ACloseOpensTheWindowAndTheBoundaryIsInclusive()
        {
            var history = new FocusHistory();
            history.Observe(7, 10f);
            history.Observe(0, 100f);
            TestAssert.That(history.Recent(7, 100f), "a sortie finishing the moment the operation closes still counts");
            TestAssert.That(history.Recent(7, 219.99f), "and inside the window");
            TestAssert.That(history.Recent(7, 220f), "exactly 120 s after the close still counts");
            TestAssert.That(!history.Recent(7, 220.01f), "a hair later does not");
            TestAssert.That(!history.Recent(7, 99f), "a clock before the close (state from another mission) never counts");
            TestAssert.That(!history.Recent(8, 150f), "an operation that never lived is not recent");
        }

        private static void ARedirectChainKeepsTheEarlierOperation()
        {
            var history = new FocusHistory();
            history.Observe(1, 0f);
            history.Observe(2, 50f);
            history.Observe(3, 90f);
            TestAssert.That(history.Recent(1, 150f) && history.Recent(2, 150f), "A to B to C keeps A and B for their own windows");
            TestAssert.That(!history.Recent(3, 150f), "C is live, so it is not in the window");
            TestAssert.That(history.Recent(1, 170f) && !history.Recent(1, 170.5f), "A was closed at 50, so its window ends at 170");
            TestAssert.That(history.Recent(2, 210f) && !history.Recent(2, 211f), "B was closed at 90, so its window ends at 210");
        }

        private static void TheRingOfFourEvictsTheOldest()
        {
            var history = new FocusHistory();
            for (int id = 1; id <= 6; id++) history.Observe(id, id * 10f);
            // 1..5 have closed (at 20..60); four fit, so 1 is evicted although it is inside its window.
            TestAssert.That(!history.Recent(1, 65f), "the fifth close evicts the oldest entry");
            TestAssert.That(history.Recent(2, 65f) && history.Recent(3, 65f) && history.Recent(4, 65f) && history.Recent(5, 65f),
                "the four newest closed operations remain");
            history.Observe(0, 70f);
            TestAssert.That(!history.Recent(2, 71f) && history.Recent(3, 71f) && history.Recent(6, 71f),
                "closing the sixth evicts the second");
        }

        private static void NothingToStampWhenNothingChanged()
        {
            var history = new FocusHistory();
            history.Observe(0, 5f);
            history.Observe(9, 10f);
            history.Observe(9, 500f);
            history.Observe(9, 900f);
            history.Observe(0, 1000f);
            history.Observe(0, 1050f);
            TestAssert.That(history.Recent(9, 1100f), "the close is stamped once, when the id changed");
            TestAssert.That(!history.Recent(9, 1121f), "repeated observations of no operation do not refresh the stamp");
        }

        private static void UnusableIdsAndClocksFailClosed()
        {
            var history = new FocusHistory();
            history.Observe(4, 10f);
            history.Observe(0, 20f);
            TestAssert.That(!history.Recent(0, 30f) && !history.Recent(-4, 30f), "an untied sortie (id 0) or a negative id is never recent");
            TestAssert.That(!history.Recent(4, float.NaN) && !history.Recent(4, float.PositiveInfinity), "an unreadable clock never counts");
            history.Observe(5, float.NaN);
            history.Observe(0, float.PositiveInfinity);
            history.Observe(-3, 25f);
            TestAssert.That(history.Recent(4, 30f) && !history.Recent(5, 30f), "an unreadable observation changes nothing, a negative id reads as none");
        }

        private static void ResetForgetsEverything()
        {
            var history = new FocusHistory();
            history.Observe(1, 0f);
            history.Observe(0, 10f);
            history.Observe(2, 15f);
            history.Reset();
            TestAssert.That(!history.Recent(1, 20f), "a scene or mission change forgets the closed operations");
            history.Observe(0, 30f);
            TestAssert.That(!history.Recent(2, 31f), "and the operation that was live, so it is not stamped closed afterwards");
        }
    }
}
