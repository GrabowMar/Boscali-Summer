using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MapScaleTests
    {
        public static void Run()
        {
            TestAssert.That(MapScale.Mean(10f, 14f, out float mean) && mean == 12f, "the scale is the mean of the east and north runs");
            TestAssert.That(!MapScale.Mean(0f, 14f, out mean) && mean == 0f, "a zero run is not a scale");
            TestAssert.That(!MapScale.Mean(10f, -1f, out _), "a negative run is not a scale");
            TestAssert.That(!MapScale.Mean(float.NaN, 14f, out _) && !MapScale.Mean(10f, float.NaN, out _), "NaN is not a scale");
            TestAssert.That(!MapScale.Mean(float.PositiveInfinity, 14f, out _), "infinity is not a scale");
        }
    }
}
