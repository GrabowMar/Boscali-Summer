using System;
using BoscaliSummer.Features.Trenches.Domain;

namespace BoscaliSummer.Tests.Features.Trenches
{
    internal static class TrenchTests
    {
        public static void Run()
        {
            TestSappingAndLinking();
            TestStageProgressionRules();
            TestSectorLayout();
            TestAssert.That(TrenchTacticalMath.DefenderBudget(1) == 2 && TrenchTacticalMath.DefenderBudget(2) == 3 &&
                TrenchTacticalMath.DefenderBudget(3) == 4 && TrenchTacticalMath.DefenderBudget(4) == 4 &&
                TrenchTacticalMath.DefenderBudget(5) == 4, "Defenses grow 2/3/4 and stay sparse");
            TestAssert.That(!TrenchTacticalMath.CanConstruct(false, 59, 60, 45), "Damage suppresses construction");
            TestAssert.That(TrenchTacticalMath.CanConstruct(false, 60, 60, 45), "Survivors resume after a full quiet minute");
            TestAssert.That(!TrenchTacticalMath.CanConstruct(true, 600, 60, 45), "An overrun position never rebuilds defenders");
            TestAssert.That(TrenchTacticalMath.IsBuildableGround(20f, 1f), "Dry level ground accepts trenches");
            TestAssert.That(!TrenchTacticalMath.IsBuildableGround(2f, 1f), "Shore and water reject trenches");
            TestAssert.That(!TrenchTacticalMath.IsBuildableGround(20f, 0.98f), "Steep ground rejects trenches");
            TestAssert.That(!TrenchTacticalMath.IsBuildableGround(float.NaN, 1f), "Unknown ground fails closed");
        }

        private static void TestSappingAndLinking()
        {
            TestAssert.That(!TrenchTacticalMath.IsSappingEligible(5f), "5m is too close for a junction trench");
            TestAssert.That(TrenchTacticalMath.IsSappingEligible(15f), "15m is ideal junction range");
            TestAssert.That(TrenchTacticalMath.IsSappingEligible(35f), "35m is still a valid junction");
            TestAssert.That(TrenchTacticalMath.IsSappingEligible(TrenchTacticalMath.LinkRange),
                "A full-link gap is joinable");
            TestAssert.That(!TrenchTacticalMath.IsSappingEligible(70f), "70m is too far for a direct junction");
            TestAssert.That(TrenchTacticalMath.LinkMargin >= TrenchTacticalMath.LinkRange,
                "The junction corridor margin covers the whole joinable gap");
        }

        private static void TestStageProgressionRules()
        {
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(0, 3, 0, 0) == 0, "Unlinked scrapes stay Stage 0");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(0, 3, 2, 0) == 1, "Linked scrapes advance to Stage 1");

            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(1, 5, 4, 0) == 1, "A partial line cannot deepen yet");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(1, 7, 6, 0) == 2, "A connected seed line advances to Stage 2");

            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(2, 7, 6, 0) == 3, "A connected fire line advances to Stage 3");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(3, 9, 8, 0) == 4, "An extended line advances to Stage 4");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(3, 7, 6, 0) == 3, "An unextended line stays Stage 3");

            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(4, 12, 13, 0) == 4, "A support line without a dugout stays Stage 4");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(4, 12, 13, 1) == 5, "A linked support line advances to Stage 5");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(5, 17, 18, 1) == 5, "A belt missing a dugout cannot sap forward");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(5, 17, 16, 2) == 5, "An unjoined support span cannot sap forward");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(5, 17, 18, 2) == 6, "A whole belt advances to forward saps");
            TestAssert.That(TrenchTacticalMath.EvaluateNextStage(6, 19, 20, 2) == 6, "A finished belt never advances further");
        }

        private static void TestSectorLayout()
        {
            TestAssert.That(TrenchTacticalMath.LineSpan(TrenchTacticalMath.SeedBayCount) == 132f,
                "Seed line spans 132m across the sector");
            TestAssert.That(TrenchTacticalMath.LineOffset(0, 7) == -66f && TrenchTacticalMath.LineOffset(6, 7) == 66f &&
                TrenchTacticalMath.LineOffset(3, 7) == 0f, "Seed bays are centered on the sector");
            TestAssert.That(TrenchTacticalMath.CapFlankLimit(1250f) == TrenchTacticalMath.MaxFlankHalfLength,
                "Huge border sides cap at the network half-width");
            TestAssert.That(TrenchTacticalMath.CapFlankLimit(80f) == TrenchTacticalMath.MinFlankHalfLength,
                "Tight border sides keep the minimum half-width");
            TestAssert.That(TrenchTacticalMath.SupportLineDepth >= 100f && TrenchTacticalMath.RearLineDepth >= 200f,
                "Support and rear lines sit at deliberate field-position depth");
            TestAssert.That(TrenchTacticalMath.ForwardLimit > TrenchTacticalMath.SapDepth &&
                TrenchTacticalMath.SapDepth > TrenchTacticalMath.SupportLineDepth * 0.2f,
                "Forward saps push a real listening post toward the enemy");
            TestAssert.That(TrenchTacticalMath.MaxFlankHalfLength * 2f + TrenchTacticalMath.LinkRange >= 380f,
                "Flank reach plus a junction trench spans the 380m sector spacing");
        }
    }
}
