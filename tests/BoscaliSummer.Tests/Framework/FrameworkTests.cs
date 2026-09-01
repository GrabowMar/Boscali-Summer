using System;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Tests.Framework
{
    internal static class FrameworkTests
    {
        public static void Run()
        {
            FeatureMetadata[] features =
            {
                new FeatureMetadata("support-calls", "Support calls", "progression"),
                new FeatureMetadata("progression", "Progression", "persistence"),
                new FeatureMetadata("persistence", "Persistence")
            };
            int[] order = FeatureGraph.Sort(features);
            TestAssert.That(order.Length == 3, "feature graph dropped an entry");
            TestAssert.That(order[0] == 2 && order[1] == 1 && order[2] == 0,
                "feature dependencies were not ordered before consumers");
            TestAssert.That(FeatureId.IsValid("support-calls"), "valid feature ID was rejected");
            TestAssert.That(!FeatureId.IsValid("Support Calls"), "invalid feature ID was accepted");

            TestAssert.Throws<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("same", "One"),
                new FeatureMetadata("same", "Two")
            }), "duplicate feature IDs were accepted");
            TestAssert.Throws<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("dependent", "Dependent", "missing")
            }), "missing feature dependency was accepted");
            TestAssert.Throws<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("cycle-a", "Cycle A", "cycle-b"),
                new FeatureMetadata("cycle-b", "Cycle B", "cycle-a")
            }), "feature dependency cycle was accepted");

            var registry = new ServiceRegistry();
            var expected = new ExampleService();
            registry.Add<IExampleService>(expected);
            TestAssert.That(registry.TryGet(out IExampleService actual) && ReferenceEquals(expected, actual),
                "registered service could not be resolved through its contract");
            TestAssert.That(ReferenceEquals(expected, registry.GetRequired<IExampleService>()),
                "required service lookup returned the wrong instance");
            TestAssert.Throws<InvalidOperationException>(
                () => registry.Add<IExampleService>(new ExampleService()),
                "duplicate service registration was accepted");
            TestAssert.Throws<InvalidOperationException>(() => registry.GetRequired<MissingService>(),
                "missing required service was accepted");
        }

        private interface IExampleService { }
        private sealed class ExampleService : IExampleService { }
        private sealed class MissingService { }
    }
}
