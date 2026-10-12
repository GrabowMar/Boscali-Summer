using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    /// <summary>Annex 04 W2: the fold of one sortie's wing tasking, stepped at 1 Hz.</summary>
    internal static class WingSortieTallyTests
    {
        public static void Run()
        {
            BeginStartsATaskedTasking();
            OnStationTogglesAndFallsBack();
            AGoneDestroyTargetEndsDone();
            CoverAndObserveIgnoreTargetGone();
            TheDeadlineEndsDoneAndZeroNeverDoes();
            NoWingmanLeftIsFailed();
            StepPrecedenceIsTargetThenClockThenWingmen();
            EndReturnsTrueOnceAndOnlyForFinalStates();
            AnEndedTaskingNeverChanges();
            BeginReplacesAndClearResets();
            WingmenAreUnknownUntilTheFirstStep();
        }

        private static WingSortieTally Begun(WingSortieIntent intent, float deadline = 0f)
        {
            var tally = new WingSortieTally();
            tally.Begin(41, intent, deadline);
            return tally;
        }

        private static void BeginStartsATaskedTasking()
        {
            var tally = new WingSortieTally();
            TestAssert.That(!tally.Active && tally.State == WingSortieState.None && !tally.Ended && tally.SortieId == 0, "a new tally has no tasking");
            TestAssert.That(tally.Step(10f, 2, false, true) == WingSortieState.None, "stepping with no tasking stays None");
            tally.Begin(41, WingSortieIntent.Destroy, 0f);
            TestAssert.That(tally.Active && tally.SortieId == 41 && tally.Intent == WingSortieIntent.Destroy, "Begin records the sortie and its intent");
            TestAssert.That(tally.State == WingSortieState.Tasked && !tally.Ended && !tally.TargetDown, "and starts Tasked");
        }

        private static void OnStationTogglesAndFallsBack()
        {
            var tally = Begun(WingSortieIntent.Cover);
            TestAssert.That(tally.Step(1f, 2, false, false) == WingSortieState.Tasked, "on the way reads Tasked");
            TestAssert.That(tally.Step(2f, 2, false, true) == WingSortieState.OnStation, "inside the circle reads OnStation");
            TestAssert.That(tally.Step(3f, 2, false, false) == WingSortieState.Tasked, "leaving the circle falls back to Tasked (hold credit stops)");
            TestAssert.That(tally.Step(4f, 2, false, true) == WingSortieState.OnStation && tally.Wingmen == 2, "and returns to OnStation with the wingman count");
            TestAssert.That(!tally.Ended, "none of this ends the tasking");
        }

        private static void AGoneDestroyTargetEndsDone()
        {
            var tally = Begun(WingSortieIntent.Destroy);
            TestAssert.That(tally.Step(5f, 2, true, true) == WingSortieState.Done, "a Destroy target gone is Done");
            TestAssert.That(tally.TargetDown && tally.Ended, "with TargetDown set and the tasking ended");
        }

        private static void CoverAndObserveIgnoreTargetGone()
        {
            foreach (WingSortieIntent intent in new[] { WingSortieIntent.Cover, WingSortieIntent.Observe })
            {
                var tally = Begun(intent);
                TestAssert.That(tally.Step(5f, 2, true, true) == WingSortieState.OnStation && !tally.TargetDown && !tally.Ended, intent + " ignores targetGone");
            }
        }

        private static void TheDeadlineEndsDoneAndZeroNeverDoes()
        {
            var timed = Begun(WingSortieIntent.Cover, 100f);
            TestAssert.That(timed.Step(99.9f, 2, false, true) == WingSortieState.OnStation, "before the deadline the tasking runs");
            TestAssert.That(timed.Step(100f, 2, false, true) == WingSortieState.Done && !timed.TargetDown, "at the deadline the tasking is Done (and no target is claimed down)");
            foreach (float none in new[] { 0f, -5f, float.NaN })
            {
                var open = Begun(WingSortieIntent.Cover, none);
                TestAssert.That(open.Step(1e9f, 2, false, true) == WingSortieState.OnStation, "deadline " + none + " means none");
            }
        }

        private static void NoWingmanLeftIsFailed()
        {
            var tally = Begun(WingSortieIntent.Destroy);
            TestAssert.That(tally.Step(5f, 0, false, false) == WingSortieState.Failed && tally.Ended && tally.Wingmen == 0, "no wingman flying is Failed");
            var negative = Begun(WingSortieIntent.Destroy);
            TestAssert.That(negative.Step(5f, -3, false, false) == WingSortieState.Failed && negative.Wingmen == 0, "a negative count reads as none flying");
        }

        private static void StepPrecedenceIsTargetThenClockThenWingmen()
        {
            var all = Begun(WingSortieIntent.Destroy, 10f);
            TestAssert.That(all.Step(20f, 0, true, false) == WingSortieState.Done && all.TargetDown, "target gone outranks the clock and an empty wing");
            var clockAndEmpty = Begun(WingSortieIntent.Destroy, 10f);
            TestAssert.That(clockAndEmpty.Step(20f, 0, false, false) == WingSortieState.Done && !clockAndEmpty.TargetDown, "the clock outranks an empty wing");
        }

        private static void EndReturnsTrueOnceAndOnlyForFinalStates()
        {
            var tally = Begun(WingSortieIntent.Destroy);
            TestAssert.That(!tally.End(WingSortieState.None) && !tally.End(WingSortieState.Tasked) && !tally.End(WingSortieState.OnStation) && !tally.Ended,
                "only Done, Failed and Recalled end a tasking");
            TestAssert.That(tally.End(WingSortieState.Done) && tally.State == WingSortieState.Done && tally.Ended, "End as Done returns true once");
            TestAssert.That(!tally.End(WingSortieState.Done) && !tally.End(WingSortieState.Recalled), "and never again (the FormUp is issued exactly once)");
            TestAssert.That(tally.State == WingSortieState.Done, "a second End changes nothing");
            var none = new WingSortieTally();
            TestAssert.That(!none.End(WingSortieState.Recalled), "ending a tally that has no tasking does nothing");
            var recalled = Begun(WingSortieIntent.Cover);
            TestAssert.That(recalled.End(WingSortieState.Recalled) && recalled.State == WingSortieState.Recalled, "Recalled ends it too");
        }

        private static void AnEndedTaskingNeverChanges()
        {
            var tally = Begun(WingSortieIntent.Destroy);
            tally.Step(1f, 2, false, true);
            tally.End(WingSortieState.Recalled);
            TestAssert.That(tally.Step(2f, 0, true, true) == WingSortieState.Recalled, "stepping an ended tasking returns its final state");
            TestAssert.That(!tally.TargetDown && tally.Wingmen == 2 && tally.Ended, "and leaves its numbers as they were");
            TestAssert.That(tally.Active && tally.SortieId == 41, "an ended tasking stays readable");
        }

        private static void BeginReplacesAndClearResets()
        {
            var tally = Begun(WingSortieIntent.Destroy, 100f);
            tally.Step(1f, 2, true, true);
            tally.Begin(42, WingSortieIntent.Cover, 0f);
            TestAssert.That(tally.SortieId == 42 && tally.Intent == WingSortieIntent.Cover && tally.State == WingSortieState.Tasked &&
                            !tally.Ended && !tally.TargetDown, "Begin replaces the earlier tasking whole");
            TestAssert.That(tally.Step(1e6f, 2, false, true) == WingSortieState.OnStation, "including its deadline");
            tally.Clear();
            TestAssert.That(!tally.Active && tally.State == WingSortieState.None && !tally.Ended && !tally.TargetDown &&
                            tally.SortieId == 0 && tally.Intent == WingSortieIntent.None, "Clear forgets everything");
        }

        private static void WingmenAreUnknownUntilTheFirstStep()
        {
            var tally = Begun(WingSortieIntent.Cover);
            TestAssert.That(tally.Wingmen == -1, "before the first step the wingman count is unknown (-1), not a confident zero");
            tally.Step(1f, 3, false, false);
            TestAssert.That(tally.Wingmen == 3, "the first step reads it");
        }
    }
}
