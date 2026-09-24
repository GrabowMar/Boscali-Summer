using BoscaliSummer.Features.FireAndDestruction.Domain;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class HitEscalationTests
    {
        public static void Run()
        {
            TestAssert.That(!HitEscalation.ShouldCount(10f, 10.1f),
                "a hit inside the debounce window must not count again");
            TestAssert.That(HitEscalation.ShouldCount(10f, 10f + HitEscalation.DebounceSeconds),
                "a hit exactly at the debounce edge counts");
            TestAssert.That(HitEscalation.ShouldCount(10f, 11f),
                "a hit after the debounce window counts");

            TestAssert.That(!HitEscalation.HasWisp(0), "no wisp before any hit");
            TestAssert.That(!HitEscalation.HasWisp(1), "no wisp on the first hit");
            TestAssert.That(HitEscalation.HasWisp(2), "the second hit starts the wisp");
            TestAssert.That(HitEscalation.HasWisp(9), "the wisp persists past the third hit");
            TestAssert.That(HitEscalation.WispIntensity(1) == 0f, "no wisp intensity on hit one");
            TestAssert.That(HitEscalation.WispIntensity(2) > 0f && HitEscalation.WispIntensity(2) < 1f,
                "hit two is a thin wisp, not the full plume");
            TestAssert.That(HitEscalation.WispIntensity(3) == 1f, "hit three is the full plume");
            TestAssert.That(HitEscalation.WispIntensity(12) == 1f, "plume intensity stays capped");

            TestAssert.That(
                HitEscalation.BreachSize(0f) == HitEscalation.MinBreachSize,
                "zero blast power still leaves the smallest breach");
            TestAssert.That(
                HitEscalation.BreachSize(1000f) == HitEscalation.MaxBreachSize,
                "a huge blast clamps to the largest breach");
            TestAssert.That(
                HitEscalation.BreachSize(5f) > HitEscalation.BreachSize(1f),
                "breach size must grow with blast power inside the band");
            for (float power = -4f; power <= 40f; power += 1f)
            {
                float size = HitEscalation.BreachSize(power);
                TestAssert.That(
                    size >= HitEscalation.MinBreachSize && size <= HitEscalation.MaxBreachSize,
                    "breach size must stay inside its band for every power");
            }

            TestAssert.That(
                HitEscalation.GroundScarDiameter(10f, 8f) == 10f * HitEscalation.GroundScarGrowth,
                "the scar grows slightly past the fallen footprint");
            TestAssert.That(
                HitEscalation.GroundScarDiameter(1f, 1f) == HitEscalation.MinGroundScar,
                "a shed still leaves a readable scar");
            TestAssert.That(
                HitEscalation.GroundScarDiameter(500f, 40f) == HitEscalation.MaxGroundScar,
                "a hangar scar clamps to the maximum");
            TestAssert.That(
                HitEscalation.TreeRowAshDiameter(113f, 31f) > HitEscalation.GroundScarDiameter(113f, 31f) * 0.5f,
                "a tree-row ash bed must cover a meaningful part of the row");
            TestAssert.That(
                HitEscalation.TreeRowAshDiameter(1f, 1f) == HitEscalation.MinTreeRowAsh,
                "ash diameter clamps at the minimum");
            TestAssert.That(
                HitEscalation.TreeRowAshDiameter(900f, 900f) == HitEscalation.MaxTreeRowAsh,
                "ash diameter clamps at the maximum");
        }
    }
}
