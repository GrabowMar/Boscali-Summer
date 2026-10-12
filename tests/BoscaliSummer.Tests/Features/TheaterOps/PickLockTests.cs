using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>A pilot's pick locks other pilots out for a while; the picker and a staff pick never do (annex 01 E2).</summary>
    internal static class PickLockTests
    {
        private const ulong Picker = 42UL, Other = 7UL;

        public static void Run()
        {
            ThePickerPassesInsideTheLock();
            AnotherActorIsLockedWithTheSecondsLeft();
            TheLockEndsAtItsBoundary();
            AStaffPickNeverLocks();
            ActorZeroAgainstAPilotPickIsLocked();
            NoLockAndBadClocksAllow();
        }

        private static void ThePickerPassesInsideTheLock()
        {
            TestAssert.That(PickLock.Check(false, 100f, 220f, Picker, Picker, out float left) == null, "the picker may redirect or call off at any time");
            TestAssert.Near(left, 0f, "an allowed act reports no seconds");
        }

        private static void AnotherActorIsLockedWithTheSecondsLeft()
        {
            TheaterCommandCode? code = PickLock.Check(false, 100f, 220f, Picker, Other, out float left);
            TestAssert.That(code == TheaterCommandCode.Locked, "a different pilot inside the lock is answered Locked");
            TestAssert.Near(left, 120f, "with the seconds left");
            PickLock.Check(false, 200.5f, 220f, Picker, Other, out left);
            TestAssert.Near(left, 19.5f, "the seconds shrink with the clock");
        }

        private static void TheLockEndsAtItsBoundary()
        {
            TestAssert.That(PickLock.Check(false, 219.99f, 220f, Picker, Other, out _) == TheaterCommandCode.Locked, "still locked just before the end");
            TestAssert.That(PickLock.Check(false, 220f, 220f, Picker, Other, out float left) == null, "free exactly at the end");
            TestAssert.Near(left, 0f, "no seconds once free");
            TestAssert.That(PickLock.Check(false, 300f, 220f, Picker, Other, out _) == null, "free long after");
        }

        private static void AStaffPickNeverLocks()
        {
            TestAssert.That(PickLock.Check(true, 10f, 0f, 0UL, Other, out _) == null, "a staff pick (lock 0) never locks");
            TestAssert.That(PickLock.Check(true, 10f, 500f, Picker, Other, out _) == null, "even a stale lock time cannot lock under a staff pick");
        }

        private static void ActorZeroAgainstAPilotPickIsLocked()
        {
            TestAssert.That(PickLock.Check(false, 100f, 220f, Picker, 0UL, out _) == TheaterCommandCode.Locked, "an unidentified actor cannot override a pilot's pick");
        }

        private static void NoLockAndBadClocksAllow()
        {
            TestAssert.That(PickLock.Check(false, 100f, 0f, Picker, Other, out _) == null, "no lock time means no lock");
            TestAssert.That(PickLock.Check(false, 100f, float.NaN, Picker, Other, out _) == null, "a NaN lock time cannot lock");
            TestAssert.That(PickLock.Check(false, float.NaN, 220f, Picker, Other, out _) == null, "a NaN clock cannot lock");
        }
    }
}
