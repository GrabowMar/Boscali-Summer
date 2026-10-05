using System;
using System.Collections.Generic;
using BoscaliSummer.Fire;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class ForestIndexTests
    {
        public static void Run()
        {
            TestUnbuiltIndexReturnsFalse();
            TestEmptyIndexReturnsFalse();
            TestSinglePointHitRadius();
            TestCellBoundaryCrossing();
            TestMultiplePointsInSameCell();
            TestNegativeCoordinates();
            TestMultiCellPointDistribution();
        }

        private static void TestUnbuiltIndexReturnsFalse()
        {
            var index = new ForestIndex();
            TestAssert.That(!index.Ready, "Index must not be ready before build");
            TestAssert.That(!index.Contains(0f, 0f), "Unbuilt index must not contain any point");
        }

        private static void TestEmptyIndexReturnsFalse()
        {
            var index = new ForestIndex();
            index.BuildFromPoints(Array.Empty<Vector2>(), 32f);
            TestAssert.That(index.Ready, "Index must be ready after empty build");
            TestAssert.That(index.PositionCount == 0, "PositionCount must be 0 for empty build");
            TestAssert.That(!index.Contains(0f, 0f), "Empty index must not contain points");
            TestAssert.That(!index.Contains(100f, 100f), "Empty index must not contain points at arbitrary coords");
        }

        private static void TestSinglePointHitRadius()
        {
            var index = new ForestIndex();
            index.BuildFromPoints(new[] { new Vector2(100f, 100f) }, 32f);
            TestAssert.That(index.Ready, "Index must be ready");
            TestAssert.That(index.PositionCount == 1, "PositionCount must be 1");

            TestAssert.That(index.Contains(100f, 100f), "Exact tree position must be contained");

            TestAssert.That(index.Contains(110f, 100f), "Position 10m away must be within hit radius");
            TestAssert.That(index.Contains(100f, 117.9f), "Position 17.9m away must be within hit radius");
            TestAssert.That(index.Contains(112.7f, 112.7f), "Position at sqrt(12.7^2 + 12.7^2) ~ 17.96m must be within hit radius");

            TestAssert.That(!index.Contains(118.5f, 100f), "Position 18.5m away must be outside hit radius");
            TestAssert.That(!index.Contains(113f, 113f), "Position at sqrt(13^2 + 13^2) ~ 18.38m must be outside hit radius");
            TestAssert.That(!index.Contains(200f, 200f), "Distant position must not be contained");
        }

        private static void TestCellBoundaryCrossing()
        {
            var index = new ForestIndex();
            index.BuildFromPoints(new[] { new Vector2(31f, 31f) }, 32f);

            TestAssert.That(index.Contains(33f, 31f), "Neighbor cell across X boundary must detect tree");
            TestAssert.That(index.Contains(31f, 33f), "Neighbor cell across Z boundary must detect tree");
            TestAssert.That(index.Contains(33f, 33f), "Diagonal neighbor cell must detect tree within radius");

            TestAssert.That(!index.Contains(55f, 31f), "Neighbor cell beyond 18m must return false");
        }

        private static void TestMultiplePointsInSameCell()
        {
            var index = new ForestIndex();
            index.BuildFromPoints(new[] { new Vector2(2f, 2f), new Vector2(30f, 30f) }, 32f);
            TestAssert.That(index.PositionCount == 2, "PositionCount must be 2");

            TestAssert.That(index.Contains(2f, 2f), "First tree contained");
            TestAssert.That(index.Contains(30f, 30f), "Second tree contained");
            TestAssert.That(index.Contains(10f, 2f), "Near first tree contained");
            TestAssert.That(index.Contains(25f, 30f), "Near second tree contained");

            TestAssert.That(!index.Contains(16f, 16f), "Midpoint between trees separated by >36m must return false");
        }

        private static void TestNegativeCoordinates()
        {
            var index = new ForestIndex();
            index.BuildFromPoints(new[] { new Vector2(-5f, -5f) }, 32f);

            TestAssert.That(index.Contains(-5f, -5f), "Negative tree coordinate must be contained");
            TestAssert.That(index.Contains(5f, 5f), "Query across negative/positive boundary must detect tree");
            TestAssert.That(index.Contains(-15f, -5f), "Point at (-15, -5) is 10m away, must be contained");
            TestAssert.That(!index.Contains(-25f, -5f), "Point at (-25, -5) is 20m away, must not be contained");
        }

        private static void TestMultiCellPointDistribution()
        {
            var index = new ForestIndex();
            var points = new List<Vector2>();

            for (int i = 0; i < 100; i++)
            {
                float x = (i * 73 % 500) - 250f;
                float z = (i * 97 % 500) - 250f;
                points.Add(new Vector2(x, z));
            }

            index.BuildFromPoints(points, 32f);
            TestAssert.That(index.PositionCount == 100, "PositionCount must match input list");

            for (int i = 0; i < points.Count; i++)
            {
                TestAssert.That(index.Contains(points[i]), $"Tree at index {i} ({points[i].x}, {points[i].y}) must be contained");
            }

            TestAssert.That(!index.Contains(5000f, 5000f), "Point at (5000, 5000) must return false");
            TestAssert.That(!index.Contains(-5000f, -5000f), "Point at (-5000, -5000) must return false");
        }
    }
}
