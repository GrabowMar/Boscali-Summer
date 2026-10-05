using System.Collections.Generic;
using BoscaliSummer.Modules.Wing.Domain.Pure;

namespace WingPure.Tests
{
    internal static class StationBoardTests
    {
        public static void Run()
        {
            byte[] phase = { 2, 2, 1, 3 };
            TestAssert.That(StationBoard.InSlot(phase, 4) == 2 && StationBoard.InSlot(phase, 1) == 1, "in-slot counts phase 2");
            TestAssert.That(StationBoard.InSlot(phase, 0) == 0, "no members, none in slot");
            byte[] err = { 1, 3, 4 };
            float rms = StationBoard.Rms(err, 3);
            TestAssert.That(System.Math.Abs(rms - 29.44f) < 0.1f, "rms of 10, 30, 40 m");
            TestAssert.That(StationBoard.Rms(err, 0) < 0f, "no rms for nobody");
            TestAssert.That(StationBoard.Percent(2, 3) == 67 && StationBoard.Percent(0, 0) == 0, "percent rounds");
            float[] x = { 0f, 30f, 0f }, z = { 0f, 40f, 500f };
            TestAssert.That(System.Math.Abs(StationBoard.MinSeparation(x, z, 3) - 50f) < 0.01f, "closest pair is 50 m");
            TestAssert.That(StationBoard.MinSeparation(x, z, 1) < 0f, "one aircraft has no separation");
            TestAssert.That(StationBoard.Signed(4) == "+4" && StationBoard.Signed(-1) == "-1" && StationBoard.Signed(0) == "0", "signed closure");
            TestAssert.That(StationBoard.ErrorText(310f) == "310 m" && StationBoard.ErrorText(1200f) == "1.2 km", "error words");
        }
    }

    internal static class StanceMatchTests
    {
        private static Stance S(string id, params byte[] a) => new Stance { Id = id, Name = id.ToUpperInvariant(), Axes = a, BuiltIn = true };

        private static StanceBook Book() => StanceBook.FromJson(null, new[]
        {
            S("a", 0, 0, 0, 0, 0, 0, 0, 0), S("b", 0, 0, 0, 0, 1, 1, 0, 0), S("c", 0, 0, 0, 0, 3, 1, 0, 1),
        }, new List<string>());

        public static void Run()
        {
            StanceBook b = Book();
            int slot = StanceMatch.Closest(b, new byte[] { 0, 0, 0, 0, 1, 1, 0, 0 }, out int mask);
            TestAssert.That(slot == 1 && mask == 0, "an exact match is its slot with no difference");
            slot = StanceMatch.Closest(b, new byte[] { 0, 0, 0, 0, 1, 1, 0, 2 }, out mask);
            TestAssert.That(slot == 1 && StanceDiff.Count(mask) == 1 && StanceDiff.Changed(mask, 7), "one radar change from the closest slot");
            TestAssert.That(StanceMatch.Closest(new StanceBook(), new byte[8], out mask) == -1, "an empty book matches nothing");
            TestAssert.That(StanceMatch.NextFreeSlot(b) == 3, "slots 1-3 are taken");
            Stance s = StanceMatch.SaveAs(b, new byte[] { 0, 0, 0, 0, 2, 0, 1, 1 });
            TestAssert.That(s != null && s.Name == "CUSTOM 1" && !s.BuiltIn && b.Slot(3) == s, "saved into the next free slot");
            Stance t = StanceMatch.SaveAs(b, new byte[] { 0, 0, 0, 0, 2, 0, 1, 0 });
            TestAssert.That(t != null && t.Name == "CUSTOM 2" && b.Slot(4) == t, "the next save gets the next number and slot");
            StanceMatch.SaveAs(b, new byte[8]);
            TestAssert.That(StanceMatch.NextFreeSlot(b) == -1 && StanceMatch.SaveAs(b, new byte[8]) == null, "six slots then no more");
            TestAssert.That(StanceMatch.SaveAs(Book(), new byte[3]) == null, "bad axes are refused");
        }
    }
}
