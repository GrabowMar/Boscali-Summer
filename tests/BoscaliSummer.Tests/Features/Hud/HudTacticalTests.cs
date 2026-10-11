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
            Retention();
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

        /// <summary>How long the radar-warning page keeps a radar: until the hold after its newest warning, a lock or a search ping.</summary>
        private static void Retention()
        {
            float never = float.NegativeInfinity;
            TestAssert.That(!HudThreats.Retained(10f, never, never, 6f), "A radar that never warned is not kept");
            TestAssert.That(HudThreats.Retained(15.5f, never, 10f, 6f) && !HudThreats.Retained(16f, never, 10f, 6f), "A search ping keeps the radar for the hold, not for the lamp's 1.5 s");
            TestAssert.That(HudThreats.Retained(15.5f, 10f, never, 6f) && !HudThreats.Retained(16f, 10f, never, 6f), "...and so does a lock, not only its 2 s");
            TestAssert.That(HudThreats.Retained(17.5f, 10f, 12f, 6f) && !HudThreats.Retained(18f, 10f, 12f, 6f), "The newest of the lock and the ping counts: a ping after the lock");
            TestAssert.That(HudThreats.Retained(17.5f, 12f, 10f, 6f) && !HudThreats.Retained(18f, 12f, 10f, 6f), "...and a lock after the ping");
            TestAssert.That(HudThreats.Retained(39.5f, never, 10f, 30f) && !HudThreats.Retained(40f, never, 10f, 30f), "The hold is the option's: 30 s keeps it 30 s");
            TestAssert.That(HudThreats.Retained(11.5f, never, 10f, 2f) && !HudThreats.Retained(12f, never, 10f, 2f), "...and 2 s, the shortest, 2 s");
            TestAssert.That(BoardOptions.HoldMin >= HudThreats.LockHold && BoardOptions.HoldMin >= HudThreats.SpikeHold, "The option never keeps a radar for less than the lamps do");

            // A search radar that pings every 5 s: with the hold it stays on the page between pings (it used to pop off after the lamp's 1.5 s and on again at the next ping).
            bool kept = true, droppedBefore = false;
            for (int k = 0; k <= 600; k++)
            {
                float now = k * 0.1f, last = (k / 50) * 5f;
                kept &= HudThreats.Retained(now, never, last, BoardOptions.Default.ThreatHoldSeconds);
                droppedBefore |= now - last >= HudThreats.SpikeHold;
            }
            TestAssert.That(kept, "A radar pinging every 5 s is on the page for a whole minute");
            TestAssert.That(droppedBefore, "...where the lamp's own hold alone would have let it go between pings");
        }

        private static void Shots()
        {
            var ledger = new ShotLedger();
            Func<int, bool> allFlying = _ => true, noneFlying = _ => false;
            ledger.Launch(1, false, "SKYWY", 0f);
            ledger.Launch(2, true, "GBU12", 0f);
            ledger.Detonate(1, true, 5f, 100f, 6200f, -40f);
            int one = ledger.IndexOf(1), two = ledger.IndexOf(2);
            TestAssert.That(ledger.State(one) == ShotState.Hit && ledger.State(two) == ShotState.InFlight, "Hit resolves only its own entry");
            TestAssert.That(ledger.TryPlace(one, out float px, out float py, out float pz) && px == 100f && py == 6200f && pz == -40f && !ledger.TryPlace(two, out _, out _, out _), "A resolved entry keeps where it ended; one in flight has no place");
            TestAssert.That(ledger.Name(one) == "SKYWY" && ledger.IsBomb(two) && ledger.Id(one) == 1, "An entry keeps its weapon's name, kind and id");
            ledger.Detonate(99, false, 5f);
            TestAssert.That(ledger.State(two) == ShotState.InFlight, "Unknown detonation changes nothing");
            ledger.Expire(8f, allFlying);
            TestAssert.That(ledger.State(one) == ShotState.Hit, "A result stays on the timeline for a minute (not the old 2.5 s)");
            ledger.Expire(66f, allFlying);
            TestAssert.That(ledger.State(one) == ShotState.Empty && ledger.IndexOf(1) < 0, "...and then clears");

            // A cruise missile in the air for twenty minutes is still in flight; pausing the game cannot age it out.
            ledger.Expire(1200f, allFlying);
            TestAssert.That(ledger.State(two) == ShotState.InFlight, "A weapon the game still has airborne never times out");
            ledger.Expire(1202f, noneFlying);
            TestAssert.That(ledger.State(two) == ShotState.InFlight, "A brief gap before the detonation RPC lands keeps the entry");
            ledger.Expire(1204f, noneFlying);
            TestAssert.That(ledger.State(two) == ShotState.Miss, "Gone from the air for the grace period with no detonation: a miss");

            // A weapon whose seeker lost a living target and then misses is DEFEATED; one that still hits is a hit.
            var lost = new ShotLedger();
            lost.Launch(5, false, "DART", 0f);
            lost.Launch(6, false, "SKYWY", 0f);
            lost.MarkLost(5);
            lost.MarkLost(6);
            lost.Detonate(5, false, 3f);
            lost.Detonate(6, true, 3f);
            TestAssert.That(lost.State(lost.IndexOf(5)) == ShotState.Defeated && lost.State(lost.IndexOf(6)) == ShotState.Hit, "A miss after the lock was lost is DEFEATED; a hit stays a hit");
            lost.Launch(7, false, "DART", 4f);
            lost.MarkLost(7);
            lost.Expire(20f, noneFlying);
            TestAssert.That(lost.State(lost.IndexOf(7)) == ShotState.Defeated, "...also when it just vanished");

            var order = new ShotLedger();
            for (int i = 0; i < ShotLedger.Capacity + 3; i++) order.Launch(10 + i, false, "W" + i, 1210f + i);
            int flying = 0;
            for (int i = 0; i < ShotLedger.Capacity; i++) if (order.State(i) == ShotState.InFlight) flying++;
            TestAssert.That(flying == ShotLedger.Capacity, "A full ring keeps every entry in flight");
            var slots = new int[ShotLedger.Capacity];
            TestAssert.That(order.InOrder(slots) == ShotLedger.Capacity && order.Id(slots[0]) == 10 && order.Id(slots[ShotLedger.Capacity - 1]) == 10 + ShotLedger.Capacity - 1, "Entries come back in launch order");
            order.Detonate(12, true, 1230f);
            order.Launch(40, false, "NEW", 1231f);
            TestAssert.That(order.IndexOf(12) < 0 && order.IndexOf(40) >= 0, "A new launch takes the place of the oldest resolved entry");
            order.InOrder(slots);
            TestAssert.That(order.Id(slots[ShotLedger.Capacity - 1]) == 40, "...and is the newest in the order");

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
