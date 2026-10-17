using System;
using System.Reflection;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class GroundAsideRecoveryTests
    {
        public static void Run()
        {
            var fixture = new GroundFixture();
            GroundPilot pilot = fixture.Pilot;
            int aside = fixture.Traffic.Graph.ServiceNode(0);
            int hold = fixture.Traffic.Graph.HoldShort(0, false);
            Vec3 refuge = GroundFixture.Spawn + Vec3.Right * 30f;
            // Recreate the refuge state reached by the real four-member SE airport model. Reflection only
            // primes this otherwise long scenario; both watchdog expiry and subsequent taxi use public Step.
            typeof(GroundPilot).GetMethod("SetPath", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(pilot, new object[] { new[] { GroundFixture.Spawn, refuge } });
            Set(pilot, "pulledAside", true);
            Set(pilot, "asideFor", 2);
            Set(pilot, "asideNode", aside);
            Set(pilot, "asideSince", 0f);
            fixture.Traffic.Report(2, fixture.Hold);
            fixture.Traffic.ReportRoute(2, new[] { hold, aside }, Array.Empty<int>(), 0);
            pilot.Watchdog.Update(refuge, false, 0f);
            AircraftState state = GroundFixture.At(refuge);
            pilot.Step(state, fixture.Profile, null, 3f, StuckWatchdog.RelocateSeconds, null, 0);
            pilot.Step(state, fixture.Profile, null, 3.1f, 0.1f, null, 0);
            TestAssert.That(pilot.Phase == GroundPhase.TaxiOut || pilot.Phase == GroundPhase.HoldShort,
                "expired refuge resumes taxi with a coherent route");
            TestAssert.Eq(pilot.Stop, GroundStop.Relocating, "occupied relocation goal leaves a bounded pending recovery");
            TestAssert.That(!pilot.TakeRelocation(out _), "refuge recovery never relocates onto the occupied hold-short");
        }

        private static void Set(GroundPilot pilot, string name, object value) =>
            typeof(GroundPilot).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pilot, value);
    }
}
