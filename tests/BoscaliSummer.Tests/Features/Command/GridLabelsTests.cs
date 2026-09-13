using System;
using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class GridLabelsTests
    {
        public static void Run()
        {
            TestVanillaFallbackEquivalence();
            TestExpandedViewportEdges();
            TestCornerReadoutPositions();
            TestCustomPaddingAndExtents();
        }

        private static void TestVanillaFallbackEquivalence()
        {
            // Vanilla Nuclear Option 900x900 viewport hardcoded values:
            // Top numbers: +440f
            // Left letters: -440f
            // Minor top ticks: +420f
            // Minor left ticks: -420f
            float top = GridLabelsMath.CalculateTopEdge(900f);
            float left = GridLabelsMath.CalculateLeftEdge(900f);
            float minorTop = GridLabelsMath.CalculateTopEdgeMinor(900f);
            float minorLeft = GridLabelsMath.CalculateLeftEdgeMinor(900f);

            TestAssert.That(Math.Abs(top - 440f) < 0.001f, "Vanilla fallback top edge must equal 440f");
            TestAssert.That(Math.Abs(left - (-440f)) < 0.001f, "Vanilla fallback left edge must equal -440f");
            TestAssert.That(Math.Abs(minorTop - 420f) < 0.001f, "Vanilla fallback minor top edge must equal 420f");
            TestAssert.That(Math.Abs(minorLeft - (-420f)) < 0.001f, "Vanilla fallback minor left edge must equal -420f");
        }

        private static void TestExpandedViewportEdges()
        {
            // Expanded center column viewport (e.g. 1320x918):
            // Width = 1320 -> half = 660. Left edge with 10px margin = -660 + 10 = -650f.
            // Height = 918 -> half = 459. Top edge with 10px margin = 459 - 10 = 449f.
            // Minor left with 30px margin = -660 + 30 = -630f.
            // Minor top with 30px margin = 459 - 30 = 429f.
            float top = GridLabelsMath.CalculateTopEdge(918f);
            float left = GridLabelsMath.CalculateLeftEdge(1320f);
            float minorTop = GridLabelsMath.CalculateTopEdgeMinor(918f);
            float minorLeft = GridLabelsMath.CalculateLeftEdgeMinor(1320f);

            TestAssert.That(Math.Abs(top - 449f) < 0.001f, "Expanded top edge must be 449f");
            TestAssert.That(Math.Abs(left - (-650f)) < 0.001f, "Expanded left edge must be -650f");
            TestAssert.That(Math.Abs(minorTop - 429f) < 0.001f, "Expanded minor top edge must be 429f");
            TestAssert.That(Math.Abs(minorLeft - (-630f)) < 0.001f, "Expanded minor left edge must be -630f");

            // Left edge must move further left as width increases (not stay stuck at -440f!)
            TestAssert.That(left < -440f, "Expanded left edge must extend past vanilla -440f boundary");
        }

        private static void TestCornerReadoutPositions()
        {
            // Vanilla 900x900 viewport:
            // ToolTip: (450 - 20, -450 + 60) = (430, -390)
            // Aircraft: (450 - 20, -450 + 30) = (430, -420)
            GridLabelsMath.CalculateTooltipPosition(900f, 900f, out float vx, out float vy);
            TestAssert.That(Math.Abs(vx - 430f) < 0.001f && Math.Abs(vy - (-390f)) < 0.001f,
                "Vanilla tooltip position must be (430, -390)");

            GridLabelsMath.CalculateAircraftCoordPosition(900f, 900f, out float ax, out float ay);
            TestAssert.That(Math.Abs(ax - 430f) < 0.001f && Math.Abs(ay - (-420f)) < 0.001f,
                "Vanilla aircraft coord position must be (430, -420)");

            // Expanded 1320x918 viewport:
            // ToolTip: (660 - 20, -459 + 60) = (640, -399)
            // Aircraft: (660 - 20, -459 + 30) = (640, -429)
            GridLabelsMath.CalculateTooltipPosition(1320f, 918f, out float ex, out float ey);
            TestAssert.That(Math.Abs(ex - 640f) < 0.001f && Math.Abs(ey - (-399f)) < 0.001f,
                "Expanded tooltip position must pin to bottom-right corner at (640, -399)");

            GridLabelsMath.CalculateAircraftCoordPosition(1320f, 918f, out float eax, out float eay);
            TestAssert.That(Math.Abs(eax - 640f) < 0.001f && Math.Abs(eay - (-429f)) < 0.001f,
                "Expanded aircraft coord position must pin to bottom-right corner at (640, -429)");
        }

        private static void TestCustomPaddingAndExtents()
        {
            // Parametric padding checks
            float top = GridLabelsMath.CalculateTopEdge(1000f, 15f);
            float left = GridLabelsMath.CalculateLeftEdge(1000f, 15f);
            TestAssert.That(Math.Abs(top - 485f) < 0.001f, "Top edge with 15px padding on 1000px height is 485f");
            TestAssert.That(Math.Abs(left - (-485f)) < 0.001f, "Left edge with 15px padding on 1000px width is -485f");

            // Zero viewport
            TestAssert.That(GridLabelsMath.CalculateTopEdge(0f, 10f) == -10f, "Zero height edge is -padding");
            TestAssert.That(GridLabelsMath.CalculateLeftEdge(0f, 10f) == 10f, "Zero width edge is padding");
        }
    }
}
