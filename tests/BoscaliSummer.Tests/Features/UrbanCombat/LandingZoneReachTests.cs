using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class LandingZoneReachTests
    {
        public static void Run()
        {
            // HALO: below the pull height there is no glide, only the canopy run out and back.
            TestAssert.That(LandingZoneReach.Halo(300f) == HaloGlidePlan.CanopyRun * 2f,
                "low drop reaches only the canopy run");
            // At 2350 m over the zone: 2.5 x (2350 - 150) of glide plus the 150 m canopy run.
            TestAssert.That(LandingZoneReach.Halo(2350f) == 5650f,
                "halo reach is glide ratio over pull height plus canopy run");
            TestAssert.That(LandingZoneReach.Halo(400f) == 775f,
                "just into the glide: 2.5 x 250 of glide plus the canopy run");

            // Glider: fifteen metres per metre of height, capped.
            TestAssert.That(LandingZoneReach.Glider(1000f) == 15000f, "glider reach is 15:1");
            TestAssert.That(LandingZoneReach.Glider(10000f) == LandingZoneReach.GliderMax, "glider reach caps");
            TestAssert.That(LandingZoneReach.Glider(-50f) == 0f, "below the zone there is no reach");

            // The window edge itself counts: dropping at exactly max reach lands on the zone.
            TestAssert.That(LandingZoneReach.InWindow(5650f, 5650f), "exactly at reach is in the window");
            TestAssert.That(!LandingZoneReach.InWindow(5650.1f, 5650f), "past reach falls short");
        }
    }
}
