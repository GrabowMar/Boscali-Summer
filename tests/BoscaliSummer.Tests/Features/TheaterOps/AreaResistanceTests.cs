using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The director's resistance read: observed power where it has looked, never zero where it has not.</summary>
    internal static class AreaResistanceTests
    {
        public static void Run()
        {
            UnscoutedGroundTakesTheMedianOfScoutedTargets();
            WithNothingScoutedThePriorIsFour();
            HeldGroundAndMissingIntelKeepTheLiveScan();
            PowerRoundsHonestly();
        }

        private static void UnscoutedGroundTakesTheMedianOfScoutedTargets()
        {
            bool[] held = { false, false, false };
            bool[] known = { true, true, true };
            bool[] scouted = { true, true, false };
            float[] power = { 2.4f, 6.0f, 0f };
            int[] hostile = { 0, 0, 0 };
            int prior = AreaResistance.Resolve(3, held, known, scouted, power, hostile, new int[3]);
            TestAssert.That(hostile[0] == 2 && hostile[1] == 6, "scouted ground reads its observed power");
            TestAssert.That(prior == 6 && hostile[2] == 6,
                "unscouted ground takes the (upper) median of the scouted targets, never zero");
        }

        private static void WithNothingScoutedThePriorIsFour()
        {
            bool[] held = { false, false };
            bool[] known = { true, true };
            bool[] scouted = { false, false };
            float[] power = { 0f, 7.2f };
            int[] hostile = { 0, 0 };
            TestAssert.That(AreaResistance.Resolve(2, held, known, scouted, power, hostile, new int[2]) ==
                            AreaResistance.UnscoutedPrior, "no scouted target: the prior is 4");
            TestAssert.That(hostile[0] == 4 && hostile[1] == 7,
                "an unscouted target never reads below what is already known there");
        }

        private static void HeldGroundAndMissingIntelKeepTheLiveScan()
        {
            bool[] held = { true, false };
            bool[] known = { true, false };
            bool[] scouted = { true, false };
            float[] power = { 9f, 9f };
            int[] hostile = { 3, 1 };
            AreaResistance.Resolve(2, held, known, scouted, power, hostile, new int[2]);
            TestAssert.That(hostile[0] == 3, "held ground keeps the live contact count the defence watch needs");
            TestAssert.That(hostile[1] == 1, "without a picture the live scan stands");
        }

        private static void PowerRoundsHonestly()
        {
            TestAssert.That(AreaResistance.Round(2.5f) == 3 && AreaResistance.Round(2.49f) == 2, "power rounds half away from zero");
            TestAssert.That(AreaResistance.Round(float.NaN) == 0 && AreaResistance.Round(-1f) == 0,
                "no power is no resistance, never negative");
        }
    }
}
