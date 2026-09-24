using System;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class UrbanRuinMathTests
    {
        public static void Run()
        {
            // Fractions are occupy-relative and clamped.
            TestAssert.That(UrbanRuinMath.DamageFraction(100f, 100f) == 0f, "undamaged shell reads zero");
            TestAssert.That(Math.Abs(UrbanRuinMath.DamageFraction(100f, 65f) - 0.35f) < 1e-6f, "thirty-five damage reads 0.35");
            TestAssert.That(UrbanRuinMath.DamageFraction(100f, 0f) == 1f, "destroyed shell reads one");
            TestAssert.That(UrbanRuinMath.DamageFraction(100f, -20f) == 1f, "overkill clamps at one");
            TestAssert.That(UrbanRuinMath.DamageFraction(100f, 140f) == 0f, "overheal clamps at zero");
            TestAssert.That(UrbanRuinMath.DamageFraction(0f, 0f) == 0f, "zero max reads zero");
            TestAssert.That(UrbanRuinMath.DamageFraction(-50f, 10f) == 0f, "negative max reads zero");

            // Stage boundaries.
            TestAssert.That(UrbanRuinMath.DamageStage(0f) == UrbanRuinMath.StageIntact, "zero is intact");
            TestAssert.That(UrbanRuinMath.DamageStage(0.349f) == UrbanRuinMath.StageIntact, "below scarred stays intact");
            TestAssert.That(UrbanRuinMath.DamageStage(0.35f) == UrbanRuinMath.StageScarred, "0.35 scars");
            TestAssert.That(UrbanRuinMath.DamageStage(0.749f) == UrbanRuinMath.StageScarred, "below ravaged stays scarred");
            TestAssert.That(UrbanRuinMath.DamageStage(0.75f) == UrbanRuinMath.StageRavaged, "0.75 ravages");
            TestAssert.That(UrbanRuinMath.DamageStage(1f) == UrbanRuinMath.StageRavaged, "full damage ravages");
            TestAssert.That(UrbanRuinMath.DamageStage(-1f) == UrbanRuinMath.StageIntact, "negative is intact");

            // Strongpoint value survives scarring, not ravaging.
            TestAssert.That(UrbanRuinMath.CountsAsStrongpoint(0f), "intact shell holds the floor");
            TestAssert.That(UrbanRuinMath.CountsAsStrongpoint(0.74f), "scarred shell holds the floor");
            TestAssert.That(!UrbanRuinMath.CountsAsStrongpoint(0.75f), "ravaged shell drops the floor");
            TestAssert.That(!UrbanRuinMath.CountsAsStrongpoint(1f), "destroyed shell drops the floor");
        }
    }
}
