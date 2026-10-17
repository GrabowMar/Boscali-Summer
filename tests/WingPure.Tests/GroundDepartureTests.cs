using System;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

// Only unused airborne/task seams are declared here, keeping these regressions independent of the flight stack.
// GroundPilot, its contracts, graph, reservations, sequencer and controller are linked production sources.
namespace BoscaliSummer.Modules.Wing.Domain
{
    internal enum TaskKind : byte { Form, Move, Route, Patrol, Orbit, Hold }
    internal interface IFlightPipeline
    {
        GuidanceCommand Guide(in FlightIntent intent, in AircraftState s, AirframeProfile p);
        ControlOutput Step(in GuidanceCommand guidance, in AircraftState s, in LimitContext ctx, AirframeProfile p, float dt);
        void Track(in AircraftState s, in ControlOutput applied, AirframeProfile p);
    }
}

namespace WingPure.Tests
{
    internal static class GroundGuidanceProgressTests
    {
        public static void Run()
        {
            // Each parallel leg is closer. Updating progress must not move the search window itself.
            var path = new Vec3[16];
            for (int i = 0; i < path.Length; i++)
                path[i] = new Vec3(12f - (i / 2), 0f, (i % 4 == 1 || i % 4 == 2) ? 100f : 0f);
            int progress = 0;
            GroundGuidance.Pursue(path, ref progress, GroundFixture.At(new Vec3(0f, 0f, 50f)), 1000f);
            TestAssert.That(progress <= GroundGuidance.ProgressWindow, "nearest-segment search stays within its original progress window");
            TestAssert.That(progress > 0, "a nearby later hairpin leg can still advance progress");
        }
    }

    internal static class GroundGuidanceStationaryProgressTests
    {
        public static void Run()
        {
            var path = new Vec3[16];
            for (int i = 0; i < path.Length; i++)
                path[i] = new Vec3(100f - 10f * (i / 2), 0f, (i % 4 == 1 || i % 4 == 2) ? 100f : 0f);
            int progress = 0;
            AircraftState s = GroundFixture.At(new Vec3(0f, 0f, 50f));
            for (int i = 0; i < 12; i++) GroundGuidance.Pursue(path, ref progress, s, 1000f);
            TestAssert.Eq(progress, 0, "stationary aircraft far from its route cannot creep through distant parallel legs");

            Vec3[] hairpin = { Vec3.Zero, new Vec3(0f, 0f, 100f), new Vec3(10f, 0f, 100f), new Vec3(10f, 0f, 0f) };
            progress = 0;
            s = GroundFixture.At(new Vec3(10f, 0f, 80f), -Vec3.Forward);
            GroundGuidance.Pursue(hairpin, ref progress, s, 100f);
            TestAssert.Eq(progress, 2, "nearby actual return leg remains recoverable after a hairpin");
        }
    }

