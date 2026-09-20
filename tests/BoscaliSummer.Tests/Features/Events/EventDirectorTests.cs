using System.Collections.Generic;
using BoscaliSummer.Features.Events.Domain;

namespace BoscaliSummer.Tests.Features.Events
{
    internal static class EventDirectorTests
    {
        private static readonly TheaterBalance Contested =
            new TheaterBalance(2, 5, 2, 111, 222, 3);

        private static readonly TheaterBalance Balanced =
            new TheaterBalance(2, 3, 3, 111, 222, 1);

        public static void Run()
        {
            BalanceResolvesTargets();
            SuperEligibilityIsGated();
            TargetedSupersNeedALosingSide();
            OpeningRollAlwaysMovesAPrice();
            SupersAreCappedAndSpaced();
            DisabledMeansNeverSuper();
            RecentEntriesAreAvoided();
            SelectionIsSeederDeterministic();
        }

        private static void OpeningRollAlwaysMovesAPrice()
        {
            var recent = new List<int>(EventSelector.RecentWindow);
            for (int i = 0; i < 256; i++)
            {
                var state = new DirectorState(0.5f, 0, -9999f, Contested, openingRoll: true);
                int index = EventDirector.Select(
                    (uint)(i * 7013 + 29), EventCatalog.All, recent, new HashSet<int>(), true, state);
                EventDefinition entry = EventCatalog.At(index);
                TestAssert.That(entry.Tier != EventTier.Super,
                    "no super at the opening bell: the situation does not exist yet");
                TestAssert.That(System.Math.Abs(entry.SupportCostMultiplier - 1f) > 0.05f,
                    "the mission opens on something that actually moves a price");
                recent.Add(index);
                if (recent.Count > EventSelector.RecentWindow) recent.RemoveAt(0);
            }
        }

        private static void BalanceResolvesTargets()
        {
            TestAssert.That(Contested.Deficit == 3, "deficit is the ground gap");
            TestAssert.That(Contested.Contested, "a clear gap is a contested theater");
            TestAssert.That(Contested.HashFor(EventTarget.Losing) == 222, "the losing hash resolves");
            TestAssert.That(Contested.HashFor(EventTarget.Leading) == 111, "the leading hash resolves");
            TestAssert.That(Contested.HashFor(EventTarget.All) == 0, "an all-theater target is zero");

            TestAssert.That(!TheaterBalance.Unknown.Known, "unknown reads as unobserved");
            TestAssert.That(!Balanced.Contested, "a tie has no intervention pressure");
            TestAssert.That(Balanced.Deficit == 0, "a tie has no deficit");
        }

        private static void SuperEligibilityIsGated()
        {
            TestAssert.That(!EventDirector.SuperEligible(new DirectorState(10f, 0, -9999f, Contested)),
                "the opening minutes are calm on purpose");
            TestAssert.That(EventDirector.SuperEligible(
                    new DirectorState(EventDirector.SuperMinimumMissionTime, 0, -9999f, Contested)),
                "eligible once the situation exists");

            float justFired = EventDirector.SuperMinimumMissionTime;
            TestAssert.That(!EventDirector.SuperEligible(
                    new DirectorState(justFired + EventDirector.SuperCooldownSeconds - 1f, 1, justFired, Contested)),
                "supers are spaced");
            TestAssert.That(EventDirector.SuperEligible(
                    new DirectorState(justFired + EventDirector.SuperCooldownSeconds, 1, justFired, Contested)),
                "spaced supers come back");

            TestAssert.That(!EventDirector.SuperEligible(
                    new DirectorState(9999f, EventDirector.MaximumSupers, 0f, Contested)),
                "the mission ceiling holds");
        }

