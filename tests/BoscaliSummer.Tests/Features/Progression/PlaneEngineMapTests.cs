using BoscaliSummer.Modules.Progression.Domain;

namespace BoscaliSummer.Tests
{
    internal static class PlaneEngineMapTests
    {
        internal static void Run()
        {
            TestAssert.That(PlaneEngineMap.IsDefined(0) && PlaneEngineMap.IsDefined(1) &&
                !PlaneEngineMap.IsDefined(2), "engine map wire ids are bounded");
            TestAssert.That(PlaneEngineMap.FuelFactor(0) == 1f && PlaneEngineMap.ThrottleCeiling(0) == 1f,
                "stock map preserves native fuel draw and throttle");
            TestAssert.That(PlaneEngineMap.FuelFactor(1) == .90f && PlaneEngineMap.ThrottleCeiling(1) == .85f,
                "range map uses 90 percent fuel draw and caps throttle at 85 percent");
            TestAssert.That(!PlaneEngineMap.IsDefined(255) && PlaneEngineMap.FuelFactor(255) == 1f &&
                PlaneEngineMap.ThrottleCeiling(255) == 1f,
                "an invalid wire id safely falls back to stock");
        }
    }
}
