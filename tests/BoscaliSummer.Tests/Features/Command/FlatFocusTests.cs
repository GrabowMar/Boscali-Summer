using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The maths of centring the flat sheet on a world point (annex 05 P6). The offsets are those of Wing's MapFit.Offset: world * displayFactor - stationary.</summary>
    internal static class FlatFocusTests
    {
        public static void Run()
        {
            TheOffsetIsWingsFormula();
            AnOffsetPutsThePointAtTheCentre();
            TheZoomRaisesToAreaLevelAndStaysInTheGamesRange();
            ZoomingOutLeavesTheZoomAlone();
        }

        private static void TheOffsetIsWingsFormula()
        {
            // Three literal inputs, worked by hand with MapFit.Offset(x, z, displayFactor, stationaryX, stationaryY) = (x * f - sx, z * f - sy).
            FlatFocus.Offset(1000f, 2000f, 0.5f, 100f, 200f, out float x, out float z);
            TestAssert.That(x == 400f && z == 800f, "(1000, 2000) at 0.5 with stationary (100, 200) is (400, 800), got (" + x + ", " + z + ")");
            FlatFocus.Offset(-3500f, 7250f, 0.01f, 12f, -8f, out x, out z);
            TestAssert.That(Near(x, -47f) && Near(z, 80.5f), "(-3500, 7250) at 0.01 with stationary (12, -8) is (-47, 80.5), got (" + x + ", " + z + ")");
            FlatFocus.Offset(0f, 0f, 2f, 0f, 0f, out x, out z);
            TestAssert.That(x == 0f && z == 0f, "the origin with no stationary offset stays at zero");
        }

        private static void AnOffsetPutsThePointAtTheCentre()
        {
            // The game draws the sheet at -(stationary + offset) in map units, so the point's map position plus that origin is zero: the viewport centre.
            float factor = 0.004f, sx = 31f, sz = -17f;
            FlatFocus.Offset(12345f, -6789f, factor, sx, sz, out float ox, out float oz);
            float sheetX = -(sx + ox), sheetZ = -(sz + oz);
            TestAssert.That(Near(12345f * factor + sheetX, 0f) && Near(-6789f * factor + sheetZ, 0f), "the point lands on the centre of the viewport");
        }

        private static void TheZoomRaisesToAreaLevelAndStaysInTheGamesRange()
        {
            TestAssert.That(FlatFocus.Zoom(1f, true) == 3f, "from the whole-map zoom a locate goes to 3");
            TestAssert.That(FlatFocus.Zoom(0.2f, true) == 3f && FlatFocus.Zoom(-4f, true) == 3f, "a zoom under the floor goes to 3");
            TestAssert.That(FlatFocus.Zoom(3f, true) == 3f && FlatFocus.Zoom(12f, true) == 12f, "a zoom already at area level or closer stays");
            TestAssert.That(FlatFocus.Zoom(40f, true) == 40f && FlatFocus.Zoom(55f, true) == 40f, "the game's top zoom of 40 caps it");
            TestAssert.That(FlatFocus.Zoom(float.PositiveInfinity, true) == 40f && FlatFocus.Zoom(float.NegativeInfinity, true) == 3f, "infinities land inside the range");
            TestAssert.That(FlatFocus.Zoom(float.NaN, true) == 3f, "an unknown zoom is raised to 3, not propagated");
        }

        private static void ZoomingOutLeavesTheZoomAlone()
        {
            TestAssert.That(FlatFocus.Zoom(1f, false) == 1f && FlatFocus.Zoom(7.5f, false) == 7.5f && FlatFocus.Zoom(40f, false) == 40f, "without zoomIn the current zoom comes back");
        }

        private static bool Near(float a, float b) => System.Math.Abs(a - b) < 1e-3f;
    }
}
