using BoscaliSummer.Fire;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class GroundSnapTests
    {
        public static void Run()
        {
            const float sea = 0f;
            TestAssert.That(
                FireGroundSnapPolicy.CanAnchor(4f, 3f, sea, 30f),
                "a surface impact must anchor a fire on the ground it hit");
            TestAssert.That(
                FireGroundSnapPolicy.CanAnchor(800f, 800f, sea, 30f),
                "a hit exactly at the reported point must anchor");
            TestAssert.That(
                !FireGroundSnapPolicy.CanAnchor(1200f, 2000f, sea, 30f),
                "an air-to-air air burst must not anchor a ground fire");
            TestAssert.That(
                !FireGroundSnapPolicy.CanAnchor(-40f, -38f, sea, 30f),
                "a seabed hit below sea level must not anchor a fire");
            TestAssert.That(
                FireGroundSnapPolicy.CanAnchor(120f, 80f, sea, 95f),
                "forest spread may descend one spread step down a valley side");
            TestAssert.That(
                !FireGroundSnapPolicy.CanAnchor(0f, 96f, sea, 95f),
                "a hit more than one spread step below the candidate is refused");
        }
    }
}
