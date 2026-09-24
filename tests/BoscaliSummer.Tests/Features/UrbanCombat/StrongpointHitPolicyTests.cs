using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class StrongpointHitPolicyTests
    {
        public static void Run()
        {
            TestAssert.That(
                StrongpointHitPolicy.BlastTerm(30f, 10f, 5f, 5f) == 20f,
                "blast term must mirror vanilla: (30 - 10) * 5 / 5");
            TestAssert.That(
                StrongpointHitPolicy.BlastTerm(5f, 10f, 100f, 1f) == 0f,
                "blast below armor contributes nothing no matter the area");
            TestAssert.That(
                StrongpointHitPolicy.BlastTerm(11f, 10f, 1f, 0f) == 100f,
                "zero tolerance floors at 0.01 like vanilla");
            float total = StrongpointHitPolicy.TotalEstimate(
                10f, 30f, 5f, 0f, 4f, 0f, 1f, 10f, 5f, 0f, 1f);
            TestAssert.That(total == 10f + 20f + 0f + 4f,
                "total estimate must mirror vanilla pierce + blast + fire + impact");

            TestAssert.That(
                StrongpointHitPolicy.Decide(0, -10f, 0f, 20f, 20f) ==
                    StrongpointHitPolicy.Verdict.Count,
                "the first explosive hit past debounce counts");
            TestAssert.That(
                StrongpointHitPolicy.Decide(0, 0f, 0.1f, 500f, 500f) ==
                    StrongpointHitPolicy.Verdict.Ignore,
                "a salvo inside the debounce window is one hit");
            TestAssert.That(
                StrongpointHitPolicy.Decide(1, -10f, 0f, 19.9f, 500f) ==
                    StrongpointHitPolicy.Verdict.Ignore,
                "a huge non-blast call never wears a strongpoint down");
            TestAssert.That(
                StrongpointHitPolicy.Decide(0, -10f, 0f, 0f, 0f) ==
                    StrongpointHitPolicy.Verdict.Ignore,
                "bullets and plinking are ignored");
            TestAssert.That(
                StrongpointHitPolicy.Decide(3, -10f, 0f, 20f, 20f) ==
                    StrongpointHitPolicy.Verdict.Final,
                "the fourth separate explosive hit is final");
            TestAssert.That(
                StrongpointHitPolicy.Decide(0, 0f, 0.1f, 2000f, 2000f) ==
                    StrongpointHitPolicy.Verdict.Overkill,
                "overkill bypasses the debounce");
            TestAssert.That(
                StrongpointHitPolicy.Decide(0, -10f, 0f, 0f, 1000f) ==
                    StrongpointHitPolicy.Verdict.Overkill,
                "overkill needs no blast term");
            TestAssert.That(
                StrongpointHitPolicy.Decide(9, -10f, 0f, 20f, 20f) ==
                    StrongpointHitPolicy.Verdict.Final,
                "extra hits past the kill count stay final, never wrap around");

            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(1) == 75f, "one hit steps to 75");
            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(2) == 50f, "two hits step to 50");
            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(3) == 25f, "three hits step to 25");
            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(4) == 0f, "four hits step to 0");
            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(99) == 0f, "steps never go negative");

            TestAssert.That(StrongpointHitPolicy.DugoutStage(100f) == 0, "full carrier is stage 0");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(75f) == 1, "75 HP is stage 1");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(50f) == 2, "50 HP is stage 2");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(25f) == 3, "25 HP is stage 3");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(0f) == 3, "empty carrier stays stage 3");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(87f) == 0, "splash between ticks stages down");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(60f) == 1, "splash between ticks stages down");
        }
    }
}
