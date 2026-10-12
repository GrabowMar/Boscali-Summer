using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class ReliefClustersTests
    {
        public static void Run()
        {
            TheCellSnapsToAPowerOfTwo();
            OneKnotIsOneStack();
            SidesAndDistantKnotsStayApart();
            AKnotAcrossACellBorderMerges();
            MergesDoNotChain();
        }

        private static void TheCellSnapsToAPowerOfTwo()
        {
            float a = ReliefClusters.CellMetres(10f, 40f), b = ReliefClusters.CellMetres(13f, 40f);
            TestAssert.That(a == 512f && b == 512f, "a slow zoom keeps the grid: 400 m and 520 m both snap to 512, got " + a + " and " + b);
            TestAssert.That(ReliefClusters.CellMetres(5f, 40f) == 256f, "twice the zoom halves the cell");
            TestAssert.That(ReliefClusters.CellMetres(0f, 40f) == 1f && ReliefClusters.CellMetres(float.NaN, 40f) == 1f,
                "a view with no scale gives a harmless cell, not zero or NaN");
        }

        private static void OneKnotIsOneStack()
        {
            var plan = new ReliefClusters();
            plan.Begin(512f);
            int a = plan.Add(100f, 100f, 1), b = plan.Add(140f, 90f, 1), c = plan.Add(120f, 160f, 1);
            plan.Solve();
            TestAssert.That(plan.StackOf(a) == plan.StackOf(b) && plan.StackOf(b) == plan.StackOf(c), "three contacts in one cell share a stack");
            TestAssert.That(plan.Size(plan.StackOf(a)) == 3, "and the stack counts all three");
            TestAssert.That(plan.StackOf(-1) == -1 && plan.Size(-1) == 0, "a refused handle belongs to no stack");
        }

        private static void SidesAndDistantKnotsStayApart()
        {
            var plan = new ReliefClusters();
            plan.Begin(512f);
            int blue = plan.Add(100f, 100f, 1), red = plan.Add(110f, 100f, 2), far = plan.Add(1400f, 100f, 1);
            plan.Solve();
            TestAssert.That(plan.StackOf(blue) != plan.StackOf(red), "two sides on the same spot never share a stack");
            TestAssert.That(plan.StackOf(blue) != plan.StackOf(far), "a knot two cells away is its own stack");
        }

        private static void AKnotAcrossACellBorderMerges()
        {
            var plan = new ReliefClusters();
            plan.Begin(512f);
            // The border is at x = 512: four on the left of it, two just over it.
            int left = -1, right = -1;
            for (int i = 0; i < 4; i++) left = plan.Add(480f + i, 200f, 1);
            for (int i = 0; i < 2; i++) right = plan.Add(530f + i, 200f, 1);
            plan.Solve();
            TestAssert.That(plan.StackOf(left) == plan.StackOf(right), "a knot split by a grid line is still one stack");
            TestAssert.That(plan.Size(plan.StackOf(left)) == 6, "and its count is the whole knot");
        }

        private static void MergesDoNotChain()
        {
            var plan = new ReliefClusters();
            plan.Begin(512f);
            // Three cells in a row, each within reach of the next (200 m, then 330 m, reach 358 m). The biggest takes the middle;
            // the far end must not ride in through it, or a line of units collapses into one stack a kilometre long.
            int west = -1, mid = -1, east = -1;
            for (int i = 0; i < 5; i++) west = plan.Add(500f, 100f, 1);
            for (int i = 0; i < 3; i++) mid = plan.Add(700f, 100f, 1);
            for (int i = 0; i < 2; i++) east = plan.Add(1030f, 100f, 1);
            plan.Solve();
            TestAssert.That(plan.StackOf(west) == plan.StackOf(mid) && plan.Size(plan.StackOf(west)) == 8, "the near neighbour joins the bigger knot");
            TestAssert.That(plan.StackOf(east) != plan.StackOf(mid) && plan.Size(plan.StackOf(east)) == 2, "the far end of the line stays its own stack");
        }
    }
}
