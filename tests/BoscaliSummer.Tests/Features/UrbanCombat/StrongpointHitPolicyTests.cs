using System;
using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class StrongpointHitPolicyTests
    {
        // Vanilla blast power is yield^(1/3); yields read from the stock munition prefabs.
        private static float Power(float yield) => (float)Math.Pow(yield, 0.3333);

        public static void Run()
        {
            float agm = StrongpointHitPolicy.HitWeight(Power(9f));
            float bomb125 = StrongpointHitPolicy.HitWeight(Power(100f));
            float bomb500 = StrongpointHitPolicy.HitWeight(Power(400f));
            float penetrator = StrongpointHitPolicy.HitWeight(Power(800f));
            TestAssert.That(Math.Abs(bomb125 - 1f) < 0.02f, "a 125 kg bomb is one unit of structure");
            TestAssert.That(agm < 0.15f, "an AGM barely scratches a strongpoint");
            TestAssert.That(bomb500 > 2.3f && bomb500 < 2.7f, "a 500 kg bomb counts about 2.5");
            TestAssert.That(penetrator > 3.8f, "a penetrator nearly levels a small block alone");
            TestAssert.That(StrongpointHitPolicy.HitWeight(0f) == 0f, "no shockwave, no wear");
            TestAssert.That(StrongpointHitPolicy.HitWeight(0.4f) == 0f, "below vanilla's shockwave floor, no wear");

            TestAssert.That(StrongpointHitPolicy.Structure(100f) == 3f, "small roofs still take three bombs");
            TestAssert.That(StrongpointHitPolicy.Structure(896f) == 4f, "a midrise block takes four");
            TestAssert.That(StrongpointHitPolicy.Structure(5120f) == 8f, "a mall caps at eight");

            TestAssert.That(StrongpointHitPolicy.Decide(0f, 4f, Power(100f)) == StrongpointHitPolicy.Verdict.Count,
                "the first bomb counts");
            TestAssert.That(StrongpointHitPolicy.Decide(3f, 4f, Power(100f)) == StrongpointHitPolicy.Verdict.Final,
                "the bomb that empties the pool is final");
            TestAssert.That(StrongpointHitPolicy.Decide(9f, 4f, Power(100f)) == StrongpointHitPolicy.Verdict.Final,
                "past the pool stays final");
            TestAssert.That(StrongpointHitPolicy.Decide(0f, 4f, 0f) == StrongpointHitPolicy.Verdict.Ignore,
                "gunfire is ignored");
            TestAssert.That(StrongpointHitPolicy.Decide(0f, 8f, Power(11000f)) == StrongpointHitPolicy.Verdict.Overkill,
                "demolition bombs flatten outright");
            float worn = 0f;
            int agms = 0;
            while (StrongpointHitPolicy.Decide(worn, 4f, Power(9f)) != StrongpointHitPolicy.Verdict.Final && agms < 1000)
            {
                worn += agm;
                agms++;
            }
            TestAssert.That(agms >= 35, "a midrise shrugs off dozens of AGMs");

            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(0.25f) == 75f, "a quarter worn steps to 75");
            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(2f) == 0f, "steps never go negative");
            TestAssert.That(StrongpointHitPolicy.SteppedHitPoints(0.25f, 60f) == 45f, "steps scale with the shell's own HP");
            TestAssert.That(StrongpointHitPolicy.CarrierHitPoints(0.5f) == 50f, "carrier mirrors the fraction");
            TestAssert.That(StrongpointHitPolicy.CarrierHitPoints(1f) == StrongpointHitPolicy.CarrierFloor,
                "carrier never reaches zero before the kill");
            TestAssert.That(StrongpointHitPolicy.Fraction(1f, 4f) == 0.25f, "fraction is damage over structure");

            TestAssert.That(StrongpointHitPolicy.DugoutStage(100f) == 0, "full carrier is stage 0");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(75f) == 1, "75 HP is stage 1");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(50f) == 2, "50 HP is stage 2");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(25f) == 3, "25 HP is stage 3");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(0f) == 3, "empty carrier stays stage 3");
            TestAssert.That(StrongpointHitPolicy.DugoutStage(87f) == 0, "splash between ticks stages down");
        }
    }
}
