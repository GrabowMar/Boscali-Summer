using System;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Surviving orbit geometry and deterministic SAR coverage from the retired platform suite.</summary>
    internal static class SpaceTests
    {
        public static void Run()
        {
            TestOrbitMath();
            TestSarProjection();
            TestSarFormation();
        }

        private static bool Near(double value, double expected, double tolerance) =>
            Math.Abs(value - expected) <= tolerance;

        private static void TestOrbitMath()
        {
            TestAssert.That(Near(OrbitMath.Velocity(525000.0), 7600.0, 15.0), "525 km orbital velocity must be ~7.6 km/s");
            TestAssert.That(Near(OrbitMath.Elevation(525000.0, 0.0), Math.PI * 0.5, 1e-9), "overhead must read 90°");
            TestAssert.That(Near(OrbitMath.OffNadir(525000.0, Math.PI * 0.5), 0.0, 1e-9), "overhead must be nadir");
            TestAssert.That(Near(OrbitMath.SlantRange(525000.0, 0.0), 525000.0, 1e-6), "overhead slant must equal altitude");

            double central = OrbitMath.CentralAngleForOffNadir(525000.0, 30.0 * OrbitMath.Deg);
            double elevation = OrbitMath.Elevation(525000.0, central);
            TestAssert.That(Near(OrbitMath.OffNadir(525000.0, elevation), 30.0 * OrbitMath.Deg, 1e-6),
                "off-nadir and central angle must invert each other");
            TestAssert.That(OrbitMath.Incidence(elevation) > 30.0 * OrbitMath.Deg,
                "Earth curvature makes incidence exceed off-nadir");
        }

        private static void TestSarProjection()
        {
            var geometry = new SarGeometry(30.0 * OrbitMath.Deg, 1.0, 0.0, 700000.0, 7500.0);
            var former = new SarImageFormer(200, 200, 1000.0, geometry, 7);
            TestAssert.That(former.Project(0.0, 0.0, 0.0, 0.0, out int groundColumn, out int groundRow),
                "the scene centre must project into the image");
            TestAssert.That(former.Project(0.0, 100.0, 0.0, 0.0, out int tallColumn, out int tallRow), "a tall point must project");
            double layover = (groundColumn - tallColumn) * former.PixelRange;
            TestAssert.That(Near(layover, 100.0 / Math.Tan(30.0 * OrbitMath.Deg), former.PixelRange * 1.01),
                "height must lay over toward the sensor by h·cot(incidence)");
            TestAssert.That(tallRow == groundRow, "layover must not move a point in azimuth");

            TestAssert.That(former.Project(0.0, 0.0, 0.0, 5.0, out _, out int movingRow), "a slow mover must stay in the scene");
            double shift = Math.Abs(movingRow - groundRow) * former.PixelAzimuth;
            TestAssert.That(Near(shift, 5.0 * 700000.0 / 7500.0, former.PixelAzimuth * 1.01),
                "radial velocity must displace a target in azimuth by v·R/V");

            TestAssert.That(!former.Project(5000.0, 0.0, 0.0, 0.0, out _, out _), "points outside the scene must be dropped");
            former.Add(5000.0, 0.0, 0.0, 1.0, 0.0);
            TestAssert.That(former.Dropped == 1 && former.Accepted == 0, "dropped samples must be counted, not drawn");

            former.PlanRay(0, 300, out double x0, out double z0);
            TestAssert.That(x0 > 900.0 && Math.Abs(z0) <= 1000.0, "the first ray must start at near range");
            TestAssert.That(former.RayCount(300) == 300 * 200, "ray count must be range samples × azimuth lines");
        }

        private static void TestSarFormation()
        {
            var geometry = new SarGeometry(35.0 * OrbitMath.Deg, 0.0, 1.0, 650000.0, 7500.0);
            byte[] Build(int seed)
            {
                var former = new SarImageFormer(64, 64, 640.0, geometry, seed);
                for (int i = 0; i < former.RayCount(96); i++)
                {
                    former.PlanRay(i, 96, out double x, out double z);
                    bool water = z < 0.0;
                    former.Add(x, 0.0, z, water ? 0.004 : 0.12, 0.0);
                }
                former.Add(0.0, 0.0, 300.0, 40.0, 0.0);
                return former.Form(2, 0.0005);
            }

            byte[] a = Build(21);
            byte[] b = Build(21);
            byte[] c = Build(22);
            bool same = true, differs = false;
            for (int i = 0; i < a.Length; i++)
            {
                same &= a[i] == b[i];
                differs |= a[i] != c[i];
            }
            TestAssert.That(same, "the same samples and seed must form identical images");
            TestAssert.That(differs, "a different seed must change the speckle");

            // Range axis points north here: +z (land) is near range (low columns), -z (water) far range.
            double land = 0.0, water = 0.0;
            int landCount = 0, waterCount = 0;
            for (int row = 0; row < 64; row++)
            {
                for (int column = 4; column < 28; column++) { land += a[row * 64 + column]; landCount++; }
                for (int column = 36; column < 60; column++) { water += a[row * 64 + column]; waterCount++; }
            }
            TestAssert.That(land / landCount > water / waterCount + 40.0, "water must image much darker than land");

            var probe = new SarImageFormer(64, 64, 640.0, geometry, 21);
            probe.Project(0.0, 0.0, 300.0, 0.0, out int pointColumn, out int pointRow);
            TestAssert.That(a[pointRow * 64 + pointColumn] >= 250, "a point target must saturate");
        }
    }
}
