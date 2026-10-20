using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins REMORA "ARCHER" fire control, phase rules, HUD copy, elevons and slot reuse.</summary>
    internal static class RemoraTests
    {
        public static void Run()
        {
            TestAssert.That(RemoraArcher.Rounds == 2, "two missiles only: four would be too strong");
            TestAssert.That(RemoraArcher.Left(0) == 2 && RemoraArcher.Left(1) == 1 && RemoraArcher.Left(2) == 1 &&
                RemoraArcher.Left(3) == 0, "two rounds, one bit per rail");
            TestAssert.That(RemoraArcher.NextPylon(0) == 0 && RemoraArcher.NextPylon(1) == 1 && RemoraArcher.NextPylon(2) == 0 &&
                RemoraArcher.NextPylon(3) == -1, "left rail first, then right, then empty");

            TestAssert.That(RemoraArcher.Decide(false, true, 2, false) == RemoraPhase.Escort, "no designation holds the slot");
            TestAssert.That(RemoraArcher.Decide(true, true, 1, true) == RemoraPhase.Engage, "rounds left keep engaging an air target");
            TestAssert.That(RemoraArcher.Decide(true, true, 0, true) == RemoraPhase.Escort, "after the volley the same cue sends it home");
            TestAssert.That(RemoraArcher.Decide(true, true, 0, false) == RemoraPhase.Ram, "a fresh cue with no rounds rams");
            TestAssert.That(RemoraArcher.Decide(true, false, 2, false) == RemoraPhase.Ram, "ground designations are bombed, rounds kept");

            TestAssert.That(RemoraArcher.CanFire(11999f, 59f, 1.5f, 1, true), "inside 12 km, the 60 deg cone and the 1.5 s interval");
            TestAssert.That(!RemoraArcher.CanFire(12001f, 0f, 9f, 2, true), "beyond 12 km holds fire");
            TestAssert.That(!RemoraArcher.CanFire(5000f, 61f, 9f, 2, true), "outside the nose cone holds fire");
            TestAssert.That(!RemoraArcher.CanFire(5000f, 0f, 1.4f, 1, true), "1.5 s between rounds");
            TestAssert.That(!RemoraArcher.CanFire(5000f, 0f, 9f, 0, true), "empty rails hold fire");
            TestAssert.That(!RemoraArcher.CanFire(5000f, 0f, 9f, 2, false), "no faction track holds fire");
            TestAssert.That(RemoraArcher.CanFire(8000f, 110f, 2f, 1, true, RemoraArcher.DefenceCone), "defence shots at inbound missiles use the wide cone");
            TestAssert.That(!RemoraArcher.CanFire(8000f, 110f, 2f, 1, true), "the same shot on the hunt cone holds fire");
            TestAssert.That(RemoraArcher.HoldsForTrack(5000f, 10f, 2, false), "in range and cone, rounds left, no track = waiting on a sensor");
            TestAssert.That(!RemoraArcher.HoldsForTrack(5000f, 10f, 2, true) && !RemoraArcher.HoldsForTrack(13000f, 10f, 2, false) &&
                !RemoraArcher.HoldsForTrack(5000f, 70f, 2, false) && !RemoraArcher.HoldsForTrack(5000f, 10f, 0, false),
                "tracked, out of range, out of cone or empty is not a track hold");

            TestAssert.That(RemoraArcher.CanDefend(RemoraArcher.DefendRange, 1) && !RemoraArcher.CanDefend(RemoraArcher.DefendRange + 1f, 1) &&
                !RemoraArcher.CanDefend(5000f, 0), "defence envelope needs a round and an inbound inside 9 km");
            TestAssert.That(RemoraArcher.FlareBursts == 6, "six flare bursts");
            TestAssert.That(RemoraArcher.ShouldFlare(2500f, 300f, 6, RemoraArcher.FlareInterval), "close, fast IR missile draws a burst");
            TestAssert.That(!RemoraArcher.ShouldFlare(3100f, 300f, 6, 9f) && !RemoraArcher.ShouldFlare(2500f, 50f, 6, 9f) &&
                !RemoraArcher.ShouldFlare(2500f, 300f, 0, 9f) && !RemoraArcher.ShouldFlare(2500f, 300f, 6, 0.5f),
                "far, slow, empty or just-popped holds the flares");

            TestAssert.That(Near(RemoraArcher.FuseRadius(0f, 0.02f), RemoraArcher.RamFuseMin) &&
                Near(RemoraArcher.FuseRadius(-300f, 0.02f), RemoraArcher.RamFuseMin), "slow or opening bomb keeps the tight 6 m fuse");
            TestAssert.That(Near(RemoraArcher.FuseRadius(700f, 0.02f), 8.4f), "700 m/s closing: window 1.2 steps wide");
            TestAssert.That(Near(RemoraArcher.FuseRadius(5000f, 0.02f), RemoraArcher.RamFuseMax), "fuse never opens past 12 m");

            TestAssert.That(RemoraArcher.Status(2, 2, RemoraPhase.Escort) == "ARCHER 2/2 READY", "ready line");
            TestAssert.That(RemoraArcher.Status(3, 4, RemoraPhase.Engage) == "ARCHER ENGAGING 3/4", "engaging line sums drones");
            TestAssert.That(RemoraArcher.Status(0, 2, RemoraPhase.Ram) == "REMORA GUIDED BOMB — INBOUND", "bomb line");
            TestAssert.That(RemoraArcher.Status(0, 2, RemoraPhase.Escort) == "REMORA WINCHESTER", "empty escort line");

            RemoraArcher.Elevons(0f, 0f, out float l0, out float r0);
            TestAssert.That(l0 == 0f && r0 == 0f, "wings level, elevons neutral");
            RemoraArcher.Elevons(30f, 0f, out float l1, out float r1);
            TestAssert.That(Near(l1, 4.5f) && Near(r1, 4.5f), "a 30 deg bank pulls both trailing edges up 4.5 deg");
            RemoraArcher.Elevons(0f, 40f, out float l2, out float r2);
            TestAssert.That(Near(l2, -10f) && Near(r2, 10f), "rolling right 40 deg/s splits them -10/+10");
            RemoraArcher.Elevons(-90f, 200f, out float l3, out float r3);
            TestAssert.That(Near(l3, -5f) && Near(r3, RemoraArcher.ElevonLimit), "pull caps at 10, roll at 15, total at 25");

            TestAssert.That(FormationSlot.FirstFree(new HashSet<int>()) == 0, "first drone takes slot 0");
            TestAssert.That(FormationSlot.FirstFree(new HashSet<int> { 0, 1 }) == 2, "third drone echelons out");
            TestAssert.That(FormationSlot.FirstFree(new HashSet<int> { 1 }) == 0, "a lost slot-0 wingman is replaced, not doubled on slot 1");
            TestAssert.That(FormationSlot.FirstFree(new HashSet<int> { 0, 2 }) == 1, "gaps fill lowest first");
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f;
    }
}
