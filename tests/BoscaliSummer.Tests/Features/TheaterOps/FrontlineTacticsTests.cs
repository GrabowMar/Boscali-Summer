using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class FrontlineTacticsTests
    {
        public static void Run()
        {
            var points = new[]
            {
                new FrontlineTracePoint(-1000f, 0f),
                new FrontlineTracePoint(0f, 0f),
                new FrontlineTracePoint(1000f, 0f),
                new FrontlineTracePoint(9000f, 9000f),
                new FrontlineTracePoint(10000f, 9000f)
            };
            var lengths = new[] { 3, 2 };
            TestAssert.That(FrontlineTactics.TrySlot(points, lengths, 2, 0f, -500f, 0,
                out float centerX, out float centerZ, out float tangentX, out float tangentZ) &&
                System.Math.Abs(centerX) < 1f && System.Math.Abs(centerZ) < 1f &&
                tangentX > .9f && System.Math.Abs(tangentZ) < .1f,
                "the nearest front segment supplies the center and tangent");
            TestAssert.That(FrontlineTactics.TrySlot(points, lengths, 2, 0f, -500f, 1,
                out float rightX, out _, out _, out _) && rightX > centerX + 100f,
                "a second vehicle spreads along the chosen front");
            TestAssert.That(FrontlineTactics.TrySlot(points, lengths, 2, 0f, -500f, 2,
                out float leftX, out _, out _, out _) && leftX < centerX - 100f,
                "a third vehicle spreads to the opposite side");
            TestAssert.That(!FrontlineTactics.TrySlot(points, lengths, 0, 0f, 0f, 0,
                out _, out _, out _, out _), "missing front data falls back to vanilla");
            TestAssert.That(FrontlineTactics.ShouldAdvance(3, 6, 89f) &&
                !FrontlineTactics.ShouldAdvance(2, 6, 89f) &&
                FrontlineTactics.ShouldAdvance(2, 6, 90f) &&
                !FrontlineTactics.ShouldAdvance(0, 6, 120f),
                "half the group or a bounded wait after first arrival releases the next stage");
            TestAssert.That(FrontlineTactics.ShouldWithdraw(2, 6) &&
                !FrontlineTactics.ShouldWithdraw(3, 6) &&
                !FrontlineTactics.ShouldWithdraw(0, 2),
                "a formed group withdraws below half strength");
            TestAssert.That(FrontlineTactics.PincerAxis(0, 1) == 0 &&
                FrontlineTactics.PincerAxis(0, 2) == -1 &&
                FrontlineTactics.PincerAxis(1, 2) == 1 &&
                FrontlineTactics.PincerAxis(2, 3) == 0,
                "a pincer needs two viable groups and only the lead pair take flanks");
        }
    }
}
