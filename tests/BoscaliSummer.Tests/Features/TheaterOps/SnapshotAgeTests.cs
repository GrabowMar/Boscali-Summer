using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The age of the staff's data: the host's on the mission clock, a client's as send age plus unscaled receipt time (annex 01 E8).</summary>
    internal static class SnapshotAgeTests
    {
        public static void Run()
        {
            HostAgeIsMonotonicAndNeverNegative();
            AFrozenMissionClockFreezesTheHostAge();
            AnUnsetOrBrokenHostAgeIsUnknown();
            ClientAgeAddsUnscaledTimeSinceReceipt();
            ABrokenClientAgeIsUnknown();
        }

        private static void HostAgeIsMonotonicAndNeverNegative()
        {
            TestAssert.Near(SnapshotAge.Host(100f, 90f), 10f, "10 s after the sense");
            TestAssert.Near(SnapshotAge.Host(100f, 100f), 0f, "the same second is age 0");
            TestAssert.Near(SnapshotAge.Host(90f, 100f), 0f, "a sense stamped in the future is never negative");
            float last = -1f;
            for (float now = 10f; now <= 200f; now += 7f)
            {
                float age = SnapshotAge.Host(now, 10f);
                TestAssert.That(age >= last && age >= 0f, "age never goes down while the clock goes up (" + now + ")");
                last = age;
            }
        }

        private static void AFrozenMissionClockFreezesTheHostAge()
        {
            float first = SnapshotAge.Host(500f, 495f);
            float second = SnapshotAge.Host(500f, 495f);
            TestAssert.Near(first, 5f, "a paused host reads the age it had");
            TestAssert.Eq(second, first, "and keeps reading it, so a paused host never reads stale");
        }

        private static void AnUnsetOrBrokenHostAgeIsUnknown()
        {
            TestAssert.Eq(SnapshotAge.Host(100f, -1f), -1f, "a staff that has not sensed yet has no age");
            TestAssert.Eq(SnapshotAge.Host(float.NaN, 5f), -1f, "a NaN clock has no age");
            TestAssert.Eq(SnapshotAge.Host(100f, float.NaN), -1f, "a NaN sense time has no age");
            TestAssert.Eq(SnapshotAge.Host(float.PositiveInfinity, 5f), -1f, "an infinite clock has no age");
        }

        private static void ClientAgeAddsUnscaledTimeSinceReceipt()
        {
            TestAssert.Near(SnapshotAge.Client(3f, 110f, 100f), 13f, "3 s old at send plus 10 s on the wire and the shelf");
            TestAssert.Near(SnapshotAge.Client(0f, 100f, 100f), 0f, "a snapshot just received from a fresh host");
            TestAssert.Near(SnapshotAge.Client(3f, 90f, 100f), 3f, "an unscaled clock that went backwards adds nothing");
            TestAssert.Near(SnapshotAge.Client(-5f, 100f, 100f), 0f, "a negative send age never makes a negative age");
            TestAssert.Near(SnapshotAge.Client(14f, 101f, 100f), 15f, "14 s at send and 1 s since receipt reaches the 15 s line");
        }

        private static void ABrokenClientAgeIsUnknown()
        {
            TestAssert.Eq(SnapshotAge.Client(float.NaN, 100f, 100f), -1f, "a NaN send age is unknown");
            TestAssert.Eq(SnapshotAge.Client(3f, float.NaN, 100f), -1f, "a NaN clock is unknown");
            TestAssert.Eq(SnapshotAge.Client(3f, 100f, float.NaN), -1f, "a NaN receipt time is unknown");
            TestAssert.Eq(SnapshotAge.Client(3f, float.PositiveInfinity, 100f), -1f, "an infinite clock is unknown");
        }
    }
}
