using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins the ORCA SLEEPER numbers: drop range, rest depth, wake/expiry budget, run ramp, HUD lines.</summary>
    internal static class OrcaTests
    {
        public static void Run()
        {
            TestAssert.Near(OrcaSleeper.DropRange(0f), 3000f, "low launch lays the sleeper 3 km out");
            TestAssert.Near(OrcaSleeper.DropRange(2000f), 5000f, "2 km up glides 5 km");
            TestAssert.Near(OrcaSleeper.DropRange(9000f), 12000f, "drop range caps at 12 km");

            TestAssert.Near(OrcaSleeper.RestDepth(-500f), -35f, "deep water: sleeps at -35 m");
            TestAssert.Near(OrcaSleeper.RestDepth(-20f), -16f, "shallow: 4 m off the seabed");
            TestAssert.Near(OrcaSleeper.RestDepth(-5f), -8f, "never shallower than -8 m");

            TestAssert.That(!OrcaSleeper.Expired(599.9f) && OrcaSleeper.Expired(600f), "sleeps up to 600 s");
            TestAssert.Eq(OrcaSleeper.WakeRange, 1200f, "hostile hull within 1.2 km wakes it");
            TestAssert.Eq(OrcaSleeper.AttackRadius, 2000f, "drop point within 2 km of a hostile hull attacks it");
            TestAssert.Eq(OrcaSleeper.ScanInterval, 1f, "sleeper scans at 1 Hz");

            TestAssert.Near(OrcaSleeper.Settle(new Vector3(200f, 0f, 0f), 1f).x, 44.626f, "splash coast decays e^-1.5 per second");
            TestAssert.Near(WaterRun.VerticalAccel(-35f, 0f, OrcaSleeper.SleepDepth), 0f, "spring holds the sleep depth");
            TestAssert.Near(WaterRun.VerticalAccel(0f, 0f, OrcaSleeper.SleepDepth), -140f, "splash sinks hard toward -35 m");
            TestAssert.Near(WaterRun.VerticalAccel(-35f, 0f), 116f, "woken sleeper rises toward the -6 m run depth");
            TestAssert.Near(WaterRun.RunSpeed(0f, 1f), 30f, "woken run ramps 30 m/s^2");
            TestAssert.Near(WaterRun.RunSpeed(200f, 0.02f), 90f, "fast entries clamp to the run speed");

            TestAssert.Near(OrcaSleeper.RotorDegPerSec(1f), 0f, "rotor still while asleep");
            TestAssert.Near(OrcaSleeper.RotorDegPerSec(5f), 180f, "rotor idles at 180 deg/s");
            TestAssert.Near(OrcaSleeper.RotorDegPerSec(90f), 1440f, "rotor 4 rev/s on the run");
            TestAssert.That(OrcaSleeper.Bubbles(-6f, 90f) && !OrcaSleeper.Bubbles(-35f, 0.5f) && !OrcaSleeper.Bubbles(50f, 250f),
                "bubble trail only for a running torpedo under water");

            TestAssert.Eq(OrcaSleeper.Line(OrcaCall.Set, 7.46f), "SLEEPER SET 7.5 KM", "set line carries the distance");
            TestAssert.Eq(OrcaSleeper.Line(OrcaCall.Awake, 0f), "SLEEPER AWAKE — RUN", "wake line");
            TestAssert.Eq(OrcaSleeper.Line(OrcaCall.Hit, 0f), "ORCA HIT", "hit line");
            TestAssert.Eq((byte)OrcaCall.Expired, (byte)4, "OrcaCall byte values are wire format");
        }
    }
}
