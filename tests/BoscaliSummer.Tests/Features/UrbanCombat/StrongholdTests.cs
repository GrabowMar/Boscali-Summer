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
        }
    }
}
