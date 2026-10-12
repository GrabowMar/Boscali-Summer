using BoscaliSummer.Modules.HighCommand.Domain;

namespace BoscaliSummer.Tests.Features.HighCommand
{
    /// <summary>Annex 04 H3: what each faction has confirmed about enemy command posts, with the global position of the sighting.</summary>
    internal static class PostIntelTests
    {
        public static void Run()
        {
            FirstConfirmIsFirstAndRemembersTheGlobalPosition();
            AgeTracksNowAndClampsAtTheWireBound();
            ForgetDropsEveryObserver();
            ObserversAreIndependent();
            OutOfRangeIsIgnoredNeverThrown();
            ASightingAtTimeZeroStillCountsAsSeen();
            ARepeatSightingRefreshesTheRecord();
            ClearForgetsEverything();
        }

        private static void FirstConfirmIsFirstAndRemembersTheGlobalPosition()
        {
            var intel = new PostIntel();
            TestAssert.That(!intel.TryGet(0, 5, 100f, out _) && intel.LastSight(0, 5) == 0f, "nothing is known before the first sighting");
            TestAssert.That(intel.Confirm(0, 5, 100f, 1200f, 35f, -3400f), "the first confirmation reports first");
            TestAssert.That(!intel.Confirm(0, 5, 110f, 1200f, 35f, -3400f), "the second does not");
            TestAssert.That(intel.TryGet(0, 5, 110f, out PostSighting seen), "a confirmed post can be read");
            TestAssert.That(seen.X == 1200f && seen.Y == 35f && seen.Z == -3400f, "the stored position is the sighting's global position");
            TestAssert.That(intel.LastSight(0, 5) == 110f, "LastSight is the clock of the latest sighting");
        }

        private static void AgeTracksNowAndClampsAtTheWireBound()
        {
            var intel = new PostIntel();
            intel.Confirm(1, 9, 100f, 0f, 0f, 0f);
            intel.TryGet(1, 9, 100f, out PostSighting same);
            intel.TryGet(1, 9, 130f, out PostSighting later);
            intel.TryGet(1, 9, 100f + PostIntel.MaximumAge, out PostSighting edge);
            intel.TryGet(1, 9, 100000f, out PostSighting old);
            intel.TryGet(1, 9, 50f, out PostSighting before);
            intel.TryGet(1, 9, float.NaN, out PostSighting unknown);
            TestAssert.That(same.Age == 0f, "a sighting this instant is age 0");
            TestAssert.That(later.Age == 30f, "the age follows now");
            TestAssert.That(edge.Age == PostIntel.MaximumAge && old.Age == PostIntel.MaximumAge, "the age clamps at the 600 s wire bound");
            TestAssert.That(before.Age == 0f, "a clock that went backwards never reads a negative age");
            TestAssert.That(unknown.Age == PostIntel.MaximumAge, "an unreadable clock reads as the oldest, never as fresh");
        }

        private static void ForgetDropsEveryObserver()
        {
            var intel = new PostIntel();
            intel.Confirm(0, 12, 10f, 1f, 2f, 3f);
            intel.Confirm(3, 12, 11f, 4f, 5f, 6f);
            intel.Confirm(3, 13, 11f, 7f, 8f, 9f);
            intel.Forget(12);
            TestAssert.That(!intel.TryGet(0, 12, 20f, out _) && !intel.TryGet(3, 12, 20f, out _), "the post fell: nobody knows it any more");
            TestAssert.That(intel.LastSight(0, 12) == 0f && intel.LastSight(3, 12) == 0f, "LastSight reads never");
            TestAssert.That(intel.TryGet(3, 13, 20f, out _), "another key is untouched");
            TestAssert.That(intel.Confirm(0, 12, 30f, 1f, 2f, 3f), "the next confirmation after a Forget is first again");
        }