        private static void TargetedSupersNeedALosingSide()
        {
            for (int i = 0; i < EventCatalog.Count; i++)
            {
                EventDefinition entry = EventCatalog.At(i);
                if (entry.Tier != EventTier.Super) continue;

                if (entry.Target == EventTarget.All)
                {
                    TestAssert.That(EventDirector.Fits(entry, TheaterBalance.Unknown),
                        "a global superevent does not need a frontline");
                }
                else
                {
                    TestAssert.That(!EventDirector.Fits(entry, TheaterBalance.Unknown),
                        "a targeted superevent needs ground custody data");
                    TestAssert.That(!EventDirector.Fits(entry, Balanced),
                        "a targeted superevent needs an actual deficit");
                    TestAssert.That(!EventDirector.Fits(
                            entry, new TheaterBalance(2, 3, 2, 111, 222, 0)),
                        "one base is not a story");
                    TestAssert.That(EventDirector.Fits(entry, Contested),
                        "two bases of deficit arm the intervention");
                }
            }
        }

        private static void SupersAreCappedAndSpaced()
        {
            var recent = new List<int>(EventSelector.RecentWindow);
            var used = new HashSet<int>();
            int supersFired = 0;
            float lastSuperAt = -9999f;
            int lastSuperIndex = -1;

            for (float time = 0f; time < 7200f; time += 60f)
            {
                var state = new DirectorState(time, supersFired, lastSuperAt, Contested);
                int index = EventDirector.Select(
                    (uint)(time * 31f + 7f), EventCatalog.All, recent, used, true, state);
                TestAssert.That(index >= 0 && index < EventCatalog.Count, "index stays inside the catalog");

                EventDefinition entry = EventCatalog.At(index);
                bool superTime = entry.Tier == EventTier.Super;
                if (superTime)
                {
                    TestAssert.That(EventDirector.SuperEligible(state),
                        "a super never rolls outside its eligibility");
                    TestAssert.That(!used.Contains(index), "a super never repeats in one mission");
                    TestAssert.That(time - lastSuperAt >= EventDirector.SuperCooldownSeconds,
                        "supers keep their spacing");
                    supersFired++;
                    lastSuperAt = time;
                    used.Add(index);

                    if (index == lastSuperIndex)
                        TestAssert.That(false, "the same super never fires twice");
                    lastSuperIndex = index;
                }

                recent.Add(index);
                if (recent.Count > EventSelector.RecentWindow) recent.RemoveAt(0);
            }

            TestAssert.That(supersFired <= EventDirector.MaximumSupers,
                "the mission never exceeds its superevent ceiling");
            TestAssert.That(supersFired > 0, "a two-hour leaning mission gets its story");
        }

        private static void DisabledMeansNeverSuper()
        {
            var recent = new List<int>(EventSelector.RecentWindow);
            for (int i = 0; i < 512; i++)
            {
                var state = new DirectorState(3600f, 0, -9999f, Contested);
                int index = EventDirector.Select(
                    (uint)(i * 7919 + 3), EventCatalog.All, recent, new HashSet<int>(), false, state);
                TestAssert.That(EventCatalog.At(index).Tier != EventTier.Super,
                    "the super toggle really disables escalation");
                recent.Add(index);
                if (recent.Count > EventSelector.RecentWindow) recent.RemoveAt(0);
            }
        }

        private static void RecentEntriesAreAvoided()
        {
            var recent = new List<int>(EventSelector.RecentWindow);
            for (int i = 0; i < 512; i++)
            {
                var state = new DirectorState(3600f, 0, -9999f, Balanced);
                int index = EventDirector.Select(
                    (uint)(i * 104729 + 11), EventCatalog.All, recent, new HashSet<int>(), true, state);
                TestAssert.That(!recent.Contains(index), "the last three draws stay excluded");
                recent.Add(index);
                if (recent.Count > EventSelector.RecentWindow) recent.RemoveAt(0);
            }
        }

        private static void SelectionIsSeederDeterministic()
        {
            var state = new DirectorState(900f, 1, 300f, Contested);
            for (int i = 0; i < 64; i++)
            {
                int a = EventDirector.Select((uint)(i * 13 + 5), EventCatalog.All,
                    new List<int>(), new HashSet<int>(), true, state);
                int b = EventDirector.Select((uint)(i * 13 + 5), EventCatalog.All,
                    new List<int>(), new HashSet<int>(), true, state);
                TestAssert.That(a == b, "host and client seeds agree");
            }
        }
    }
}
