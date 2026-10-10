using System;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>Threat lamps, shot tracker, lamp test and attitude maths for the tactical HUD features.</summary>
    internal static class HudTacticalTests
    {
        public static void Run()
        {
            Threats();
            Shots();
            Test();
            Angles();
        }

        private static void Threats()
        {
            var lamps = new Lamp[HudThreats.Count];
            HudThreats.Fill(ThreatPicture.Clear, lamps);
            foreach (Lamp l in lamps) TestAssert.That(l.Present && l.State == LampState.Dark, "Quiet sky: threat lamps dark " + l.Legend);

            ThreatPicture t = ThreatPicture.Clear;
            t.SinceLock = 0.4f; t.SinceSpike = 0.2f; t.Missiles = 2;
            HudThreats.Fill(t, lamps);
            TestAssert.That(lamps[0].State == LampState.Warning && lamps[0].Flash, "Fresh lock flashes red");
            TestAssert.That(lamps[1].State == LampState.Caution, "Spike lights amber");
            TestAssert.That(lamps[2].Legend == "MSL 2" && lamps[2].Flash, "Two inbound: MSL 2 flashing");
            t.SinceLock = 1.5f; HudThreats.Fill(t, lamps);
            TestAssert.That(lamps[0].State == LampState.Warning && !lamps[0].Flash, "Held lock goes steady");
            t.SinceLock = 3f; HudThreats.Fill(t, lamps);
            TestAssert.That(lamps[0].State == LampState.Dark, "Lock times out");

            HudThreats.Clock(1f, 0f, 1f, out float bearing, out float elevation);
            TestAssert.That(Math.Abs(bearing - 45f) < 0.01f && Math.Abs(elevation) < 0.01f, "Right-front is +45 degrees");
            HudThreats.Clock(0f, 1f, -1f, out bearing, out elevation);
            TestAssert.That(Math.Abs(Math.Abs(bearing) - 180f) < 0.01f && HudThreats.HighLow(elevation) == "HI", "Behind and above: 180, HI");
        }

        private static void Shots()
        {
            var ledger = new ShotLedger();
            Func<int, bool> allFlying = _ => true, noneFlying = _ => false;
            ledger.Launch(1, false, 0f);
            ledger.Launch(2, true, 0f);
            ledger.Detonate(1, true, 5f);
            TestAssert.That(ledger.State(0) == ShotState.Hit && ledger.State(1) == ShotState.InFlight, "Hit resolves only its own pip");
            ledger.Detonate(99, false, 5f);
            TestAssert.That(ledger.State(1) == ShotState.InFlight, "Unknown detonation changes nothing");
            ledger.Expire(8f, allFlying);
            TestAssert.That(ledger.State(0) == ShotState.Empty, "Resolved pip clears after its hold");

            // A cruise missile in the air for twenty minutes is still a pip; pausing the game cannot age it out.
            ledger.Expire(1200f, allFlying);
            TestAssert.That(ledger.State(1) == ShotState.InFlight, "A weapon the game still has airborne never times out");
            ledger.Expire(1202f, noneFlying);
            TestAssert.That(ledger.State(1) == ShotState.InFlight, "A brief gap before the detonation RPC lands keeps the pip");
            ledger.Expire(1204f, noneFlying);
            TestAssert.That(ledger.State(1) == ShotState.Miss, "Gone from the air for the grace period with no detonation: a miss");

            for (int i = 0; i < ShotLedger.Capacity + 3; i++) ledger.Launch(10 + i, false, 1210f);
            int flying = 0;
            for (int i = 0; i < ShotLedger.Capacity; i++) if (ledger.State(i) == ShotState.InFlight) flying++;
            TestAssert.That(flying == ShotLedger.Capacity, "A full ring keeps every pip in flight");

            var legend = new ShotLedger();
            TestAssert.That(legend.Legend == "", "No pips, no legend");
            legend.Launch(1, false, 0f);
            TestAssert.That(legend.Legend == "SHOT", "A pip in flight reads SHOT");
            legend.Detonate(1, true, 1f);
            TestAssert.That(legend.Legend == "HIT", "The latest outcome reads HIT");
            legend.Launch(2, false, 1.5f);
            legend.Detonate(2, false, 2f);
            TestAssert.That(legend.Legend == "MISS", "A later miss replaces it");
            legend.Expire(10f, null);
            TestAssert.That(legend.Legend == "", "Resolved pips clear and take the legend with them");

            TestAssert.That(ShotRules.Hit(true, false), "A dumb bomb that struck armour is a hit even with no target");
            TestAssert.That(ShotRules.Hit(false, true), "A burst within 200 m of its target is a hit (even when it also flags the ground, as a ground target's impact does)");
            TestAssert.That(!ShotRules.Hit(false, false), "A terrain-only impact or a self-destruct with no target in reach is a miss");

            TestAssert.That(!ShotRules.IsDelivery(1f, false) && !ShotRules.IsDelivery(2f, false), "MALD-X/J and ALE-X decoys earn no pip");
            TestAssert.That(!ShotRules.IsDelivery(0.2f, false) && !ShotRules.IsDelivery(0f, false), "AEGIS darts and GLAIVE pods earn no pip");
            TestAssert.That(ShotRules.IsDelivery(60f, false) && ShotRules.IsDelivery(450f, false), "REMORA and ORCA strikes earn a pip");
            TestAssert.That(!ShotRules.IsDelivery(500f, true), "A jammer is never a delivery");
        }

        private static void Test()
        {
            TestAssert.That(LampTest.Lit(0, 10, 0.05f) && !LampTest.Lit(9, 10, 0.05f), "The sweep lights lamps in order");
            TestAssert.That(LampTest.Lit(9, 10, 0.89f), "Every lamp is lit by the end of the sweep");
            TestAssert.That(!LampTest.Running(2f) && LampTest.Caption(2f) == "SYSTEMS ONLINE" && LampTest.Caption(3f) == null,
                "Test ends, says ONLINE, then gets out of the way");
        }

        private static void Angles()
        {
            TestAssert.That(Math.Abs(Attitude.Bank(0f, 1f)) < 0.01f, "Wings level: zero bank");
            TestAssert.That(Math.Abs(Attitude.Bank(-0.7071f, 0.7071f) - 45f) < 0.1f, "Right wing down 45: +45");
            TestAssert.That(Attitude.Slip(0f, 100f) == 0f && Attitude.Slip(5f, 5f) == 0f, "No slip in coordinated or slow flight");
            TestAssert.That(Attitude.Slip(10f, 100f) > 5f, "Air from the right reads positive");
        }
    }
}
