using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins KS-01 KESTREL: keys and role, orbit geometry, height rule, phase clock, spot filter, lase pick and HUD words.</summary>
    internal static class KestrelTests
    {
        private static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude <= 1e-3f;

        public static void Run()
        {
            TestAssert.That(VanguardKeys.Kestrel == "VG_Kestrel" && VanguardKeys.KestrelMount == "VG_Kestrel_Sled", "jsonKeys: VG_Kestrel on VG_Kestrel_Sled");
            TestAssert.That(VanguardKeys.RoleOf(VanguardKeys.Kestrel) == VanguardRole.Scout && !VanguardKeys.IsGunDrone(VanguardRole.Scout),
                "KESTREL is the scout, not a gun drone");
            TestAssert.That((int)VanguardRole.Scout == (int)VanguardRole.Gunship + 1 && VanguardRole.Scout != VanguardRole.Lander,
                "Scout preserves its appended value alongside the cargo lander");

            TestAssert.That(KestrelProfile.CruiseSpeed == 80f && KestrelProfile.OrbitRadius == 800f && KestrelProfile.OrbitHeight == 1000f &&
                KestrelProfile.MinAgl == 300f, "80 m/s, 800 m circle, 1000 m above the target, 300 m terrain floor");
            TestAssert.That(KestrelProfile.StationSeconds == 300f && KestrelProfile.HardCap == 900f && KestrelProfile.ClearSeconds == 2.5f,
                "300 s on station, 900 s hard cap, 2.5 s to clear the hold");
            TestAssert.That(KestrelProfile.SpotRadius == 5000f && KestrelProfile.SpotInterval == 2f && KestrelProfile.SpotCap == 48,
                "spot 5 km around the centre every 2 s, 48 units a pass");

            // Orbit guidance is GLAIVE's, scaled: pure tangent on the 800 m circle, centre on PORT, radial correction either side.
            Vector3 onCircle = KestrelProfile.OrbitOffset(Vector3.right * 800f, Vector3.forward);
            TestAssert.That(Near(onCircle, new Vector3(0f, 0f, GlaiveProfile.Lookahead)), "on the 800 m circle the waypoint is one lookahead along the tangent");
            TestAssert.That(Vector3.Cross(onCircle.normalized, -Vector3.right).y < 0f, "counter-clockwise orbit: the centre is on the left (port)");
            TestAssert.That(KestrelProfile.OrbitOffset(Vector3.right * 1600f, Vector3.forward).x < 0f &&
                KestrelProfile.OrbitOffset(Vector3.right * 200f, Vector3.forward).x > 0f, "radial correction pulls in from outside, out from inside");

            TestAssert.That(KestrelProfile.Capture(1500f, 400f, 6f, 80f) && !KestrelProfile.Capture(1700f, 400f, 6f, 80f) &&
                !KestrelProfile.Capture(1500f, 400f, 4f, 80f) && !KestrelProfile.Capture(1500f, 50f, 6f, 80f) &&
                !KestrelProfile.Capture(1500f, 400f, 6f, 170f), "capture: after 5 s, inside two radii (1600 m), above 70 m, under 160 m/s");

            TestAssert.That(KestrelProfile.Throttle(80f) == .4f && KestrelProfile.Throttle(110f) == 0f, "propeller throttle holds 80 m/s");
            TestAssert.Near(KestrelProfile.Throttle(60f), .9f, "throttle opens below the cruise speed");

            TestAssert.Near(KestrelProfile.AimHeight(0f, 0f, 1000f), 1000f, "on height: 1000 m above the target");
            TestAssert.Near(KestrelProfile.AimHeight(0f, 900f, 1200f), 1200f, "terrain floor: never under 300 m above the ground below");
            TestAssert.Near(KestrelProfile.AimHeight(0f, 0f, 3000f), 2950f, "from high it comes down 1 in 5 (50 m per 250 m waypoint)");
            TestAssert.Near(KestrelProfile.AimHeight(0f, 900f, 1000f), 1037.5f, "from low it climbs 3 in 20");

            TestAssert.That(KestrelProfile.Next(KestrelPhase.Transit, false, 0f, 10f) == KestrelPhase.Transit &&
                KestrelProfile.Next(KestrelPhase.Transit, true, 0f, 10f) == KestrelPhase.Station, "transit until the orbit is captured");
            TestAssert.That(KestrelProfile.Next(KestrelPhase.Station, false, 299f, 400f) == KestrelPhase.Station &&
                KestrelProfile.Next(KestrelPhase.Station, true, 300f, 400f) == KestrelPhase.Depart, "300 s on station, counted from orbit entry");
            TestAssert.That(KestrelProfile.Next(KestrelPhase.Transit, false, 0f, 892f) == KestrelPhase.Depart &&
                KestrelProfile.Next(KestrelPhase.Station, false, 10f, 891f) == KestrelPhase.Station &&
                KestrelProfile.Next(KestrelPhase.Station, false, 10f, 892f) == KestrelPhase.Depart,
                "hard cap: the 8 s departure starts at 892 s whatever the phase, so the glider is gone by 900 s");
            TestAssert.That(KestrelProfile.Next(KestrelPhase.Depart, true, 0f, 10f) == KestrelPhase.Depart, "departure is final");

            TestAssert.Near(KestrelProfile.StationLeft(KestrelPhase.Transit, 0f, 100f), 300f, "the station clock waits for the orbit");
            TestAssert.Near(KestrelProfile.StationLeft(KestrelPhase.Station, 120f, 400f), 180f, "station clock runs down");
            TestAssert.Near(KestrelProfile.StationLeft(KestrelPhase.Station, 10f, 800f), 92f, "a late arrival is cut short by the hard cap");
            TestAssert.That(KestrelProfile.StationLeft(KestrelPhase.Depart, 10f, 400f) == 0f && KestrelProfile.StationLeft(KestrelPhase.Transit, 0f, 895f) == 0f,
                "nothing left once departing or past the cap");
            float fullStationReach = (KestrelProfile.HardCap - KestrelProfile.DepartSeconds - KestrelProfile.StationSeconds) * KestrelProfile.CruiseSpeed;
            TestAssert.Near(fullStationReach, 47360f, "a target inside 47 km still gets the full 300 s");

            TestAssert.That(KestrelProfile.MaySpot(4000f, true, false) && KestrelProfile.MaySpot(5000f, false, true), "ground vehicles and ships inside 5 km are spotted");
            TestAssert.That(!KestrelProfile.MaySpot(5001f, true, false) && !KestrelProfile.MaySpot(100f, false, false),
                "nothing beyond 5 km; never aircraft, missiles or buildings");

            var ranges = new List<float> { 500f, 120f, 900f };
            TestAssert.Eq(KestrelProfile.LasePick(-1, ranges), 1, "no mark yet: the candidate nearest the orbit centre");
            TestAssert.Eq(KestrelProfile.LasePick(2, ranges), 2, "the marked unit keeps the mark while it is still a candidate");
            TestAssert.Eq(KestrelProfile.LasePick(5, ranges), 1, "an index past this pass's candidates is no mark");
            TestAssert.Eq(KestrelProfile.LasePick(-1, new List<float>()), -1, "nothing in sight: no mark");

            TestAssert.Eq(KestrelProfile.HudText(KestrelPhase.Transit, 300f), "KESTREL INBOUND", "HUD inbound");
            TestAssert.Eq(KestrelProfile.HudText(KestrelPhase.Station, 272f), "KESTREL ON STATION 4:32", "HUD station clock");
            TestAssert.Eq(KestrelProfile.HudText(KestrelPhase.Station, 4.2f), "KESTREL ON STATION 0:05", "HUD clock rounds up to whole seconds");
            TestAssert.That(ReferenceEquals(KestrelProfile.HudText(KestrelPhase.Station, 271.2f), KestrelProfile.HudText(KestrelPhase.Station, 272f)),
                "station text is one cached string per second (the HUD asks at 5 Hz)");
            TestAssert.Eq(KestrelProfile.HudText(KestrelPhase.Station, 9999f), "KESTREL ON STATION 5:00", "HUD clock is clamped to the station time");
            TestAssert.Eq(KestrelProfile.HudText(KestrelPhase.Depart, 0f), "KESTREL OFF STATION", "HUD departing");
            TestAssert.Eq(KestrelProfile.HudDetail(12, null), "SPOT 12", "HUD detail without a mark");
            TestAssert.Eq(KestrelProfile.HudDetail(3, "Hlt-L"), "SPOT 3  LASE HLT-L", "HUD detail names the lased target");
        }
    }
}
