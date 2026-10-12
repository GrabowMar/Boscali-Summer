using BoscaliSummer.Core.Contracts;
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
            KnownSitesCountsDistinctSites();
        }

        private static AirDefenceRing Ring(int site) =>
            new AirDefenceRing(0f, 0f, 1000f, 0f, 1000f, AirDefenceKind.RadarSam, false, false, RingSource.Tracked, true, 0f, false, 1, site, 0);

        private static void KnownSitesCountsDistinctSites()
        {
            AirDefenceRing[] rings = { Ring(1), Ring(1), Ring(2), Ring(3) };
            TestAssert.Eq(TheaterReadout.KnownSites(true, rings, rings.Length), 3, "two cones at one site are one site");
            TestAssert.Eq(TheaterReadout.KnownSites(true, rings, 2), 1, "only the first count rings are read");
            TestAssert.Eq(TheaterReadout.KnownSites(true, rings, 99), 3, "a count past the buffer is clamped to it");
            TestAssert.Eq(TheaterReadout.KnownSites(true, new AirDefenceRing[4], 0), 0, "a ready picture with nothing in it is a real zero");
            TestAssert.Eq(TheaterReadout.KnownSites(false, rings, rings.Length), -1, "a picture that is not ready is unknown, not zero");
            TestAssert.Eq(TheaterReadout.KnownSites(true, null, 3), -1, "no buffer is unknown");
        }
    }
}
