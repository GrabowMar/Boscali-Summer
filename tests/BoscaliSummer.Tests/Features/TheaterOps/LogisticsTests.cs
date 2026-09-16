using BoscaliSummer.Features.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class LogisticsTests
    {
        public static void Run()
        {
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(true, 3000f, 2500f, 0f) == ReinforcementGate.Ready,
                "an allowed, off-cooldown, affordable call is ready");
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(true, 2500f, 2500f, 0f) == ReinforcementGate.Ready,
                "funds exactly covering the cost are enough");
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(true, 1000f, 2500f, 0f) == ReinforcementGate.Unaffordable,
                "a short pool cannot fund the call");
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(true, float.NaN, 2500f, 0f) == ReinforcementGate.Unaffordable,
                "an unreadable pool is never treated as affordable");
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(true, 3000f, 2500f, 12.5f) == ReinforcementGate.Cooling,
                "a running cooldown blocks the call");
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(true, 0f, 0f, -1f) == ReinforcementGate.Ready,
                "a negative cooldown reads as elapsed");
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(false, 3000f, 2500f, 0f) == ReinforcementGate.Disabled,
                "a disabled gate wins over affordability");
            TestAssert.That(
                ReinforcementGatePolicy.Evaluate(false, 3000f, 2500f, 12.5f) == ReinforcementGate.Disabled,
                "a disabled gate wins over cooldown");
        }
    }
}
