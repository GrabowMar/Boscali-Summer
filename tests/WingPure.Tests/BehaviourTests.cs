using System.Collections.Generic;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class StanceWordsTests
    {
        public static void Run()
        {
            // SILENT HUNT from the mockup: AIR, 12 km, silent, guard wing, break, spread, 2:1, refit at winchester.
            byte[] hunt = { 2, 0, 2, 1, 1, 1, 0, 1 };
            string says = StanceWords.Says(hunt, 2, 2, 0);
            TestAssert.That(says.StartsWith("Hunt enemy aircraft out to 12 km, radar silent until we engage."), "targets, range and radar lead: " + says);
            TestAssert.That(says.Contains("Break from missiles.") && says.Contains("Fall back at 2 : 1.") && says.Contains("Refit at winchester.") && says.Contains("RTB at bingo."), "defend and after: " + says);
            TestAssert.That(StanceWords.Says(new byte[] { 0, 0, 1, 0, 0, 0, 0, 0 }, -1, -1, -1).StartsWith("Hold fire unless ordered, radar on."), "hold fire");
            TestAssert.That(!StanceWords.Says(hunt, -1, -1, -1).Contains("Fall back"), "-1 leaves the follow-ons unsaid");
            TestAssert.That(StanceWords.Says(hunt, 0, -1, -1).Contains("Never fall back."), "never falls back");
            TestAssert.That(StanceWords.Says(new byte[] { 1, 1, 0, 0, 4, 0, 3, 2 }, -1, -1, -1).Contains("Cover the protected aircraft"), "cover");
            TestAssert.That(StanceWords.Says(null, 0, 0, 0) == "" && StanceWords.Says(new byte[3], 0, 0, 0) == "", "bad axes say nothing");
            TestAssert.That(StanceWords.Says(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }, 9, 9, 9).Length > 0, "out-of-range axes do not throw");

            TestAssert.That(StanceWords.Brief(hunt) == "AIR · 12 KM · SILENT", "brief");
            TestAssert.That(StanceWords.AxisWord(StanceWords.Weapons, 3) == "NO A-G" && StanceWords.AxisWord(4, 99) == "?", "axis words");
            TestAssert.That(StanceWords.AxisName(0) == "MSL GUARD" && StanceWords.AxisName(8) == "?", "axis names");

            var all = new List<Stance>
            {
                new Stance { Id = "a", Axes = new byte[] { 1, 0, 1, 1, 0, 0, 0, 0 } },
                new Stance { Id = "b", Axes = new byte[] { 1, 0, 1, 1, 1, 1, 0, 1 } },
            };
            byte[] live = { 1, 0, 1, 1, 1, 1, 0, 0 };
            Stance c = StanceWords.Closest(all, live, out int mask);
            TestAssert.That(c.Id == "b" && StanceDiff.Count(mask) == 1 && StanceDiff.Changed(mask, 7), "closest stance and its changed axis");
            TestAssert.That(StanceWords.ChangedWords(mask, live) == "RADAR ON", "changed words use the live value");
            TestAssert.That(StanceWords.ChangedWords(0, live) == "", "no change says nothing");
            TestAssert.That(StanceWords.Closest(new List<Stance>(), live, out int none) == null && none == 0, "no stances, no closest");
            Stance exact = StanceWords.Closest(all, all[0].Axes, out int zero);
            TestAssert.That(exact.Id == "a" && zero == 0, "an exact match has no changed axes");
        }
    }

    internal static class SortieWordsTests
    {
        public static void Run()
        {
            TestAssert.That(SortieWords.Clock(54f) == "0:54" && SortieWords.Clock(125f) == "2:05" && SortieWords.Clock(-3f) == "0:00" && SortieWords.Clock(float.NaN) == "-", "clock");
            TestAssert.That(SortieWords.Diff(108f, 162f) == "LATE 0:54", "late");
            TestAssert.That(SortieWords.Diff(162f, 150f) == "EARLY 0:12", "early");
            TestAssert.That(SortieWords.Diff(100f, 103f) == "ON TIME", "within five seconds");
            TestAssert.That(SortieWords.Diff(float.NaN, 3f) == "" && SortieWords.Diff(3f, float.NaN) == "", "unknown timing says nothing");
            TestAssert.That(SortieWords.Tone("A lost a wingman") == 2 && SortieWords.Tone("bingo fuel") == 1 && SortieWords.Tone("airborne") == 0 && SortieWords.Tone(null) == 0, "tone");
            TestAssert.That(SortieWords.Stamp(160f, true, 100f) == "T+1:00" && SortieWords.Stamp(60f, true, 100f) == "1:00" && SortieWords.Stamp(160f, false, 100f) == "2:40", "stamp");
        }
    }
}
