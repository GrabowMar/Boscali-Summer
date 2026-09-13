using System;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Pure coordinate calculations for map grid labels, minor tick labels, and corner readouts.
    /// Free of engine/Unity runtime dependencies so it can be tested in pure CLI unit test runners.
    /// </summary>
    internal static class GridLabelsMath
    {
        public const float MajorPadding = 10f;
        public const float MinorPadding = 30f;
        public const float FallbackSize = 900f;

        public static float CalculateTopEdge(float viewportHeight, float padding = MajorPadding) =>
            viewportHeight * 0.5f - padding;

        public static float CalculateLeftEdge(float viewportWidth, float padding = MajorPadding) =>
            -viewportWidth * 0.5f + padding;

        public static float CalculateTopEdgeMinor(float viewportHeight, float padding = MinorPadding) =>
            viewportHeight * 0.5f - padding;

        public static float CalculateLeftEdgeMinor(float viewportWidth, float padding = MinorPadding) =>
            -viewportWidth * 0.5f + padding;

        public static void CalculateTooltipPosition(float viewportWidth, float viewportHeight, out float x, out float y)
        {
            x = viewportWidth * 0.5f - 20f;
            y = -viewportHeight * 0.5f + 60f;
        }

        public static void CalculateAircraftCoordPosition(float viewportWidth, float viewportHeight, out float x, out float y)
        {
            x = viewportWidth * 0.5f - 20f;
            y = -viewportHeight * 0.5f + 30f;
        }
    }
}
