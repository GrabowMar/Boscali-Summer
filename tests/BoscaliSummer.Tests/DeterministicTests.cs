using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Tests
{
    internal static class DeterministicTests
    {
        public static void Run()
        {
            TestAssert.That(
                Deterministic.Hash(1, 2, 3, 4) == Deterministic.Hash(1, 2, 3, 4),
                "hash must be stable");
            TestAssert.That(
                Deterministic.Hash(1, 2, 3, 4) != Deterministic.Hash(1, 2, 3, 5),
                "salt must affect hash");
            TestAssert.That(
                Deterministic.HashString("Airbase Alpha") == Deterministic.HashString("Airbase Alpha"),
                "string hash must be stable");
            TestAssert.That(
                Deterministic.HashString("Airbase Alpha") != Deterministic.HashString("Airbase Bravo"),
                "names must separate seeds");

            for (int i = -1000; i <= 1000; i++)
            {
                float value = Deterministic.UnitFloat(Deterministic.Hash(i, i * 7, -i));
                TestAssert.That(value >= 0f && value < 1f, "unit float outside [0,1)");
            }

            TestAssert.That(
                Deterministic.CellKey(0f, 0f, 32f) == Deterministic.CellKey(31.99f, 31.99f, 32f),
                "same positive cell split");
            TestAssert.That(
                Deterministic.CellKey(-0.01f, -0.01f, 32f) ==
                Deterministic.CellKey(-31.99f, -31.99f, 32f),
                "negative floor cell split");
            TestAssert.That(
                Deterministic.CellKey(-0.01f, 0f, 32f) != Deterministic.CellKey(0f, 0f, 32f),
                "negative and positive cells collided");

        }
    }
}
