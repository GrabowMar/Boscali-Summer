using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    internal static class AceRadialMathTests
    {
        public static void Run()
        {
            TestRootLayout();
            TestBranchLayout();
            TestSelectorRotation();
            TestClosestNodeSelection();
            TestExpansionScale();
        }

        private static void TestRootLayout()
        {
            var actions = new List<AceRadialActionDescriptor>
            {
                new AceRadialActionDescriptor("act1", "Action 1"),
                new AceRadialActionDescriptor("act2", "Action 2"),
                new AceRadialActionDescriptor("act3", "Action 3"),
                new AceRadialActionDescriptor("act4", "Action 4")
            };

            AceVec2 center = new AceVec2(500f, 500f);
            const float radius = 100f;
            List<AceRadialNodeLayout> roots = AceRadialMath.CalculateRootLayout(actions, center, radius, startAngleDeg: 90f);

            TestAssert.That(roots.Count == 4, "Root layout must generate exactly 4 nodes for 4 actions");
            TestAssert.That(Math.Abs(roots[0].AngleDeg - 90f) < 0.01f, "First root node must start at 90 deg");
            TestAssert.That(Math.Abs(roots[1].AngleDeg - 0f) < 0.01f, "Second root node must be at 0 deg (90 - 90)");
            TestAssert.That(Math.Abs(roots[2].AngleDeg - (-90f)) < 0.01f, "Third root node must be at -90 deg");
            TestAssert.That(Math.Abs(roots[3].AngleDeg - (-180f)) < 0.01f, "Fourth root node must be at -180 deg");

            // Verify position Euclidean distance from center is exactly radius
            for (int i = 0; i < roots.Count; i++)
            {
                float dist = AceVec2.Distance(center, roots[i].Position);
                TestAssert.That(Math.Abs(dist - radius) < 0.1f, $"Root node {i} distance from center must match radius");
            }
        }

        private static void TestBranchLayout()
        {
            var children = new List<AceRadialActionDescriptor>
            {
                new AceRadialActionDescriptor("child1", "Child 1"),
                new AceRadialActionDescriptor("child2", "Child 2"),
                new AceRadialActionDescriptor("child3", "Child 3")
            };

            AceVec2 parentPos = new AceVec2(600f, 500f);
            const float parentAngle = 0f; // pointing directly right
            const float branchRadius = 80f;

            List<AceRadialNodeLayout> branch = AceRadialMath.CalculateBranchLayout(
                children, parentPos, parentAngle, branchRadius, maxAngleSpanDeg: 100f, expansionProgress: 1f);

            TestAssert.That(branch.Count == 3, "Branch layout must generate 3 child nodes");

            // Middle child should align with parent angle (0 deg)
            TestAssert.That(Math.Abs(branch[1].AngleDeg - parentAngle) < 0.1f,
                "Center child must align with parent radial angle");

            // Symmetric span: child 0 and child 2 must be symmetric around parent angle
            float angleDelta0 = branch[1].AngleDeg - branch[0].AngleDeg;
            float angleDelta2 = branch[2].AngleDeg - branch[1].AngleDeg;
            TestAssert.That(Math.Abs(angleDelta0 - angleDelta2) < 0.01f,
                "Child branch arc intervals must be evenly distributed");
        }

        private static void TestSelectorRotation()
        {
            // At 270 deg/s, 1 second should advance by 270 deg
            float rotated = AceRadialMath.UpdateSelectorRotation(0f, 1f);
            TestAssert.That(Math.Abs(rotated - 270f) < 0.01f, "Selector rotation must advance 270 deg in 1s");

            // In 2 seconds, 540 deg mod 360 = 180 deg
            float wrapped = AceRadialMath.UpdateSelectorRotation(0f, 2f);
            TestAssert.That(Math.Abs(wrapped - 180f) < 0.01f, "Selector rotation must wrap modulo 360 deg");
        }

        private static void TestClosestNodeSelection()
        {
            var nodes = new List<AceRadialNodeLayout>
            {
                new AceRadialNodeLayout("a", "A", new AceVec2(100f, 100f), 0f, 0, false, true),
                new AceRadialNodeLayout("b", "B", new AceVec2(200f, 100f), 0f, 0, false, true),
                new AceRadialNodeLayout("c", "C", new AceVec2(300f, 100f), 0f, 0, false, true)
            };

            // Cursor near node B (205, 102)
            int indexB = AceRadialMath.FindClosestNodeIndex(new AceVec2(205f, 102f), nodes, thresholdDistance: 50f);
            TestAssert.That(indexB == 1, "Cursor closest to node B must return index 1");

            // Cursor far away (999, 999) exceeds threshold
            int indexNone = AceRadialMath.FindClosestNodeIndex(new AceVec2(999f, 999f), nodes, thresholdDistance: 50f);
            TestAssert.That(indexNone == -1, "Cursor far beyond threshold must return -1");
        }

        private static void TestExpansionScale()
        {
            // Verify expansion scale formula (0.3 + 0.7 * progress)
            TestAssert.That(Math.Abs(AceRadialMath.CalculateExpansionScale(0f) - 0.3f) < 0.01f,
                "At progress 0, expansion scale must be 0.3");
            TestAssert.That(Math.Abs(AceRadialMath.CalculateExpansionScale(1f) - 1.0f) < 0.01f,
                "At progress 1, expansion scale must be 1.0");
            TestAssert.That(Math.Abs(AceRadialMath.CalculateExpansionScale(0.5f) - 0.65f) < 0.01f,
                "At progress 0.5, expansion scale must be 0.65");
        }
    }
}
