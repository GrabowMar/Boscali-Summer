using System;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    /// <summary>Annex 04 W2: the action table, the numbers, and every refusal and status word of SEND WING.</summary>
    internal static class WingSortieRulesTests
    {
        private static readonly WingSortieIntent[] Intents =
            { WingSortieIntent.None, WingSortieIntent.Destroy, WingSortieIntent.Cover, WingSortieIntent.Observe };

        private static readonly WingSortieState[] States =
            { WingSortieState.None, WingSortieState.Tasked, WingSortieState.OnStation, WingSortieState.Done, WingSortieState.Failed, WingSortieState.Recalled };

        public static void Run()
        {
            ActionTableCoversEveryIntent();
            EveryConstantSentenceIsBoundedAndTheRefusalsNameTheirFix();
            TheNumbersAreTheFrozenOnes();
            AttackReachIsAShareOfTheLeashAndFailsClosed();
            TooFarNamesBothDistances();
            WordsFitTheCardForEveryStateAndIntent();
            WordsReadAsTheAnnexLines();
            AnUnknownWingmanCountPrintsNoCount();
        }

        private static void ActionTableCoversEveryIntent()
        {
            TestAssert.That(WingSortieRules.ActionFor(WingSortieIntent.Destroy, true) == SortieAction.Attack, "a tracked Destroy target is attacked");
            TestAssert.That(WingSortieRules.ActionFor(WingSortieIntent.Destroy, false) == SortieAction.Scout, "an untracked Destroy target is scouted, not attacked");
            foreach (bool tracked in new[] { true, false })
            {
                TestAssert.That(WingSortieRules.ActionFor(WingSortieIntent.Cover, tracked) == SortieAction.Cap, "Cover flies a CAP whether or not anything is tracked");
                TestAssert.That(WingSortieRules.ActionFor(WingSortieIntent.Observe, tracked) == SortieAction.Scout, "Observe scouts");
                TestAssert.That(WingSortieRules.ActionFor(WingSortieIntent.None, tracked) == SortieAction.None, "None is refused");
                TestAssert.That(WingSortieRules.ActionFor((WingSortieIntent)99, tracked) == SortieAction.None, "an undefined intent is refused");
            }
            foreach (WingSortieIntent intent in Enum.GetValues(typeof(WingSortieIntent)))
                TestAssert.That(Array.IndexOf(Intents, intent) >= 0, "add " + intent + " to the action table and to this test");
        }

        private static void EveryConstantSentenceIsBoundedAndTheRefusalsNameTheirFix()
        {
            int seen = 0;
            foreach (FieldInfo field in typeof(WingSortieRules).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string)) continue;
                string text = (string)field.GetValue(null);
                TestAssert.That(!string.IsNullOrEmpty(text) && text.Length <= 100, field.Name + " is non-empty and at most 100 characters");
                seen++;
            }
            TestAssert.That(seen == 12, "the twelve fixed sentences are all covered (found " + seen + ")");
            TestAssert.That(WingSortieRules.NoWing.IndexOf("recruit", StringComparison.OrdinalIgnoreCase) >= 0, "the no-wing refusal says to recruit");
            TestAssert.That(WingSortieRules.NotAccepted.IndexOf("accept", StringComparison.OrdinalIgnoreCase) >= 0, "the not-accepted refusal says to accept");
        }

        private static void TheNumbersAreTheFrozenOnes()
        {
            TestAssert.Near(WingSortieRules.CoverRadius, 15000f, "the cover circle is 15 km until the sim calibrates it");
            TestAssert.That(WingSortieRules.CoverRadius >= 2000f && WingSortieRules.CoverRadius <= 40000f, "and sits inside the area guard 2..40 km");
            TestAssert.Near(WingSortieRules.ReachShare, 0.9f, "a target may be attacked within 90 percent of the leash");
        }

        private static void AttackReachIsAShareOfTheLeashAndFailsClosed()
        {
            TestAssert.Near(WingSortieRules.AttackReach(20000f), 18000f, "90 percent of the 20 km combat leash");
            TestAssert.Near(WingSortieRules.AttackReach(10000f), 9000f, "it follows the leash it is given");
            TestAssert.That(WingSortieRules.AttackReach(float.NaN) == 0f && WingSortieRules.AttackReach(-1f) == 0f &&
                            WingSortieRules.AttackReach(0f) == 0f && WingSortieRules.AttackReach(float.PositiveInfinity) == 0f,
                "a leash that cannot be read gives no reach rather than an unlimited one");
        }

        private static void TooFarNamesBothDistances()
        {
            TestAssert.Eq(WingSortieRules.TooFar(34000f, 18000f), "Target is 34 km away: the wing will not follow past 18 km. Close in first.", "the annex example");
            TestAssert.That(WingSortieRules.TooFar(34400f, 18000f).Contains("34 km") && WingSortieRules.TooFar(34600f, 18000f).Contains("35 km"), "distance rounds to whole kilometres");
            TestAssert.That(WingSortieRules.TooFar(34500f, 18000f).Contains("35 km"), "half a kilometre rounds up");
            string unknown = WingSortieRules.TooFar(float.NaN, 18000f);
            TestAssert.That(unknown.Contains("Target is - km") && unknown.Contains("18 km"), "a distance that cannot be read prints a dash, not a number");
            TestAssert.That(WingSortieRules.TooFar(float.MaxValue, float.MaxValue).Length <= 100, "absurd distances still fit a refusal");
            TestAssert.That(WingSortieRules.TooFar(34000f, 18000f).Length <= 100, "the refusal fits 100 characters");
        }

        private static void WordsFitTheCardForEveryStateAndIntent()
        {
            foreach (WingSortieState state in Enum.GetValues(typeof(WingSortieState)))
                TestAssert.That(Array.IndexOf(States, state) >= 0, "add " + state + " to the word table and to this test");
            foreach (WingSortieState state in States)
                foreach (WingSortieIntent intent in Intents)
                    foreach (int wingmen in new[] { -1, 0, 1, 2, 9, 99, int.MaxValue, int.MinValue })
                    {
                        string words = WingSortieRules.Words(state, intent, wingmen);
                        TestAssert.That(words.Length <= 40, state + "/" + intent + "/" + wingmen + " fits 40 characters (" + words.Length + ")");
                        TestAssert.That((state == WingSortieState.None) == (words.Length == 0), state + " is empty exactly for state None");
                    }
            TestAssert.Eq(WingSortieRules.Words((WingSortieState)99, WingSortieIntent.Destroy, 2), "", "an undefined state says nothing");
        }

        private static void WordsReadAsTheAnnexLines()
        {
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.Tasked, WingSortieIntent.Destroy, 2), "WING TASKED · 2 FLYING", "tasked");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.OnStation, WingSortieIntent.Destroy, 2), "WING ATTACKING · TARGET STILL UP", "attacking");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.OnStation, WingSortieIntent.Cover, 3), "WING ON STATION · 3 FLYING", "on station (cover)");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.OnStation, WingSortieIntent.Observe, 1), "WING ON STATION · 1 FLYING", "on station (observe)");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.Done, WingSortieIntent.Destroy, 2), "WING: TARGET DOWN · FORMING UP", "done, target down");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.Done, WingSortieIntent.Cover, 2), "WING: SORTIE OVER · FORMING UP", "done otherwise");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.Failed, WingSortieIntent.Destroy, 0), "WING: NONE LEFT FLYING THIS", "failed");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.Recalled, WingSortieIntent.Cover, 2), "WING RECALLED", "recalled");
        }

        private static void AnUnknownWingmanCountPrintsNoCount()
        {
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.Tasked, WingSortieIntent.Destroy, -1), "WING TASKED", "tasked before the first step prints no count");
            TestAssert.Eq(WingSortieRules.Words(WingSortieState.OnStation, WingSortieIntent.Cover, -1), "WING ON STATION", "on station with an unknown count prints no count");
            TestAssert.That(!WingSortieRules.Words(WingSortieState.Tasked, WingSortieIntent.Cover, -1).Contains("0"), "and never a confident zero");
        }
    }
}
