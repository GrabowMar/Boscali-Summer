using System;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class LandingZoneMarkerTests
    {
        public static void Run()
        {
            TestAssert.That(LandingZoneMarkerMath.Place(30f, -20f, true, 2f, 1f, 800f, 400f,
                out float x, out float y, out _, out bool pinned) && x == 30f && y == -20f && !pinned,
                "visible landing zone stays glued to its projected ground position");
            TestAssert.That(LandingZoneMarkerMath.Place(1600f, 400f, true, 1f, 0f, 800f, 400f,
                out x, out y, out float bearing, out pinned) && x == 800f && y == 200f && pinned && bearing > 0f,
                "offscreen front fix intersects the HUD edge along its projected bearing");
            TestAssert.That(LandingZoneMarkerMath.Place(0f, 0f, false, -2f, 1f, 800f, 400f,
                out x, out y, out bearing, out pinned) && x == -800f && y == 400f && pinned && bearing > 90f,
                "behind-left zone stays left rather than mirroring through the camera");
            TestAssert.That(LandingZoneMarkerMath.Place(0f, 0f, false, 0f, 0f, 800f, 400f,
                out x, out y, out _, out pinned) && x == 0f && y == -400f && pinned,
                "exactly astern gets a stable edge indication, never a false central fix");
            TestAssert.That(!LandingZoneMarkerMath.Place(0f, 0f, true, 0f, 0f, 0f, 400f,
                out _, out _, out _, out _), "invalid canvas bounds cannot project a marker");
            TestAssert.That(!LandingZoneMarkerMath.Place(float.NaN, 0f, false, float.PositiveInfinity, 1f, 800f, 400f,
                out _, out _, out _, out _), "non-finite directions are suppressed");
            foreach (float dx in new[] { -10000f, -100f, 0f, 100f, 10000f })
                foreach (float dy in new[] { -10000f, -100f, 0f, 100f, 10000f })
                {
                    TestAssert.That(LandingZoneMarkerMath.Place(0f, 0f, false, dx, dy, 800f, 400f,
                        out x, out y, out _, out pinned) && pinned && Math.Abs(x) <= 800.001f && Math.Abs(y) <= 400.001f &&
                        (Math.Abs(Math.Abs(x) - 800f) < .001f || Math.Abs(Math.Abs(y) - 400f) < .001f),
                        "all offscreen bearings stay bounded on the HUD edge");
                }
        }
    }
}
