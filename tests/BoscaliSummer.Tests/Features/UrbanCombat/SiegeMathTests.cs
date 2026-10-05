using BoscaliSummer.Garrisons;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class SiegeMathTests
    {
        public static void Run()
        {
            // Tiers from cached shell counts.
            TestAssert.That(SiegeMath.ComputeTier(0) == 0, "empty ground is a hamlet");
            TestAssert.That(SiegeMath.ComputeTier(9) == 0, "nine shells stay a hamlet");
            TestAssert.That(SiegeMath.ComputeTier(10) == 1, "ten shells make a town");
            TestAssert.That(SiegeMath.ComputeTier(29) == 1, "twenty-nine shells stay a town");
            TestAssert.That(SiegeMath.ComputeTier(30) == 2, "thirty shells make a city");
            TestAssert.That(SiegeMath.ComputeTier(59) == 2, "fifty-nine shells stay a city");
            TestAssert.That(SiegeMath.ComputeTier(60) == 3, "sixty shells make a metro");
            TestAssert.That(SiegeMath.ComputeTier(400) == 3, "huge downtowns cap at metro");

            // Defense multipliers: vanilla at hamlet/scale 0, steep in cities.
            TestAssert.That(SiegeMath.DefenseMultiplier(0, 0, 1f) == 1f, "hamlets stay vanilla");
            TestAssert.That(SiegeMath.DefenseMultiplier(3, 6, 0f) == 1f, "zero scale disables everything");
            TestAssert.That(SiegeMath.DefenseMultiplier(3, 6, -1f) == 1f, "negative scale disables everything");
            TestAssert.That(SiegeMath.DefenseMultiplier(1, 0, 1f) == 2.5f, "town base multiplier");
            TestAssert.That(SiegeMath.DefenseMultiplier(2, 0, 1f) == 4f, "city base multiplier");
            TestAssert.That(SiegeMath.DefenseMultiplier(3, 0, 1f) == 6.5f, "metro base multiplier");
            TestAssert.That(SiegeMath.DefenseMultiplier(3, 6, 1f) == 11f, "six nests add 4.5 in a metro");
            TestAssert.That(SiegeMath.DefenseMultiplier(2, 2, 0.5f) == 3.25f, "half scale halves the bonus");
            TestAssert.That(SiegeMath.DefenseMultiplier(9, 0, 1f) == 6.5f, "wild tiers clamp to metro");
            TestAssert.That(SiegeMath.DefenseMultiplier(-2, 0, 1f) == 1f, "negative tiers clamp to hamlet");
            TestAssert.That(SiegeMath.DefenseMultiplier(2, -4, 1f) == 4f, "negative nests are ignored");

            TestAssert.That(SiegeMath.ApplyDefense(10f, 3, 6, 1f) == 110f, "metro with nests defends at 110");
            TestAssert.That(SiegeMath.ApplyDefense(0f, 3, 6, 1f) == 0f, "zero-defense missions stay instant");
            TestAssert.That(SiegeMath.ApplyDefense(-5f, 3, 6, 1f) == -5f, "negative defense passes through");

            // Siege floor: no nests, no floor; hamlets never gate; caps below full control.
            TestAssert.That(SiegeMath.SiegeFloor(0, 6, 1f) == 0f, "hamlets never gate the drain");
            TestAssert.That(SiegeMath.SiegeFloor(3, 0, 1f) == 0f, "cleared nests lift the floor");
            TestAssert.That(SiegeMath.SiegeFloor(3, 2, 0f) == 0f, "zero scale lifts the floor");
            TestAssert.That(SiegeMath.SiegeFloor(1, 1, 1f) == 0.25f, "town floor");
            TestAssert.That(SiegeMath.SiegeFloor(2, 4, 1f) == 0.45f, "city floor");
            TestAssert.That(SiegeMath.SiegeFloor(3, 6, 1f) == 0.6f, "metro floor");
            TestAssert.That(SiegeMath.SiegeFloor(3, 6, 3f) == 0.9f, "max scale caps the floor below full");

            // Armor bonus: open ground unchanged, cities reward the spearhead.
            TestAssert.That(SiegeMath.ArmorMultiplier(0, 1f) == 1f, "no bonus outside towns");
            TestAssert.That(SiegeMath.ArmorMultiplier(1, 1f) == 1.5f, "town armor bonus");
            TestAssert.That(SiegeMath.ArmorMultiplier(2, 1f) == 2f, "city armor bonus");
            TestAssert.That(SiegeMath.ArmorMultiplier(3, 1f) == 2.5f, "metro armor bonus");
            TestAssert.That(SiegeMath.ArmorMultiplier(3, 0f) == 1f, "zero scale removes the bonus");

            // Garrison counts: config plus urban bonus inside the zone ceiling.
            TestAssert.That(SiegeMath.EffectiveGarrisons(2, 0, 6) == 2, "hamlets keep the configured count");
            TestAssert.That(SiegeMath.EffectiveGarrisons(2, 1, 6) == 3, "towns add one nest");
            TestAssert.That(SiegeMath.EffectiveGarrisons(2, 3, 6) == 5, "metros add three nests");
            TestAssert.That(SiegeMath.EffectiveGarrisons(6, 3, 6) == 6, "counts cap at the zone ceiling");
            TestAssert.That(SiegeMath.EffectiveGarrisons(0, 3, 6) == 0, "zero still leaves zones undefended");
            TestAssert.That(SiegeMath.EffectiveGarrisons(-2, 3, 6) == 0, "negative config stays undefended");
        }
    }
}
