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
}
