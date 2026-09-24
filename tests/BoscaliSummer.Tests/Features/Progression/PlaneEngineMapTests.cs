using BoscaliSummer.Features.Progression.Domain;

namespace BoscaliSummer.Tests
{
    internal static class PlaneEngineMapTests
    {
        internal static void Run()
        {
            TestAssert.That(PlaneEngineMap.IsDefined(0) && PlaneEngineMap.IsDefined(1) &&
                !PlaneEngineMap.IsDefined(2), "engine map wire ids are bounded");
            TestAssert.That(PlaneEngineMap.FuelFactor(1) < PlaneEngineMap.FuelFactor(0) &&
                PlaneEngineMap.ThrottleCeiling(1) < PlaneEngineMap.ThrottleCeiling(0),
                "range map saves fuel and gives up throttle");
        }
    }
}
