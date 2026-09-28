using System;
using BoscaliSummer.Features.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class ReliefHolesTests
    {
        private const int Side = 11;
        private const float Sea = -200f;

        public static void Run()
        {
            TestFillsRaisedCutOut();
            TestKeepsLakeAtSeaLevel();
            TestKeepsWaterOpenToEdge();
        }

        private static void Sheet(float ground, out float[] heights, out bool[] land)
        {
            heights = new float[Side * Side];
            land = new bool[heights.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = ground;
                land[i] = true;
            }
        }

        private static void Hole(float[] heights, bool[] land, int x0, int z0, int x1, int z1)
        {
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                heights[z * Side + x] = Sea;
                land[z * Side + x] = false;
            }
        }

        private static void TestFillsRaisedCutOut()
        {
            Sheet(40f, out float[] heights, out bool[] land);
            Hole(heights, land, 3, 3, 7, 6);
            int filled = ReliefHoles.FillRaised(heights, land, Side, Sea, 5f);
            TestAssert.That(filled == 20, "Relief holes: a city cut-out must be filled");
            float centre = heights[5 * Side + 5];
            TestAssert.That(land[5 * Side + 5] && Math.Abs(centre - 40f) < .01f,
                "Relief holes: a filled cut-out must sit level with its rim, not at sea level");
        }

        private static void TestKeepsLakeAtSeaLevel()
        {
            Sheet(Sea + 1f, out float[] heights, out bool[] land);
            Hole(heights, land, 3, 3, 6, 6);
            TestAssert.That(ReliefHoles.FillRaised(heights, land, Side, Sea, 5f) == 0 &&
                !land[4 * Side + 4] && heights[4 * Side + 4] == Sea,
                "Relief holes: a lake whose shore is at sea level must stay water");
        }

        private static void TestKeepsWaterOpenToEdge()
        {
            Sheet(80f, out float[] heights, out bool[] land);
            Hole(heights, land, 0, 4, 6, 6);
            TestAssert.That(ReliefHoles.FillRaised(heights, land, Side, Sea, 5f) == 0 && !land[5 * Side + 3],
                "Relief holes: water reaching the sheet edge is sea and must stay water");
        }
    }
}
