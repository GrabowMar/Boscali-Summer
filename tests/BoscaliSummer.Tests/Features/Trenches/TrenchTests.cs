using System;
using BoscaliSummer.Features.Trenches.Domain;

namespace BoscaliSummer.Tests.Features.Trenches
{
    internal static class TrenchTests
    {
        public static void Run()
        {
            TestZigzagOffsets();
            TestSappingDistance();
            TestFlankHookMath();
            TestStageProgressionRules();
        }

        private static void TestZigzagOffsets()
        {
            // Edge under 10m has no zigzags
            TestAssert.That(TrenchTacticalMath.ComputeBayCount(8f) == 1, "Short trench has 1 bay");
            TestAssert.That(TrenchTacticalMath.ComputeZigzagOffset(1, 8f) == 0f, "Short trench has 0 offset");

            // 40m trench has multiple bays with alternating offsets
            int bays = TrenchTacticalMath.ComputeBayCount(40f);
            TestAssert.That(bays >= 4, "40m trench must have at least 4 bays");

            float offset1 = TrenchTacticalMath.ComputeZigzagOffset(1, 40f);
            float offset2 = TrenchTacticalMath.ComputeZigzagOffset(2, 40f);

            TestAssert.That(offset1 > 0f, "Odd bay must displace forward toward threat");
            TestAssert.That(offset2 < 0f, "Even bay must displace rearward into parados");
            TestAssert.That(Math.Abs(offset1) <= 3.2f, "Offset must not exceed max clamp");
        }

        private static void TestSappingDistance()
        {
            TestAssert.That(!TrenchTacticalMath.IsSappingEligible(5f), "5m is too close for sapping link");
            TestAssert.That(TrenchTacticalMath.IsSappingEligible(15f), "15m is ideal sapping range");
            TestAssert.That(TrenchTacticalMath.IsSappingEligible(35f), "35m is valid sapping range");
            TestAssert.That(!TrenchTacticalMath.IsSappingEligible(70f), "70m is too far for direct trench link");
        }

        private static void TestFlankHookMath()
        {
            // Threat is facing North (0, 1). Flank hook must extend South (0, -1).
            TrenchTacticalMath.ComputeFlankHook(100f, 200f, 0f, 1f, 16f, out float hookX, out float hookZ);
            TestAssert.That(Math.Abs(hookX - 100f) < 0.001f, "X should remain 100m");
            TestAssert.That(Math.Abs(hookZ - 184f) < 0.001f, "Z should hook rearward to 184m");

            // Threat is facing East (1, 0). Flank hook must extend West (-1, 0).
            TrenchTacticalMath.ComputeFlankHook(50f, 50f, 1f, 0f, 20f, out float hookX2, out float hookZ2);
            TestAssert.That(Math.Abs(hookX2 - 30f) < 0.001f, "X should hook rearward to 30m");
            TestAssert.That(Math.Abs(hookZ2 - 50f) < 0.001f, "Z should remain 50m");
        }

        private static void TestStageProgressionRules()
        {
            // Stage 0 (Scrapes)
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(0, 3, 0, 0) == 0, "Unlinked scrapes stay Stage 0");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(0, 3, 2, 0) == 1, "Linked scrapes advance to Stage 1");

            // Stage 1 (Crawl)
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(1, 3, 2, 0) == 2, "Connected crawlways advance to Stage 2");

            // Stage 2 (Fire Trench)
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(2, 3, 2, 0) == 2, "Without bunkers stays Stage 2");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(2, 3, 2, 1) == 3, "Fortified bunker advances to Stage 3");

            // Stage 3 (Hardened)
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(3, 3, 3, 1) == 3, "Without rear lines stays Stage 3");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(3, 5, 4, 1) == 4, "Integrated network advances to Stage 4");
        }
    }
}
