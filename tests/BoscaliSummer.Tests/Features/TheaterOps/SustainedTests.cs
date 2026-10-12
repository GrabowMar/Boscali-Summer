using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>How long a condition has been continuously true, advanced by the 10 s review (annex 01 E5).</summary>
    internal static class SustainedTests
    {
        public static void Run()
        {
            StartsOnTheFirstTrue();
            ResetsOnFalse();
            ADefaultValueIsNotRunning();
            BadClocksAreHarmless();
        }

        private static void StartsOnTheFirstTrue()
        {
            var s = new Sustained();
            TestAssert.Near(s.Update(true, 100f), 0f, "the first true review has held for nothing yet");
            TestAssert.Near(s.Update(true, 110f), 10f, "the next review has held 10 s");
            TestAssert.Near(s.Update(true, 120f), 20f, "and the third 20 s: three consecutive 10 s reviews");
        }

        private static void ResetsOnFalse()
        {
            var s = new Sustained();
            s.Update(true, 100f);
            s.Update(true, 110f);
            TestAssert.Near(s.Update(false, 120f), 0f, "a false review reads 0");
            TestAssert.Near(s.Update(true, 130f), 0f, "and the count starts again from the next true");
            TestAssert.Near(s.Update(true, 140f), 10f, "from that moment");
            s.Reset();
            TestAssert.Near(s.Update(true, 150f), 0f, "Reset also starts again");
        }

        private static void ADefaultValueIsNotRunning()
        {
            var s = new Sustained();
            TestAssert.Near(s.Update(true, 500f), 0f, "a default value does not claim to have been true since second 0");
            Sustained[] timers = new Sustained[2];
            TestAssert.Near(timers[1].Update(true, 900f), 0f, "nor does an array slot");
        }

        private static void BadClocksAreHarmless()
        {
            var s = new Sustained();
            s.Update(true, 100f);
            TestAssert.Near(s.Update(true, float.NaN), 0f, "a NaN clock reads 0 and changes nothing");
            TestAssert.Near(s.Update(true, 130f), 30f, "the run it interrupted was not lost");
            TestAssert.Near(s.Update(true, 90f), 0f, "a clock that went backwards never reads negative");
        }
    }
}
