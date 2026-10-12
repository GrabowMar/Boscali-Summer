using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The one allied-share definition: friendly over friendly plus hostile, unknown when either side is unknown (annex 01 E11).</summary>
    internal static class TheaterReadoutTests
    {
        public static void Run()
        {
            TestAssert.Near(TheaterReadout.AlliedShare(3, 1), 0.75f, "three of four");
            TestAssert.Near(TheaterReadout.AlliedShare(1, 1), 0.5f, "an even split");
            TestAssert.Near(TheaterReadout.AlliedShare(0, 4), 0f, "none held is a real zero");
            TestAssert.Near(TheaterReadout.AlliedShare(4, 0), 1f, "all held is a real one");
            TestAssert.That(float.IsNaN(TheaterReadout.AlliedShare(0, 0)), "nothing on either side is unknown, not zero");
            TestAssert.That(float.IsNaN(TheaterReadout.AlliedShare(-1, 4)), "an unknown friendly count is unknown");
            TestAssert.That(float.IsNaN(TheaterReadout.AlliedShare(4, -1)), "an unknown hostile count is unknown");
            TestAssert.That(float.IsNaN(TheaterReadout.AlliedShare(-1, -1)), "both unknown is unknown");
            TestAssert.Near(TheaterReadout.AlliedShare(int.MaxValue, 1), 1f, "a huge count does not overflow into a wrong share");
        }
    }
}
