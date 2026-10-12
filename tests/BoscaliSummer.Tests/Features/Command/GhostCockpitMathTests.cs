using System;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class GhostCockpitMathTests
    {
        public static void Run()
        {
            TestLayer();
            TestScales();
            TestSameScreenPoint();
        }

        private static void TestLayer()
        {
            // The game's cockpit overlay camera sees layers 3 (Cockpit) and 14 (CockpitAndExternal).
            TestAssert.Eq(GhostCockpitMath.Layer((1 << 3) | (1 << 14)), 3, "Ghost cockpit: the Cockpit layer is the one drawn on");
            TestAssert.Eq(GhostCockpitMath.Layer(1 << 14), 14, "Ghost cockpit: without it, the lowest layer the camera sees");
            TestAssert.Eq(GhostCockpitMath.Layer((1 << 20) | (1 << 9)), 9, "Ghost cockpit: ... lowest, not first listed");
            TestAssert.Eq(GhostCockpitMath.Layer(0), -1, "Ghost cockpit: a camera that sees nothing has no layer to draw on");
            TestAssert.Eq(GhostCockpitMath.Layer(-1), 3, "Ghost cockpit: a camera that sees everything gets the Cockpit layer");
        }

        private static void TestScales()
        {
            GhostCockpitMath.Scales(63f, 63f, 5f, out float lateral, out float depth);
            TestAssert.That(Math.Abs(depth - 4f / 15000f) < 1e-7f, "Ghost cockpit: the farthest vertex (15 km) lands at 4 m (k = " + depth + ")");
            TestAssert.That(Math.Abs(lateral - depth) < 1e-9f, "Ghost cockpit: equal fields of view squeeze x and y like z");
            TestAssert.That(depth * GhostCockpitMath.FarthestMetres < 5f, "Ghost cockpit: inside the 5 m far plane");

            GhostCockpitMath.Scales(50f, 63f, 5f, out lateral, out depth);
            float ratio = (float)(Math.Tan(25.0 * Math.PI / 180.0) / Math.Tan(31.5 * Math.PI / 180.0));
            TestAssert.That(Math.Abs(lateral - depth * ratio) < 1e-8f && lateral < depth, "Ghost cockpit: a narrower overlay lens scales x and y down by the tangent ratio");

            GhostCockpitMath.Scales(63f, 63f, 2f, out lateral, out depth);
            TestAssert.That(Math.Abs(depth * GhostCockpitMath.FarthestMetres - 1.6f) < 1e-3f, "Ghost cockpit: a nearer far plane takes 80% of it");
            GhostCockpitMath.Scales(63f, 63f, 0f, out lateral, out depth);
            TestAssert.That(Math.Abs(depth * GhostCockpitMath.FarthestMetres - 4f) < 1e-3f, "Ghost cockpit: a far plane that is not a distance falls back to 4 m");

            TestAssert.Near(GhostCockpitMath.LensRatio(0f, 63f), 1f, "Ghost cockpit: a field of view of 0 reads as matching lenses");
            TestAssert.Near(GhostCockpitMath.LensRatio(63f, float.NaN), 1f, "Ghost cockpit: ... and so does one that is not a number");
            TestAssert.Near(GhostCockpitMath.LensRatio(90f, 90f), 1f, "Ghost cockpit: equal lenses have a ratio of 1");
            TestAssert.Near(GhostCockpitMath.LensRatio(90f, 53.130104f), 2f, "Ghost cockpit: tan(45) over tan(26.565) is 2");
        }

        /// <summary>A view-space point squeezed by the scales and projected by the cockpit lens lands where the main lens put it.</summary>
        private static void TestSameScreenPoint()
        {
            float[] mains = { 40f, 63f, 90f }, cockpits = { 50f, 63f, 75f };
            float[,] points = { { 1200f, -300f, -8000f }, { -9000f, 2500f, -12000f }, { 5f, 5f, -400f }, { 0f, 0f, -14000f } };
            foreach (float main in mains)
            foreach (float cockpit in cockpits)
            {
                GhostCockpitMath.Scales(cockpit, main, 5f, out float lateral, out float depth);
                double tanMain = Math.Tan(main * Math.PI / 360.0), tanCockpit = Math.Tan(cockpit * Math.PI / 360.0);
                for (int p = 0; p < points.GetLength(0); p++)
                {
                    double x = points[p, 0], y = points[p, 1], z = -points[p, 2];
                    double mainX = x / (z * tanMain), mainY = y / (z * tanMain);
                    double xs = x * lateral, ys = y * lateral, zs = z * depth;
                    double cockpitX = xs / (zs * tanCockpit), cockpitY = ys / (zs * tanCockpit);
                    TestAssert.That(Math.Abs(mainX - cockpitX) < 1e-4 && Math.Abs(mainY - cockpitY) < 1e-4,
                        "Ghost cockpit: a vertex projects to the same screen point on both cameras (fov " + main + " / " + cockpit + ")");
                    TestAssert.That(zs > 0.0 && zs < 5.0, "Ghost cockpit: ... from inside the cockpit camera's depth range (" + zs + " m)");
                }
            }
        }
    }
}