        private static void ObserversAreIndependent()
        {
            var intel = new PostIntel();
            TestAssert.That(intel.Confirm(0, 7, 10f, 1f, 1f, 1f), "observer 0 sees the post");
            TestAssert.That(intel.Confirm(1, 7, 12f, 2f, 2f, 2f), "observer 1 is first for itself");
            TestAssert.That(!intel.TryGet(2, 7, 20f, out _), "observer 2 knows nothing");
            intel.TryGet(0, 7, 20f, out PostSighting a);
            intel.TryGet(1, 7, 20f, out PostSighting b);
            TestAssert.That(a.X == 1f && b.X == 2f && a.Age == 10f && b.Age == 8f, "each observer keeps its own position and age");
        }

        private static void OutOfRangeIsIgnoredNeverThrown()
        {
            var intel = new PostIntel();
            int[] observers = { -1, PostIntel.Observers, 99 };
            int[] keys = { -1, PostIntel.Keys, 99 };
            foreach (int observer in observers)
            {
                TestAssert.That(!intel.Confirm(observer, 0, 5f, 0f, 0f, 0f), "an observer outside 0..7 is ignored");
                TestAssert.That(!intel.TryGet(observer, 0, 5f, out _) && intel.LastSight(observer, 0) == 0f, "and reads as unknown");
            }
            foreach (int key in keys)
            {
                TestAssert.That(!intel.Confirm(0, key, 5f, 0f, 0f, 0f), "a key outside 0..63 is ignored");
                TestAssert.That(!intel.TryGet(0, key, 5f, out _) && intel.LastSight(0, key) == 0f, "and reads as unknown");
                intel.Forget(key);
            }
            TestAssert.That(intel.Confirm(PostIntel.Observers - 1, PostIntel.Keys - 1, 5f, 0f, 0f, 0f), "the last observer and key are valid");
            TestAssert.That(!intel.Confirm(0, 1, 5f, float.NaN, 0f, 0f) && !intel.Confirm(0, 1, 5f, 0f, float.PositiveInfinity, 0f) &&
                            !intel.Confirm(0, 1, 5f, 0f, 0f, float.NegativeInfinity), "a post with no finite position is not confirmed");
            TestAssert.That(!intel.TryGet(0, 1, 5f, out _), "and leaves no record");
        }

        private static void ASightingAtTimeZeroStillCountsAsSeen()
        {
            var intel = new PostIntel();
            TestAssert.That(intel.Confirm(0, 2, 0f, 9f, 9f, 9f), "a sighting at mission time 0 is first");
            TestAssert.That(intel.LastSight(0, 2) > 0f && intel.TryGet(0, 2, 0f, out _), "and is stored with a positive stamp (0 means never)");
            TestAssert.That(!intel.Confirm(0, 2, 1f, 9f, 9f, 9f), "so the next sighting is not first");
            intel.Confirm(0, 3, -4f, 9f, 9f, 9f);
            intel.Confirm(0, 4, float.NaN, 9f, 9f, 9f);
            TestAssert.That(intel.LastSight(0, 3) > 0f && intel.LastSight(0, 4) > 0f, "a negative or unreadable clock still stores a positive stamp");
            intel.TryGet(0, 4, 50f, out PostSighting oldest);
            TestAssert.Near(oldest.Age, 50f, "and ages from the beginning of the mission");
        }

        private static void ARepeatSightingRefreshesTheRecord()
        {
            var intel = new PostIntel();
            intel.Confirm(0, 6, 10f, 1f, 1f, 1f);
            intel.Confirm(0, 6, 40f, 2f, 3f, 4f);
            intel.TryGet(0, 6, 50f, out PostSighting seen);
            TestAssert.That(seen.Age == 10f && seen.X == 2f && seen.Y == 3f && seen.Z == 4f, "a newer sighting replaces the age and the position");
        }

        private static void ClearForgetsEverything()
        {
            var intel = new PostIntel();
            intel.Confirm(0, 1, 5f, 0f, 0f, 0f);
            intel.Confirm(7, 63, 5f, 0f, 0f, 0f);
            intel.Clear();
            TestAssert.That(!intel.TryGet(0, 1, 6f, out _) && !intel.TryGet(7, 63, 6f, out _), "Clear forgets every record");
            TestAssert.That(intel.Confirm(0, 1, 7f, 0f, 0f, 0f), "and the next confirmation is first");
        }
    }
}
