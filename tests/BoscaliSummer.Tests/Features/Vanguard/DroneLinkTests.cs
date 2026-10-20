using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins the VANGUARD drone link rules: link range, cycling, HAND and WPT aim, plot scale.</summary>
    internal static class DroneLinkTests
    {
        public static void Run()
        {
            TestAssert.That(DroneLink.Quality(5000f) == 1f && DroneLink.Quality(DroneLink.MaxRangeM) == 0f &&
                Mathf.Abs(DroneLink.Quality(26000f) - 0.5f) < 1e-4f, "full inside 12 km, half at 26 km, gone at 40 km");
            TestAssert.That(DroneLink.InRange(39999f) && !DroneLink.InRange(DroneLink.MaxRangeM), "the link breaks at the limit");
            TestAssert.That(DroneLink.Bars(0f) == 0 && DroneLink.Bars(0.01f) == 1 && DroneLink.Bars(1f) == 5, "any live link shows a bar");

            TestAssert.That(DroneLink.Step(3, 2, 1) == 0 && DroneLink.Step(3, 0, -1) == 2 && DroneLink.Step(3, 1, 1) == 2, "NEXT/PREV wrap");
            TestAssert.That(DroneLink.Step(3, -1, 1) == 0 && DroneLink.Step(3, -1, -1) == 2 && DroneLink.Step(0, 0, 1) == -1, "nothing linked: NEXT takes the first, PREV the last");

            Vector3 drone = new Vector3(1000f, 2000f, 0f);
            Vector3 hand = DroneLink.HandAim(drone, new Vector3(0f, 0f, 2f), 1500f);
            TestAssert.That(Vector3.Distance(hand, drone + Vector3.forward * DroneLink.HandLookaheadM) < 0.01f, "HAND: the drone flies the pilot's nose direction from where it is");
            Vector3 low = DroneLink.HandAim(drone, new Vector3(0f, -1f, 1f), 20f);
            TestAssert.That(low.y > drone.y, "HAND near the ground: a dive is pulled up");

            Vector3 centre = new Vector3(0f, 1500f, 0f);
            TestAssert.That(DroneLink.OrbitAim(new Vector3(10000f, 900f, 0f), centre, 1200f) == centre, "WPT far out: straight at the point");
            Vector3 lead = DroneLink.OrbitAim(new Vector3(1200f, 1500f, 0f), centre, 1200f);
            TestAssert.That(Mathf.Abs(new Vector2(lead.x, lead.z).magnitude - 1200f) < 0.5f && lead.z > 0f && lead.y == 1500f,
                "WPT close in: a point ahead on the circle, turning left, at the point's height");

            TestAssert.That(Mathf.Abs(DroneLink.Bearing(Vector3.zero, Vector3.right) - 90f) < 1e-3f &&
                Mathf.Abs(DroneLink.Bearing(Vector3.zero, Vector3.back) - 180f) < 1e-3f, "bearing: north is +z, east is +x");
            TestAssert.That(DroneLink.PlotRangeKm(3f) == 5f && DroneLink.PlotRangeKm(9f) == 20f && DroneLink.PlotRangeKm(90f) == 40f,
                "the plot holds the farthest drone with a margin, capped at the link limit");
            TestAssert.That(DroneLink.Callsign(VanguardRole.Gunship, 2) == "GLAIVE-2S 2" && DroneLink.Job(VanguardRole.Drone) == "ARCHER" &&
                DroneLink.Clock(125f) == "02:05", "callsigns, jobs and the clock");
        }
    }
}
