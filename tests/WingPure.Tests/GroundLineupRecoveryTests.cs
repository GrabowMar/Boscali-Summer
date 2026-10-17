using System;
using System.Reflection;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class GroundLineupRecoveryTests
    {
        public static void Run()
        {
            TowRequiresWatchdog();
            TowRequiresEnqueue();
            foreach (bool nativeLanding in new[] { false, true })
            {
                GroundFixture f = PendingTow();
                f.Traffic.Departures.NativeLandingPending = nativeLanding;
                f.Traffic.Departures.RunwayBusy = !nativeLanding;
                f.Tick(GroundFixture.At(GroundFixture.Spawn), 34f);
                AssertDenied(f, nativeLanding ? "native landing" : "busy runway");
                f.Traffic.Departures.NativeLandingPending = f.Traffic.Departures.RunwayBusy = false;
                AssertTowAndPhysicalRoll(f, 35f);
            }
            foreach (bool recentRelocation in new[] { false, true })
            {
                GroundFixture f = PendingTow();
                Vec3 slot = Slot(f);
                if (recentRelocation) f.Traffic.NoteRelocation(3, slot);
                else f.Traffic.Report(3, slot);
                f.Tick(GroundFixture.At(GroundFixture.Spawn), 34f);
                AssertDenied(f, recentRelocation ? "recent slot relocation" : "occupied slot");
                if (recentRelocation) f.Traffic.Step(FieldTraffic.RelocationGuardSeconds + 0.1f);
                else f.Traffic.Leave(3);
                AssertTowAndPhysicalRoll(f, 35f);
            }
            foreach (bool nativeLanding in new[] { false, true })
            {
                GroundFixture f = PendingTow();
                TestAssert.That(f.Traffic.Departures.MayLineUp(1, 33f), "fixture already holds a runway for its group");
                f.Traffic.Departures.NativeLandingPending = nativeLanding;
                f.Traffic.Departures.RunwayBusy = !nativeLanding;
                f.Tick(GroundFixture.At(GroundFixture.Spawn), 34f);
                TestAssert.Eq(f.Pilot.Phase, GroundPhase.HoldShort, "new traffic blocks recovery even after the group locks the runway");
                TestAssert.That(!f.Pilot.TakeRelocation(out _), "a runway lock never overrides new landing or runway traffic");
                f.Traffic.Departures.NativeLandingPending = f.Traffic.Departures.RunwayBusy = false;
                AssertTowAndPhysicalRoll(f, 35f);
            }
        }

        private static void TowRequiresWatchdog()
        {
            var f = new GroundFixture();
            f.Traffic.Report(2, f.Hold);
            f.Tick(GroundFixture.At(GroundFixture.Spawn), 3f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.HoldShort, "blocked hold-short joins the normal departure queue");
            TestAssert.That(!f.Pilot.TakeRelocation(out _), "a blocked hold-short alone does not tow to the runway");
            f.Pilot.Step(GroundFixture.At(GroundFixture.Spawn), f.Profile, null, 33f, StuckWatchdog.RelocateSeconds, null, 0);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "expired watchdog may recover an enqueued aircraft onto its clear runway slot");
            TestAssert.That(f.Pilot.TakeRelocation(out WingPose pose), "watchdog recovery requests an explicit tow");
            TestAssert.Near((pose.Pos - Slot(f)).Horizontal.Length, 0f, "recovery uses the assigned runway slot");
        }

        private static void TowRequiresEnqueue()
        {
            var f = new GroundFixture();
            f.Traffic.Report(2, f.Hold);
            // Prime only the already-pending watchdog request; normal public taxiing must enqueue before it can tow.
            Set(f.Pilot, "relocationWanted", true);
            Set(f.Pilot, "relocateFrom", GroundFixture.Spawn);
            f.Tick(GroundFixture.At(GroundFixture.Spawn), 3f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.HoldShort, "a pending tow first reaches the departure queue");
            TestAssert.That(!f.Pilot.TakeRelocation(out _), "an unenqueued aircraft cannot recover directly onto the runway");
            TestAssert.That(f.Traffic.Departures.WaitingToLineUp(1), "no lineup order is assigned before enqueue completes");
        }

        private static GroundFixture PendingTow()
        {
            var f = new GroundFixture();
            f.Traffic.Report(2, f.Hold);
            f.Traffic.Departures.RunwayBusy = true;
            f.Tick(GroundFixture.At(GroundFixture.Spawn), 3f);
            f.Pilot.Step(GroundFixture.At(GroundFixture.Spawn), f.Profile, null, 33f, StuckWatchdog.RelocateSeconds, null, 0);
            TestAssert.That(!f.Pilot.TakeRelocation(out _), "fixture keeps the watchdog tow pending while the runway is busy");
            f.Traffic.Departures.RunwayBusy = false;
            return f;
        }

        private static void AssertDenied(GroundFixture f, string reason)
        {
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.HoldShort, reason + " denies runway recovery");
            TestAssert.That(!f.Pilot.TakeRelocation(out _), reason + " never requests a tow");
            TestAssert.That(f.Traffic.Departures.WaitingToLineUp(1) && !f.Traffic.Departures.RunwayLocked,
                reason + " preserves the queue without assigning a lineup order");
        }

        private static void AssertTowAndPhysicalRoll(GroundFixture f, float time)
        {
            f.Tick(GroundFixture.At(GroundFixture.Spawn), time);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "clearing the blocker permits the pending recovery");
            TestAssert.That(f.Pilot.TakeRelocation(out WingPose pose), "clear runway slot permits a tow request");
            f.Tick(GroundFixture.At(GroundFixture.Spawn), time + 0.1f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.LineUp, "a requested tow alone never starts the takeoff roll");
            f.Tick(GroundFixture.At(pose.Pos, pose.Fwd), time + 0.2f);
            TestAssert.Eq(f.Pilot.Phase, GroundPhase.Roll, "the physically occupied aligned slot permits the takeoff roll");
        }

        private static Vec3 Slot(GroundFixture f) => LineupPlanner.Slot(f.Traffic.Runway, false, 0, 0, 1, 1);

        private static void Set(GroundPilot pilot, string name, object value) =>
            typeof(GroundPilot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pilot, value);
    }

    internal static class GroundAsideTimeoutRecoveryTests
    {
        public static void Run()
        {
            var f = new GroundFixture();
            GroundPilot pilot = f.Pilot;
            int aside = f.Traffic.Graph.ServiceNode(0), hold = f.Traffic.Graph.HoldShort(0, false);
            Vec3 refuge = GroundFixture.Spawn + Vec3.Right * 30f;
            typeof(GroundPilot).GetMethod("SetPath", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(pilot, new object[] { new[] { GroundFixture.Spawn, refuge } });
            Set(pilot, "pulledAside", true); Set(pilot, "asideFor", 2);
            Set(pilot, "asideNode", aside); Set(pilot, "asideSince", 0f);
            f.Traffic.Report(2, f.Hold);
            f.Traffic.ReportRoute(2, new[] { hold, aside }, Array.Empty<int>(), 0);
            AircraftState state = GroundFixture.At(refuge);
            pilot.Step(state, f.Profile, null, GroundPilot.PullAsideSeconds + 1f, 0.1f, null, 0);
            TestAssert.That(Get<bool>(pilot, "relocationWanted"), "a refuge timeout with traffic still waiting retains a pending tow");
            TestAssert.That(!Get<bool>(pilot, "pulledAside"), "refuge timeout leaves the aside state");
            pilot.Step(state, f.Profile, null, GroundPilot.PullAsideSeconds + 1.1f, 0.1f, null, 0);
            TestAssert.That(pilot.Phase == GroundPhase.TaxiOut || pilot.Phase == GroundPhase.HoldShort,
                "the first post-timeout taxi step uses a coherent restored route");
            TestAssert.That(!pilot.TakeRelocation(out _), "a refuge timeout does not bypass departure enqueue or an occupied goal");
        }

        private static void Set(GroundPilot pilot, string name, object value) =>
            typeof(GroundPilot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pilot, value);

        private static T Get<T>(GroundPilot pilot, string name) =>
            (T)typeof(GroundPilot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pilot);
    }
}
