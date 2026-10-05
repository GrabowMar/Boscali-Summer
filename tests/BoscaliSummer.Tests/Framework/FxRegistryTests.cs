using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;

namespace BoscaliSummer.Tests.Framework
{
    internal static class FxRegistryTests
    {
        public static void Run()
        {
            var registry = new FxRegistry(2);
            var released = new List<string>();
            var first = new Fixture("first", released);
            var duplicate = new Fixture("first", released);
            var second = new Fixture("second", released) { ThrowOnDescribe = true, ThrowOnRelease = true };
            TestAssert.That(registry.TryRegister(first), "first effect registers");
            TestAssert.That(registry.TryRegister(first), "same instance registers idempotently");
            TestAssert.That(!registry.TryRegister(duplicate), "duplicate id cannot replace its owner");
            TestAssert.That(registry.TryRegister(second), "second effect registers");
            TestAssert.That(!registry.TryRegister(new Fixture("third", released)), "registry cap is enforced");
            TestAssert.That(!registry.TryRegister(new Fixture("Bad ID", released)), "effect ids are stable lowercase keys");

            var state = new Dictionary<string, object>();
            registry.Describe(state);
            TestAssert.That((string)state["fx.first"] == "ready", "healthy diagnostics survive a neighbor failure");
            TestAssert.That((string)state["fxFault.second"] == "InvalidOperationException", "diagnostic failure is visible");
            TestAssert.That(registry.ReleaseAll() == 1, "teardown counts isolated failures");
            TestAssert.That(registry.Count == 0 && released.Count == 2 &&
                released[0] == "second" && released[1] == "first", "teardown runs in reverse order and clears the registry");

            TestAssert.That(registry.TryRegister(first), "registry is reusable after teardown");
            TestAssert.That(!registry.Unregister(duplicate), "a stale instance cannot remove a newer owner");
            TestAssert.That(registry.Unregister(first) && registry.Count == 0, "owner unregisters itself");
        }

        private sealed class Fixture : IClientEffect
        {
            private readonly List<string> released;
            public Fixture(string id, List<string> released) { EffectId = id; this.released = released; }
            public string EffectId { get; }
            public FxBudget Budget => new FxBudget(0, 0, 0, false);
            public bool ThrowOnDescribe { get; set; }
            public bool ThrowOnRelease { get; set; }
            public void DescribeFx(IDictionary<string, object> state)
            {
                if (ThrowOnDescribe) throw new InvalidOperationException();
                state["fx." + EffectId] = "ready";
            }
            public void ReleaseFx()
            {
                released.Add(EffectId);
                if (ThrowOnRelease) throw new InvalidOperationException();
            }
        }
    }
}
