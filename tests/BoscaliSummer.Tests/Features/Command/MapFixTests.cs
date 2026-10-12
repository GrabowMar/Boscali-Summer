using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The map context card's pure reads: compass bearing, time to a point, and who holds the ground.</summary>
    internal static class MapFixTests
    {
        public static void Run()
        {
            TestAssert.That(MapFix.Bearing(0f, 0f, 0f, 1000f) == 0, "+Z is north");
            TestAssert.That(MapFix.Bearing(0f, 0f, 1000f, 0f) == 90, "+X is east");
            TestAssert.That(MapFix.Bearing(0f, 0f, 0f, -1000f) == 180, "-Z is south");
            TestAssert.That(MapFix.Bearing(0f, 0f, -1000f, 0f) == 270, "-X is west, never -90");
            TestAssert.That(MapFix.Bearing(0f, 0f, -1f, 1000f) == 0, "just west of north rounds to 000, never 360");
            TestAssert.That(MapFix.Range(1000f, 0f, 4000f, 4000f) == 5000f, "range is the flat distance");

            TestAssert.That(MapFix.EtaSeconds(30000f, 250f) == 120, "30 km at 250 m/s is two minutes");
            TestAssert.That(MapFix.EtaSeconds(30000f, 0f) == -1, "a parked aircraft has no arrival time");
            TestAssert.That(MapFix.EtaSeconds(30000f, float.NaN) == -1, "an unknown speed has no arrival time");
            TestAssert.That(MapFix.EtaSeconds(400000f, 40f) == -1, "past 99 minutes the clock cannot print it");

            TestAssert.That(MapFix.Classify(false, 1f) == MapFix.Hold.Unknown, "an unread field is unknown, whatever the value");
            TestAssert.That(MapFix.Classify(true, float.NaN) == MapFix.Hold.Unknown, "NaN is unknown");
            TestAssert.That(MapFix.Classify(true, .8f) == MapFix.Hold.Friendly, "strong positive hold is ours");
            TestAssert.That(MapFix.Classify(true, -.8f) == MapFix.Hold.Hostile, "strong negative hold is theirs");
            TestAssert.That(MapFix.Classify(true, .2f) == MapFix.Hold.Contested, "a weak claim sits in the contested band");
            TestAssert.That(MapFix.Classify(true, -.2f) == MapFix.Hold.Contested, "on either side of the front");
            TestAssert.That(MapFix.Classify(true, 0f) == MapFix.Hold.Unclaimed, "no claim at all is unclaimed, not contested");
        }
    }
}
