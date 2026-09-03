using System;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class StrongholdTests
    {
        public static void Run()
        {
            // 1. Armor mitigation tests
            TestAssert.That(
                StrongholdDamagePolicy.CalculateMitigatedDamage(0f, 0f, 0f, 0f, 0f, 25f, 50f) == 0f,
                "zero damage should yield 0 net damage");

            TestAssert.That(
                StrongholdDamagePolicy.CalculateMitigatedDamage(-10f, -50f, 1f, -5f, -2f, 25f, 50f) == 0f,
                "negative damage should be clamped to 0");

            // Pierce armor resistance: small arms / light rounds (< 25 pierce) deflected
            TestAssert.That(
                StrongholdDamagePolicy.CalculateMitigatedDamage(15f, 0f, 0f, 0f, 0f, 25f, 50f) == 0f,
                "pierce below pierce armor should be fully mitigated");

            // Pierce above armor penetrates
            float penDamage = StrongholdDamagePolicy.CalculateMitigatedDamage(50f, 0f, 0f, 0f, 0f, 25f, 50f);
            TestAssert.That(penDamage > 0f, "pierce above armor must deal damage");

            // Blast armor resistance: light shrapnel (< 50 blast) mitigated
            TestAssert.That(
                StrongholdDamagePolicy.CalculateMitigatedDamage(0f, 40f, 1f, 0f, 0f, 25f, 50f) == 0f,
                "blast below blast armor should be fully mitigated");

            // Heavy explosive blast deals significant damage
            float bombDamage = StrongholdDamagePolicy.CalculateMitigatedDamage(100f, 500f, 1f, 50f, 20f, 25f, 50f);
            TestAssert.That(bombDamage > penDamage, "heavy bomb damage must exceed light pierce hit");

            // Monotonic damage scaling
            float prevDamage = 0f;
            for (float raw = 10f; raw <= 500f; raw += 25f)
            {
                float current = StrongholdDamagePolicy.CalculateMitigatedDamage(raw, raw, 1f, 0f, 0f, 25f, 50f);
                TestAssert.That(current >= prevDamage, "damage mitigation must scale monotonically with incoming damage");
                prevDamage = current;
            }

            // 2. Damage fraction tests
            TestAssert.That(
                StrongholdDamagePolicy.CalculateDamageFraction(2500f, 2500f) == 0f,
                "full HP must yield 0 damage fraction (pristine)");

            TestAssert.That(
                Math.Abs(StrongholdDamagePolicy.CalculateDamageFraction(1250f, 2500f) - 0.5f) < 0.0001f,
                "half HP must yield 0.5 damage fraction");

            TestAssert.That(
                StrongholdDamagePolicy.CalculateDamageFraction(0f, 2500f) == 1f,
                "zero HP must yield 1.0 damage fraction (fully damaged)");

            TestAssert.That(
                StrongholdDamagePolicy.CalculateDamageFraction(-50f, 2500f) == 1f,
                "overkill negative HP must clamp to 1.0 damage fraction");

            TestAssert.That(
                StrongholdDamagePolicy.CalculateDamageFraction(100f, 0f) == 1f,
                "zero max HP must handle gracefully without division by zero");

            // 3. Thermal heat glow & cooling tests
            float lightGlow = StrongholdDamagePolicy.CalculateThermalGlowAddition(50f, 10f);
            float heavyGlow = StrongholdDamagePolicy.CalculateThermalGlowAddition(500f, 200f);
            TestAssert.That(lightGlow >= 0.35f, "minimum heat flash must be at least 0.35");
            TestAssert.That(heavyGlow > lightGlow, "heavy explosion must produce higher thermal glow than light hit");
            TestAssert.That(heavyGlow <= 2.2f, "thermal glow must not exceed maximum intensity cap");

            float cooled = StrongholdDamagePolicy.CoolThermalGlow(1.5f, 0.65f, 1.0f);
            TestAssert.That(Math.Abs(cooled - (1.5f - 0.65f)) < 0.0001f, "thermal cooling step must match rate * time");

            float fullyCooled = StrongholdDamagePolicy.CoolThermalGlow(0.2f, 0.65f, 1.0f);
            TestAssert.That(fullyCooled == 0f, "cooling past zero must clamp to 0 without going negative");

            // 4. Soot tint tests
            var pristine = StrongholdDamagePolicy.CalculateSootTint(0f);
            TestAssert.That(pristine.r == 1f && pristine.g == 1f && pristine.b == 1f, "pristine tint must be pure white");

            var damaged = StrongholdDamagePolicy.CalculateSootTint(1f);
            TestAssert.That(damaged.r < 0.35f && damaged.g < 0.35f && damaged.b < 0.35f,
                "maximum damage tint must be dark soot-charred");
            TestAssert.That(damaged.r > 0f && damaged.g > 0f && damaged.b > 0f,
                "soot tint must not be completely pitch black");
        }
    }
}
