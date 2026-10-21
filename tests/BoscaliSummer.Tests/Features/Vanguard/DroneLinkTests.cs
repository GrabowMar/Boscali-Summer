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
            Vector3 hand = DroneLink.HandAim(drone, 90f, 2000f, 1500f);
            TestAssert.That(Vector3.Distance(hand, drone + Vector3.right * DroneLink.HandLookaheadM) < 0.01f, "HAND: level on the commanded course (090 = east)");
            TestAssert.That(DroneLink.HandAim(drone, 0f, 9000f, 1500f).y - drone.y == DroneLink.HandClimbStepM, "HAND: a big climb is a gentle one");
            Vector3 low = DroneLink.HandAim(drone, 0f, 0f, 20f);
            TestAssert.That(low.y > drone.y, "HAND near the ground: a dive is pulled up");
            TestAssert.That(DroneLink.HandPoint(-30f, 50f) == new Vector3(330f, DroneLink.MinAltM, 0f), "HAND point: course wrapped, height floored");

            // Built by hand: Quaternion.LookRotation/Euler are engine calls the test host cannot make.
            float s45 = Mathf.Sqrt(0.5f), half5 = 5f * Mathf.Deg2Rad;
            Quaternion east = new Quaternion(0f, s45, 0f, s45);                              // yaw 90
            Quaternion up10 = new Quaternion(Mathf.Sin(-half5), 0f, 0f, Mathf.Cos(-half5));  // pitch 10 up
            Vector3 centre0 = DroneLink.LookDirection(east, 50f, 16f / 9f, new Vector2(0.5f, 0.5f));
            TestAssert.That(Vector3.Distance(centre0, Vector3.right) < 1e-4f, "feed centre looks down the lens");
            Vector3 rightEdge = DroneLink.LookDirection(Quaternion.identity, 50f, 1f, new Vector2(1f, 0.5f));
            TestAssert.That(Mathf.Abs(DroneLink.Bearing(Vector3.zero, rightEdge) - 25f) < 0.01f, "square feed: the right edge is half the field of view right");
            Vector3 onCross = DroneLink.SteerTo(centre0, Vector3.right, 2000f);
            TestAssert.That(Mathf.Abs(onCross.x - 90f) < 0.01f && Mathf.Abs(onCross.y - 2000f) < 0.01f, "a click on the cross: turn there, hold the height");
            Vector3 above = DroneLink.SteerTo(up10 * Vector3.forward, Vector3.forward, 2000f);
            TestAssert.That(Mathf.Abs(above.y - (2000f + 10f * DroneLink.AltPerDeg)) < 0.5f && above.x < 0.01f, "a click 10 deg above the cross climbs 600 m");

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

            TestAssert.That(DroneLink.HorizonLift(0f, 50f) == 0f && Mathf.Abs(DroneLink.HorizonLift(-25f, 50f) - 1f) < 1e-4f &&
                DroneLink.HorizonLift(-40f, 50f) > 1f && DroneLink.HorizonLift(10f, 50f) < 0f,
                "horizon: centred on a level lens, at the top edge with the lens down half the view, off screen past it, below on a lens up");
            TestAssert.That(DroneLink.Grid(4127f, 77319f) == "0412 7731" && DroneLink.Grid(-15f, 100005f) == "9998 0000",
                "grid: ten-metre digits in the 100 km square, wrapped below zero");
            TestAssert.That(DroneLink.LatencyMs(1f) == 40 && DroneLink.LatencyMs(0f) == 600, "latency: 40 ms on a full link, 600 at its edge");
        }
    }
}
