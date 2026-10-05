using BoscaliSummer.Modules.Wing.Domain.Pure;

namespace WingPure.Tests
{
    internal static class SheetMathTests
    {
        public static void Run()
        {
            // The mockup's eight stations: three wing pairs and two centre stations.
            int[] mock = { 2, 2, 2, 1, 1 };
            var spots = new StationMapMath.Spot[16];
            int n = StationMapMath.Spots(mock, 62f, 80f, 16f, spots);
            TestAssert.That(n == 8, "three pairs and two centre stations make eight boxes");
            for (int i = 0; i < n; i++)
            {
                TestAssert.That(System.Math.Abs(spots[i].X) <= 62f && System.Math.Abs(spots[i].Y) <= 80f, "box inside the map");
                for (int k = i + 1; k < n; k++)
                {
                    float dx = spots[i].X - spots[k].X, dy = spots[i].Y - spots[k].Y;
                    TestAssert.That(dx * dx + dy * dy >= 16f * 16f - 0.01f, "boxes " + i + " and " + k + " do not overlap");
                }
            }
            TestAssert.That(spots[0].Station == 0 && spots[1].Station == 0 && spots[0].X == -spots[1].X && spots[0].Y == spots[1].Y,
                "a pair is mirrored");
            TestAssert.That(spots[6].X == 0f && spots[7].X == 0f && spots[6].Y < spots[7].Y, "centre stations run nose to tail");

            TestAssert.That(StationMapMath.Spots(new int[0], 62f, 80f, 16f, spots) == 0, "no stations, no boxes");
            TestAssert.That(StationMapMath.Spots(new[] { 1 }, 62f, 80f, 16f, spots) == 1 && spots[0].X == 0f, "one station sits on the centreline");
            TestAssert.That(StationMapMath.Spots(new[] { 2 }, 62f, 80f, 16f, spots) == 2, "one pair is two boxes");
            var small = new StationMapMath.Spot[3];
            TestAssert.That(StationMapMath.Spots(new[] { 2, 2, 2 }, 62f, 80f, 16f, small) <= 3, "never writes past the buffer");

            // Twelve centre stations still stay inside the map.
            var many = new int[12];
            for (int i = 0; i < many.Length; i++) many[i] = 1;
            n = StationMapMath.Spots(many, 62f, 80f, 12f, spots);
            for (int i = 0; i < n; i++) TestAssert.That(System.Math.Abs(spots[i].Y) <= 80f, "tail box inside the map");

            // Chips wrap.
            var xs = new float[5];
            var lines = new int[5];
            int used = ChipFlow.Place(new[] { 50f, 50f, 50f, 50f, 50f }, 5, 10f, 170f, 2f, xs, lines);
            TestAssert.That(used == 2, "five 50 px chips wrap onto two lines of 170 px");
            TestAssert.That(lines[0] == 0 && lines[2] == 0 && lines[3] == 1 && xs[3] == 10f, "the fourth chip starts line two");
            TestAssert.That(ChipFlow.Place(new[] { 400f }, 1, 0f, 100f, 2f, xs, lines) == 1, "a too-wide chip sits alone on line one");
            TestAssert.That(ChipFlow.Place(new float[0], 0, 0f, 100f, 2f, xs, lines) == 1, "no chips is one (empty) line");
        }
    }
}
