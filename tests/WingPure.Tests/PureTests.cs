using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Wing.Domain.Pure;

namespace WingPure.Tests
{
    internal static class StanceBookTests
    {
        private static Stance S(string id, bool builtIn = false) =>
            new Stance { Id = id, Name = id.ToUpperInvariant(), Axes = new byte[] { 1, 0, 1, 1, 2, 0, 0, 0 }, BuiltIn = builtIn };

        private static Stance[] BuiltIns() => new[] { S("reserve", true), S("escort", true), S("sweep", true) };

        private static StanceBook Book() => StanceBook.FromJson(null, BuiltIns(), new List<string>());

        public static void Run()
        {
            StanceBook b = Book();
            TestAssert.That(b.All.Count == 3, "built-ins load with no file");
            TestAssert.That(b.Slot(0).Id == "reserve" && b.Slot(1).Id == "escort" && b.Slot(2).Id == "sweep", "built-ins fill slots 1-3");
            TestAssert.That(b.Slot(3) == null && b.Slot(5) == null, "slots 4-6 start empty");
            TestAssert.That(b.Slot(-1) == null && b.Slot(6) == null, "out-of-range slot is null");

            TestAssert.That(b.Add(S("hunt")), "user stance adds");
            TestAssert.That(!b.Add(S("hunt")), "duplicate id refused");
            TestAssert.That(b.Assign(3, "hunt") && b.Slot(3).Id == "hunt", "assign puts a stance in a slot");
            TestAssert.That(!b.Assign(3, "nope"), "unknown id not assigned");
            b.SetDefault("cap", "hunt");
            TestAssert.That(b.DefaultFor("cap") == "hunt" && b.DefaultFor("move") == null, "order defaults");

            // RemoveClearsSlots (review focus 2)
            TestAssert.That(b.Remove("hunt"), "user stance removes");
            TestAssert.That(b.Slot(3) == null && b.DefaultFor("cap") == null, "removing clears its slot and defaults");
            TestAssert.That(!b.Remove("escort"), "built-ins cannot be removed");

            // Round trip
            b.Add(S("sead"));
            b.Assign(4, "sead");
            string json = b.ToJson();
            var errors = new List<string>();
            StanceBook c = StanceBook.FromJson(json, BuiltIns(), errors);
            TestAssert.That(errors.Count == 0, "clean round trip has no errors");
            TestAssert.That(c.Slot(4) != null && c.Slot(4).Id == "sead" && c.Slot(4).Axes[4] == 2, "round trip keeps slot and axes");
            TestAssert.That(c.Find("escort").BuiltIn, "built-ins are never read from the file");

            // Corrupt file (review focus 1)
            var bad = new List<string>();
            StanceBook d = StanceBook.FromJson("{ not json", new[] { S("reserve", true) }, bad);
            TestAssert.That(bad.Count > 0 && d.All.Count == 1 && d.Slot(0).Id == "reserve", "corrupt file keeps built-ins and reports");

            // Bounded
            StanceBook e = Book();
            for (int i = 0; i < 40; i++) e.Add(S("u" + i));
            TestAssert.That(e.All.Count == StanceBook.MaxStances, "stance count is bounded");
        }
    }

    internal static class StanceDiffTests
    {
        public static void Run()
        {
            byte[] a = { 1, 0, 1, 1, 1, 1, 0, 1 };
            byte[] b = { 1, 0, 1, 1, 1, 1, 0, 0 };
            int m = StanceDiff.Mask(a, b);
            TestAssert.That(StanceDiff.Count(m) == 1 && StanceDiff.Changed(m, 7) && !StanceDiff.Changed(m, 0), "one radar change");
            TestAssert.That(StanceDiff.Mask(a, a) == 0, "same doctrine has no diff");
            TestAssert.That(StanceDiff.Mask(null, b) == 0 && StanceDiff.Mask(a, new byte[3]) == 0, "bad input reads as no diff");
            TestAssert.That(!StanceDiff.Changed(m, -1) && !StanceDiff.Changed(m, 8), "out-of-range axis is unchanged");
        }
    }

    internal static class StationMathTests
    {
        public static void Run()
        {
            TestAssert.That(StationMath.Phase(1f, false, 4f) == StationPhase.InSlot, "sigma 1 and small error is in slot");
            TestAssert.That(StationMath.Phase(1f, false, 40f) == StationPhase.PreSlot, "sigma 1 but far is pre-slot");
            TestAssert.That(StationMath.Phase(0.6f, false, 300f) == StationPhase.PreSlot, "mid sigma is pre-slot");
            TestAssert.That(StationMath.Phase(0.1f, false, 900f) == StationPhase.Cutoff, "low sigma is cutoff");
            TestAssert.That(StationMath.Phase(1f, true, 4f) == StationPhase.Behind, "falling behind wins");
            TestAssert.That(Math.Abs(StationMath.Closure(320f, 300f, 1f) - 20f) < 0.01f, "closing 20 m/s");
            TestAssert.That(StationMath.Closure(300f, 300f, 0f) == 0f, "dt 0 gives no closure");
            TestAssert.That(Math.Abs(StationMath.EtaSeconds(300f, 20f) - 15f) < 0.01f, "eta = error / closure");
            TestAssert.That(float.IsPositiveInfinity(StationMath.EtaSeconds(300f, -3f)), "opening gap has no eta");
            // QuantiseClamps (review focus 3)
            TestAssert.That(StationMath.QuantiseError(30000f) == 255 && StationMath.QuantiseError(-5f) == 0, "error clamps");
            TestAssert.That(StationMath.Error(StationMath.QuantiseError(304f)) == 300f, "error rounds to 10 m");
            TestAssert.That(StationMath.QuantiseClosure(400f) == 127 && StationMath.QuantiseClosure(-400f) == -127, "closure clamps");
            TestAssert.That(StationMath.QuantiseError(float.NaN) == 0 && StationMath.QuantiseClosure(float.NaN) == 0, "NaN quantises to 0");
            TestAssert.That(StationMath.Word(StationPhase.PreSlot) == "PRE-SLOT" && StationMath.Word(StationPhase.InSlot) == "IN SLOT", "words");
        }
    }

    internal static class LegMathTests
    {
        public static void Run()
        {
            TestAssert.That(Math.Abs(LegMath.BearingDeg(0, 0, 0, 100) - 0f) < 0.01f, "north is 0");
            TestAssert.That(Math.Abs(LegMath.BearingDeg(0, 0, 100, 0) - 90f) < 0.01f, "east is 90");
            TestAssert.That(Math.Abs(LegMath.BearingDeg(0, 0, -100, 0) - 270f) < 0.01f, "west is 270");
            TestAssert.That(Math.Abs(LegMath.Distance(0, 0, 3000, 4000) - 5000f) < 0.01f, "distance");
            TestAssert.That(Math.Abs(LegMath.EtaSeconds(5000f, 250f) - 20f) < 0.01f, "eta");
            TestAssert.That(float.IsPositiveInfinity(LegMath.EtaSeconds(5000f, 0f)), "no speed no eta");
            TestAssert.That(LegMath.Clock(252f) == "4:12" && LegMath.Clock(float.PositiveInfinity) == "—", "clock words");
        }
    }
}