    internal static class GroundGuidanceEndpointTests
    {
        public static void Run()
        {
            Vec3 end = new Vec3(0f, 0f, 100f);
            Vec3[] path = { Vec3.Zero, end };
            int progress = 0;
            AircraftState lateral = GroundFixture.At(end + Vec3.Right * 20f, -Vec3.Right);
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, lateral, 20f);
            TestAssert.That(!c.Stop && c.Speed > 0f, "off-axis endpoint remains reachable");
            TestAssert.Near(c.Curvature, 0f, "facing an endpoint laterally must steer to that endpoint");
            progress = 0;
            AircraftState overshoot = GroundFixture.At(end + Vec3.Forward * 8f, -Vec3.Forward);
            c = GroundGuidance.Pursue(path, ref progress, overshoot, 8f);
            TestAssert.That(!c.Stop && c.Speed > 0f, "overshot endpoint remains reachable");
            TestAssert.Near(c.Curvature, 0f, "facing an overshot endpoint must not turn toward an extrapolated path");
        }
    }

    internal static class GroundGuidanceRollExtensionTests
    {
        public static void Run()
        {
            Vec3[] path = { Vec3.Zero, new Vec3(0f, 0f, 100f) };
            int progress = 0;
            AircraftState overshoot = GroundFixture.At(new Vec3(0f, 0f, 120f));
            GroundCommand c = GroundGuidance.Pursue(path, ref progress, overshoot, 1e6f, extendEnd: true);
            TestAssert.That(!c.Stop && c.Speed > 0f, "roll continues beyond its guidance path endpoint");
            TestAssert.Near(c.Curvature, 0f, "roll still aims straight ahead after passing its path endpoint");
        }
    }

    internal static class GroundTaxiGoalTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            Vec3 hold = f.Hold;
            AircraftState off = GroundFixture.At(hold + Vec3.Right * 60f);
            f.Tick(off, 2.1f);
            ControlOutput o = f.Tick(off, 2.2f);
            TestAssert.That(f.Pilot.Phase == GroundPhase.HoldShort || f.Pilot.Phase == GroundPhase.TaxiOut,
                "longitudinal projection beside hold-short does not begin lineup");
            TestAssert.That(f.Pilot.Stop != GroundStop.Goal, "off-axis hold-short does not suppress stuck recovery as a goal");
            TestAssert.That(o.Throttle > 0f && o.Brake < 1f, "off-axis hold-short keeps pursuing the actual endpoint");
            f.Tick(GroundFixture.At(hold), 2.3f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "the actual hold-short still permits lineup");
        }
    }

    internal static class GroundTaxiStandTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            TestAssert.That(f.Pilot.TaxiIn(GroundFixture.At(f.Hold), 3f, null, 0), "fixture has a taxi-in stand");
            AircraftState off = GroundFixture.At(GroundFixture.Spawn + Vec3.Right * 60f, -Vec3.Forward);
            f.Tick(off, 3.1f);
            ControlOutput o = f.Tick(off, 3.2f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.TaxiIn, "projection beside a stand does not park the aircraft");
            TestAssert.That(o.Throttle > 0f && o.Brake < 1f, "off-axis stand keeps pursuing the actual endpoint");
            f.Tick(GroundFixture.At(GroundFixture.Spawn, -Vec3.Forward), 3.3f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.Stand, "the actual stand still parks the aircraft");
        }
    }

    internal static class GroundTaxiOvershootTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            AircraftState off = GroundFixture.At(f.Hold + Vec3.Forward * 25f, -Vec3.Forward);
            f.Tick(off, 2.1f);
            ControlOutput o = f.Tick(off, 2.2f);
            TestAssert.That(f.Pilot.Phase == GroundPhase.HoldShort || f.Pilot.Phase == GroundPhase.TaxiOut,
                "overshooting hold-short does not begin lineup from a distant point");
            TestAssert.That(f.Pilot.Stop != GroundStop.Goal && o.Throttle > 0f,
                "overshooting hold-short pursues the actual endpoint instead of stopping forever");
            f.Tick(GroundFixture.At(f.Hold), 2.3f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "recovering to hold-short still permits lineup");
        }
    }

    internal static class GroundLineupGoalTests
    {
        public static void Run() => GroundFixture.CheckLineupRecovery(Vec3.Right * 60f);
    }

    internal static class GroundLineupOvershootTests
    {
        public static void Run() => GroundFixture.CheckLineupRecovery(Vec3.Forward * 25f);
    }

    internal static class GroundTaxiReservationTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            int hold = f.Traffic.Graph.HoldShort(0, false);
            f.Traffic.Reservations.TryAdvance(2, TaxiPriority.Departing, new[] { hold }, Array.Empty<int>(), 0, 0);
            AircraftState off = GroundFixture.At(f.Hold + Vec3.Right * 60f, -Vec3.Right);
            f.Tick(off, 2.1f);
            ControlOutput o = f.Tick(off, 2.2f);
            TestAssert.Eq(f.Pilot.Stop, GroundStop.Claims, "endpoint correction preserves another aircraft's goal reservation");
            TestAssert.That(o.Brake == 1f && o.Throttle == 0f, "endpoint correction cannot drive through a reservation stop");
        }
    }

    internal static class GroundTaxiObstacleTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            f.Traffic.Obstacles.Add(GroundFixture.Spawn + Vec3.Forward * 50f);
            AircraftState s = GroundFixture.At(GroundFixture.Spawn + Vec3.Forward * 30f);
            f.Tick(s, 2.1f);
            ControlOutput o = f.Tick(s, 2.2f);
            TestAssert.Eq(f.Pilot.Stop, GroundStop.Foreign, "ordinary taxi still yields to a foreign aircraft");
            TestAssert.That(o.Brake == 1f && o.Throttle == 0f, "endpoint correction preserves a foreign aircraft stop");
        }
    }

    internal static class GroundTaxiEndpointObstacleTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            AircraftState s = GroundFixture.At(f.Hold + Vec3.Right * 60f, -Vec3.Right);
            f.Traffic.Vehicles.Add(f.Hold + Vec3.Right * 20f);
            f.Tick(s, 2.1f);
            f.Tick(s, 2.2f);
            TestAssert.Eq(f.Pilot.Stop, GroundStop.Foreign, "off-axis endpoint approach yields to a truck before the goal");
            TestAssert.That(f.Pilot.StopDistance <= 25f, "truck clearance limits physical endpoint recovery distance");
        }
    }

    internal static class GroundLineupOccupiedRecoveryTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            f.Tick(GroundFixture.At(f.Hold), 2.1f);
            f.Tick(GroundFixture.At(f.Hold), 2.2f);
            Vec3 slot = LineupPlanner.Slot(f.Traffic.Runway, false, 0, 0, 1, 1);
            AircraftState off = GroundFixture.At(slot + Vec3.Right * 60f);
            f.Traffic.Report(2, slot);
            f.Tick(off, 3f);
            f.Tick(off, 3.1f);
            f.Tick(off, 3.1f + GroundPilot.AlignSettleSeconds + 0.1f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "occupied lineup slot never permits an off-runway roll");
            TestAssert.That(!f.Pilot.TakeRelocation(out _), "lineup recovery never relocates onto another aircraft");
            f.Traffic.Leave(2);
            f.Tick(off, 3.1f + GroundPilot.AlignSettleSeconds + 0.2f);
            TestAssert.That(f.Pilot.TakeRelocation(out _), "lineup recovery retries once its slot is free");
        }
    }

    internal static class GroundLineupNormalArrivalTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            f.Tick(GroundFixture.At(f.Hold), 2.1f);
            f.Tick(GroundFixture.At(f.Hold), 2.2f);
            Vec3 slot = LineupPlanner.Slot(f.Traffic.Runway, false, 0, 0, 1, 1);
            f.Tick(GroundFixture.At(slot), 3f);
            f.Tick(GroundFixture.At(slot), 3.1f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.Roll, "normal centered and aligned lineup still begins rolling");
            TestAssert.That(!f.Pilot.TakeRelocation(out _), "normal lineup requires no recovery relocation");
        }
    }

    internal static class GroundRollOvershootTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            f.Tick(GroundFixture.At(f.Hold), 2.1f);
            f.Tick(GroundFixture.At(f.Hold), 2.2f);
            Vec3 slot = LineupPlanner.Slot(f.Traffic.Runway, false, 0, 0, 1, 1);
            f.Tick(GroundFixture.At(slot), 3f);
            f.Tick(GroundFixture.At(slot), 3.1f);
            f.Tick(GroundFixture.At(slot), 3.2f);
            Vec3 beyond = slot + Vec3.Forward * (Math.Max(500f, f.Traffic.Runway.Length) + 20f);
            ControlOutput o = f.Tick(GroundFixture.At(beyond), 3.3f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.Roll, "roll overshoot stays in the takeoff phase while still on the wheels");
            TestAssert.Near(o.Throttle, 1f, "roll overshoot retains full takeoff throttle");
            TestAssert.Near(o.Yaw, 0f, "roll overshoot keeps steering straight ahead");
        }
    }

    internal sealed class GroundFixture
    {
        public static readonly Vec3 Spawn = new Vec3(-100f, 0f, -100f);
        public readonly AirframeProfile Profile = new AirframeProfile();
        public readonly FieldTraffic Traffic;
        public readonly GroundPilot Pilot;
        public Vec3 Hold => Traffic.Graph.NodePos(Traffic.Graph.HoldShort(0, false));

        public GroundFixture()
        {
            var field = new AirbaseSample
            {
                Name = "Ground regression field", Center = Spawn,
                ServicePoints = new[] { new WingPose(Spawn, Vec3.Forward) },
                Runways = new[] { new RunwaySample
                {
                    Start = Vec3.Zero, End = new Vec3(0f, 0f, 1000f), Length = 1000f, Width = 40f,
                    Takeoff = true, Entries = new[] { new WingPose(new Vec3(-100f, 0f, 0f), Vec3.Right) },
                } },
            };
            Traffic = new FieldTraffic(field, 0, false);
            Traffic.Departures.Expect(1, 1);
            Pilot = new GroundPilot(1, Traffic, AirframeClass.FixedWing, new WingPose(Spawn, Vec3.Forward), -1, Traffic.Graph.ServiceNode(0));
            Tick(At(Spawn), 0f);
            Tick(At(Spawn), 2f);
            TestAssert.Eq(Pilot.Phase, GroundPhase.TaxiOut, "fixture starts taxiing");
        }

        public ControlOutput Tick(AircraftState s, float time) => Pilot.Step(s, Profile, null, time, 0.1f, null, 0);

        public static AircraftState At(Vec3 pos, Vec3? forward = null) => new AircraftState
        {
            Pos = pos, Fwd = forward ?? Vec3.Forward, Up = Vec3.Up,
            Right = Vec3.Cross(Vec3.Up, forward ?? Vec3.Forward),
        };

        public static void CheckLineupRecovery(Vec3 offset)
        {
            var f = new GroundFixture();
            f.Tick(At(f.Hold), 2.1f);
            f.Tick(At(f.Hold), 2.2f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "fixture reaches lineup");
            Vec3 slot = LineupPlanner.Slot(f.Traffic.Runway, false, 0, 0, 1, 1);
            AircraftState off = At(slot + offset);
            f.Tick(off, 3f);
            f.Tick(off, 3.1f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "a stopped projection past the slot does not roll off the runway");
            f.Tick(off, 3.1f + GroundPilot.AlignSettleSeconds + 0.1f);
            TestAssert.That(f.Pilot.TakeRelocation(out WingPose pose), "stopped off-slot lineup uses bounded existing slot recovery");
            TestAssert.Near((pose.Pos - slot).Horizontal.Length, 0f, "lineup recovery targets the reserved slot");
            f.Tick(At(pose.Pos, pose.Fwd), 9f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.Roll, "rolling begins after the aircraft occupies the slot");
        }
    }
}
