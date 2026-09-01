using System;
using BoscaliSummer.Core;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            VerifyFeatureGraph();
            VerifyServiceRegistry();

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

            Console.WriteLine("BoscaliSummer.Tests: all framework and deterministic assertions passed.");
            return 0;
        }

        private static void VerifyFeatureGraph()
        {
            FeatureMetadata[] features =
            {
                new FeatureMetadata("urban-combat", "Urban combat", "fire-and-destruction"),
                new FeatureMetadata("networking", "Networking"),
                new FeatureMetadata("fire-and-destruction", "Fire and destruction", "networking")
            };
            int[] order = FeatureGraph.Sort(features);
            Assert(order.Length == 3, "feature graph dropped an entry");
            Assert(order[0] == 1 && order[1] == 2 && order[2] == 0,
                "feature dependencies were not ordered before consumers");
            Assert(FeatureId.IsValid("support-calls"), "valid feature ID was rejected");
            Assert(!FeatureId.IsValid("Support Calls"), "invalid feature ID was accepted");

            AssertThrows<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("same", "One"),
                new FeatureMetadata("same", "Two")
            }), "duplicate feature IDs were accepted");
            AssertThrows<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("dependent", "Dependent", "missing")
            }), "missing feature dependency was accepted");
            AssertThrows<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("cycle-a", "Cycle A", "cycle-b"),
                new FeatureMetadata("cycle-b", "Cycle B", "cycle-a")
            }), "feature dependency cycle was accepted");
        }

        private static void VerifyServiceRegistry()
        {
            var registry = new ServiceRegistry();
            var expected = new ExampleService();
            registry.Add(expected);
            Assert(registry.TryGet(out ExampleService actual) && ReferenceEquals(expected, actual),
                "registered service could not be resolved");
            Assert(ReferenceEquals(expected, registry.GetRequired<ExampleService>()),
                "required service lookup returned the wrong instance");
            AssertThrows<InvalidOperationException>(() => registry.Add(new ExampleService()),
                "duplicate service registration was accepted");
            AssertThrows<InvalidOperationException>(() => registry.GetRequired<MissingService>(),
                "missing required service was accepted");
        }

        private static void AssertThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class ExampleService { }
        private sealed class MissingService { }
    }
}
