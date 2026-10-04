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

    internal static class RibbonTests
    {
        public static void Run()
        {
            var r = new RibbonId[Ribbons.Max];
            TestAssert.That(Ribbons.For(0, 0, 0, r) == 0, "rookie with nothing has no ribbons");
            int n = Ribbons.For(12, 8, 2, r);
            TestAssert.That(n == 5 && r[0] == RibbonId.Kills5 && r[1] == RibbonId.Kills10 && r[2] == RibbonId.Sorties5 && r[3] == RibbonId.Wingman && r[4] == RibbonId.Veteran, "veteran with 12 kills");
            TestAssert.That(Ribbons.For(99, 99, 4, r) == Ribbons.Max, "everything earned fills the rack");
            TestAssert.That(Ribbons.For(5, 0, 0, new RibbonId[1]) == 1, "never writes past the array");
            TestAssert.That(Ribbons.For(5, 5, 1, null) == 0, "null rack writes nothing");
            TestAssert.That(Ribbons.Word(RibbonId.Kills10) == "10 KILLS", "word");
        }
    }

    internal static class AckFeedTests
    {
        public static void Run()
        {
            var f = new AckFeed();
            f.Push(10f, "#3", "CAP 41-07", true, null);
            f.Push(11f, "#4", "ECM", false, "no pod");
            TestAssert.That(f.Count == 2 && f.Newest(0).Who == "#4", "newest first");
            TestAssert.That(f.Newest(1).Chip() == "#3 WILCO · CAP 41-07", "accepted chip words");
            TestAssert.That(f.Newest(0).Chip() == "#4 UNABLE · NO POD", "refused chip uses the reason, upper case");
            var chips = new AckLine[AckFeed.MaxChips];
            TestAssert.That(f.Chips(12f, chips) == 2 && f.Chips(13.5f, chips) == 1 && f.Chips(20f, chips) == 0, "chips last 3 s");
            // RingIsBounded (review focus 4)
            for (int i = 0; i < 50; i++) f.Push(30f, "#2", "ORDER " + i, true, null);
            TestAssert.That(f.Count == AckFeed.Capacity && f.Newest(0).What == "ORDER 49", "ring keeps the newest 16");
            TestAssert.That(f.Newest(AckFeed.Capacity - 1).What == "ORDER 34", "oldest kept is the 16th newest");
            TestAssert.That(f.Chips(30.5f, new AckLine[AckFeed.MaxChips]) == AckFeed.MaxChips, "at most two chips");
            TestAssert.That(f.Newest(99).Who == null && f.Newest(-1).Who == null, "out of range is empty");
        }
    }

    internal static class ChordResolverTests
    {
        public static void Run()
        {
            TestAssert.That(ChordResolver.Resolve(false, false, false, '1').Kind == ChordKind.None, "no wing key no chord");
            Chord s = ChordResolver.Resolve(true, false, false, '4');
            TestAssert.That(s.Kind == ChordKind.Stance && s.Index == 3, "wing+4 is stance slot 4");
            TestAssert.That(ChordResolver.Resolve(true, false, false, '7').Kind == ChordKind.None, "7 is not a stance");
            Chord o = ChordResolver.Resolve(true, false, false, 'z');
            TestAssert.That(o.Kind == ChordKind.Order && o.Index == 8, "wing+Z is the ninth command-card key (CAP)");
            TestAssert.That(ChordResolver.Resolve(true, false, false, '!').Kind == ChordKind.None, "unmapped key is nothing");
            Chord l = ChordResolver.Resolve(true, false, true, '0');
            TestAssert.That(l.Kind == ChordKind.Ladder && l.Index == 0, "digits walk the open ladder");
            // TypingSwallowsChords (review focus 5)
            TestAssert.That(ChordResolver.Resolve(true, true, false, '4').Kind == ChordKind.None, "typing swallows chords");
        }
    }
}
