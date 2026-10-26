using System;
using BoscaliSummer.Modules.Ares.Domain;

namespace BoscaliSummer.Tests.Features.Ares
{
    /// <summary>Pins ARES's placement geometry, drag heading, barrage spread, clock text and kit data.</summary>
    internal static class AresMathTests
    {
        public static void Run()
        {
            AresMath.Slot(0, 1, 20f, out float r, out float f);
            TestAssert.That(r == 0f && f == 0f, "one unit stands on the click");
            AresMath.Slot(0, 2, 20f, out float r0, out _);
            AresMath.Slot(1, 2, 20f, out float r1, out _);
            TestAssert.That(r0 == -10f && r1 == 10f, "two units straddle the click");
            AresMath.Slot(4, 6, 20f, out float r4, out float f4);
            AresMath.Slot(5, 6, 20f, out float r5, out _);
            TestAssert.That(f4 == -20f && r4 == -10f && r5 == 10f, "a second row of two is centred one spacing behind");

            AresMath.Rotate(0f, 100f, 0f, out float x, out float z);
            TestAssert.That(Near(x, 0f) && Near(z, 100f), "forward at heading 0 is north");
            AresMath.Rotate(0f, 100f, 90f, out x, out z);
            TestAssert.That(Near(x, 100f) && Near(z, 0f), "forward at heading 90 is east");
            AresMath.Rotate(100f, 0f, 90f, out x, out z);
            TestAssert.That(Near(x, 0f) && Near(z, -100f), "right of an east heading is south");

            TestAssert.That(AresMath.HeadingFromDrag(0f, 1f, 33f) == 33f, "a tiny drag keeps the last heading");
            TestAssert.That(Near(AresMath.HeadingFromDrag(0f, 50f, 0f), 0f), "drag north is 0");
            TestAssert.That(Near(AresMath.HeadingFromDrag(50f, 0f, 0f), 90f), "drag east is 90");
            TestAssert.That(Near(AresMath.HeadingFromDrag(-50f, 0f, 0f), 270f), "drag west is 270");

            for (int i = 0; i < 64; i++)
            {
                AresMath.Barrage(7, i, 250f, out float bx, out float bz);
                AresMath.Barrage(7, i, 250f, out float cx, out float cz);
                TestAssert.That(bx * bx + bz * bz <= 250f * 250f + 0.01f, "barrage round " + i + " lands inside the radius");
                TestAssert.That(bx == cx && bz == cz, "barrage round " + i + " is deterministic");
            }
            AresMath.Barrage(7, 0, 250f, out float ax, out float az);
            AresMath.Barrage(7, 1, 250f, out float qx, out float qz);
            TestAssert.That(ax != qx || az != qz, "rounds do not stack on one point");

            TestAssert.That(AresMath.Hour(7f) == "07:00" && AresMath.Hour(13.5f) == "13:30", "clock text");
            TestAssert.That(AresMath.Hour(25f) == "01:00" && AresMath.Hour(-1f) == "23:00", "clock wraps both ways");

            TestAssert.That(AresMath.Kits.Length == 6, "six kits");
            foreach (AresKit kit in AresMath.Kits)
            {
                TestAssert.That(kit.Members.Length >= 3 && !string.IsNullOrEmpty(kit.Name) && !string.IsNullOrEmpty(kit.Blurb), kit.Name + " is filled");
                foreach (AresKitMember member in kit.Members)
                    TestAssert.That(kit.Naval ? member.Key.StartsWith("#", StringComparison.Ordinal) : !string.IsNullOrEmpty(member.Keyword),
                        kit.Name + " member " + member.Key + " can be resolved");
            }
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;
    }
}
