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

    internal static class GroundTaxiPassedGoalTests
    {
        public static void Run()
        {
            // wing-taxi sim 2026-10-18 (highway strip): an FS-20 ran a few metres past a hold-short at the end of a sharp turn,
            // circled back for the exact point and beached itself off the strip.
            var f = new GroundFixture();
            Vec3 past = f.Hold + Vec3.Forward * 6f;
            AircraftState rolling = GroundFixture.At(past);
            rolling.Vel = Vec3.Forward * 3f;
            rolling.Tas = 3f;
            f.Tick(rolling, 2.1f);
            ControlOutput o = f.Tick(rolling, 2.2f);
            TestAssert.That(o.Throttle == 0f && o.Brake > 0f, "a hold-short just passed is braked for, not turned back to");
            f.Tick(GroundFixture.At(past), 2.3f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "stopped just past the hold-short, it lines up from there");

            // wing-taxi sweep 2026-10-18 (airbase_boscali_north, SFB-81): a 42 m bomber swept past its hold-short 18 m away, outside
            // that radius, and circled on the grass for a minute to reach the exact point.
            Vec3 wide = f.Hold + Vec3.Forward * 18f;
            AircraftState sweeping = GroundFixture.At(wide);
            sweeping.Vel = Vec3.Forward * 3f;
            sweeping.Tas = 3f;
            var small = new GroundFixture();
            small.Tick(sweeping, 2.1f);
            TestAssert.That(small.Tick(sweeping, 2.2f).Throttle > 0f, "a fighter 18 m past its hold-short still drives back to it");
            var large = new GroundFixture();
            large.Profile.SpanM = 42f;
            large.Tick(sweeping, 2.1f);
            ControlOutput b = large.Tick(sweeping, 2.2f);
            TestAssert.That(b.Throttle == 0f && b.Brake > 0f, "a hold-short under a bomber's wing is braked for, not circled back to");
        }
    }

    internal static class GroundTakeoffRunTests
    {
        public static void Run()
        {
            // The user's ruling 2026-10-19: launch fuel stays at 100 %, and SUPPLY flags the fields a fully fuelled SFB-81 cannot leave
            // (sweep: highway strips 834/889 m, airstrip_city2 1199 m, desert1 1306 m ran it off the end; navalbase 1907 m just made it).
            foreach (float runway in new[] { 834f, 889f, 1199f, 1306f })
                TestAssert.That(TakeoffRuns.Short("SFB-81 Darkreach", runway), "an SFB-81 is flagged short at a " + runway + " m runway");
            TestAssert.That(!TakeoffRuns.Short("SFB-81 Darkreach", 1907f) && !TakeoffRuns.Short("SFB-81 Darkreach", 2006f),
                "navalbase and airbase_city are long enough for it");
            TestAssert.That(!TakeoffRuns.Short("KR-67 Ifrit", 834f) && !TakeoffRuns.Short("FS-20 Vortex", 834f),
                "types with no known limit are never flagged (a KR-67 flies from 834 m)");
            TestAssert.That(!TakeoffRuns.Short("SFB-81 Darkreach", 0f), "a field with no takeoff runway is not called short");
        }
    }

    internal static class GroundPullAsideNeedTests
    {
        public static void Run()
        {
            // wing-taxi sweep 2026-10-18 (airbase_city): a deadlock victim pulled aside off a node the member it waited for never
            // drives through, was back on its route the next tick, and pulled aside again every second (30 times in a row).
            var field = new AirbaseSample
            {
                Name = "Pull-aside need field", Center = new Vec3(-100f, 0f, -150f),
                Roads = new[]
                {
                    new[] { new Vec3(-100f, 0f, -300f), new Vec3(-100f, 0f, -200f) },
                    new[] { new Vec3(-100f, 0f, -200f), new Vec3(-100f, 0f, -100f) },
                    new[] { new Vec3(-100f, 0f, -100f), new Vec3(-100f, 0f, 0f) },
                    new[] { new Vec3(-100f, 0f, -200f), new Vec3(-200f, 0f, -200f) },
                },
                Runways = new[] { new RunwaySample
                {
                    Start = Vec3.Zero, End = new Vec3(0f, 0f, 1000f), Length = 1000f, Width = 40f,
                    Takeoff = true, Entries = new[] { new WingPose(new Vec3(-100f, 0f, 0f), Vec3.Right) },
                } },
            };
            var traffic = new FieldTraffic(field, 0, false);
            var profile = new AirframeProfile();
            var events = new WingEventRing();
            // #1 stands just past the junction at z -100, facing #2 (nose to nose); its route starts at that junction, never at #2's.
            AircraftState one = GroundFixture.At(new Vec3(-100f, 0f, -110f), -Vec3.Forward), two = GroundFixture.At(new Vec3(-100f, 0f, -200f));
            var first = new GroundPilot(1, traffic, AirframeClass.FixedWing, new WingPose(one.Pos, one.Fwd), -1);
            var second = new GroundPilot(2, traffic, AirframeClass.FixedWing, new WingPose(two.Pos, two.Fwd), -1);
            traffic.Departures.Expect(1, 1);
            traffic.Departures.Expect(2, 1);
            for (float t = 0f; t < 10f; t += 0.1f)
            {
                traffic.Report(1, one.Pos);
                traffic.Report(2, two.Pos);
                traffic.Step(0.1f);
                first.Step(one, profile, null, t, 0.1f, events, 0);
                second.Step(two, profile, null, t, 0.1f, events, 1);
            }
            TestAssert.Eq(events.CountOf(WingEventKind.PulledAside), 0, "no pull-aside off a node the other member does not need");
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
