using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;

namespace BoscaliSummer.Tests.Framework
{
    internal static class FrameworkTests
    {
        public static void Run()
        {
            ModuleMetadata[] features =
            {
                new ModuleMetadata("support-calls", "Support calls", "progression"),
                new ModuleMetadata("progression", "Progression", "persistence"),
                new ModuleMetadata("persistence", "Persistence")
            };
            int[] order = ModuleGraph.Sort(features);
            TestAssert.That(order.Length == 3, "feature graph dropped an entry");
            TestAssert.That(order[0] == 2 && order[1] == 1 && order[2] == 0,
                "feature dependencies were not ordered before consumers");
            TestAssert.That(ModuleId.IsValid("support-calls"), "valid feature ID was rejected");
            TestAssert.That(!ModuleId.IsValid("Support Calls"), "invalid feature ID was accepted");

            TestAssert.Throws<InvalidOperationException>(() => ModuleGraph.Sort(new[]
            {
                new ModuleMetadata("same", "One"),
                new ModuleMetadata("same", "Two")
            }), "duplicate feature IDs were accepted");
            TestAssert.Throws<InvalidOperationException>(() => ModuleGraph.Sort(new[]
            {
                new ModuleMetadata("dependent", "Dependent", "missing")
            }), "missing feature dependency was accepted");
            TestAssert.Throws<InvalidOperationException>(() => ModuleGraph.Sort(new[]
            {
                new ModuleMetadata("cycle-a", "Cycle A", "cycle-b"),
                new ModuleMetadata("cycle-b", "Cycle B", "cycle-a")
            }), "feature dependency cycle was accepted");

            // Settings can switch off a feature another one needs (Command under Trenches); the
            // dependent must drop out with a reason instead of failing the whole load.
            string[] missing = ModuleGraph.MissingDependencies(new[]
            {
                new ModuleMetadata("overlay", "Overlay", "trenches"),
                new ModuleMetadata("trenches", "Trenches", "command"),
                new ModuleMetadata("radio", "Radio")
            });
            TestAssert.That(missing[0] == "trenches" && missing[1] == "command" && missing[2] == null,
                "a feature whose dependency is switched off, and anything built on it, must be left out by name");
            TestAssert.Throws<InvalidOperationException>(() => ModuleGraph.MissingDependencies(new[]
            {
                new ModuleMetadata("same", "One", "missing"),
                new ModuleMetadata("same", "Two")
            }), "duplicate feature IDs must not hide behind a missing dependency");

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

            TestAssert.That(HostSettingMath.Step(0.5f, 1, 0.25f, 4f, 0.25f) == 0.75f,
                "a host setting steps by its own increment");
            TestAssert.That(HostSettingMath.Step(0.25f, -1, 0.25f, 4f, 0.25f) == 0.25f,
                "a host setting never steps below its floor");
            TestAssert.That(HostSettingMath.Step(4f, 1, 0.25f, 4f, 0.25f) == 4f,
                "a host setting never steps past its ceiling");
            TestAssert.That(HostSettingMath.Step(float.NaN, 1, 0.25f, 4f, 0.25f) == 4f &&
                HostSettingMath.Step(float.PositiveInfinity, -1, 0.25f, 4f, 0.25f) == 0.25f,
                "a non-finite value steps to a finite bound");
            TestAssert.That(HostSettingMath.Step(1f, 0, 0f, 2f, 0.5f) == 1f &&
                HostSettingMath.Step(1f, 1, 0f, 2f, 0f) == 1f,
                "no direction or no increment changes nothing");
            TestAssert.That(HostSettingMath.Clamp(float.NaN, 1f, 2f) == 1f,
                "a non-finite value clamps to its floor");
        }

        private interface IExampleService { }
        private sealed class ExampleService : IExampleService { }
        private sealed class MissingService { }
    }
}
