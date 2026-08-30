using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            Assert(Deterministic.Hash(1, 2, 3, 4) == Deterministic.Hash(1, 2, 3, 4), "hash must be stable");
            Assert(Deterministic.Hash(1, 2, 3, 4) != Deterministic.Hash(1, 2, 3, 5), "salt must affect hash");
            Assert(Deterministic.HashString("Airbase Alpha") == Deterministic.HashString("Airbase Alpha"), "string hash must be stable");
            Assert(Deterministic.HashString("Airbase Alpha") != Deterministic.HashString("Airbase Bravo"), "names must separate seeds");

            for (int i = -1000; i <= 1000; i++)
            {
                float value = Deterministic.UnitFloat(Deterministic.Hash(i, i * 7, -i));
                Assert(value >= 0f && value < 1f, "unit float outside [0,1)");
            }

            Assert(Deterministic.CellKey(0f, 0f, 32f) == Deterministic.CellKey(31.99f, 31.99f, 32f), "same positive cell split");
            Assert(Deterministic.CellKey(-0.01f, -0.01f, 32f) == Deterministic.CellKey(-31.99f, -31.99f, 32f), "negative floor cell split");
            Assert(Deterministic.CellKey(-0.01f, 0f, 32f) != Deterministic.CellKey(0f, 0f, 32f), "negative and positive cells collided");

            Console.WriteLine("BoscaliSummer.Tests: 2008 assertions passed.");
            return 0;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
