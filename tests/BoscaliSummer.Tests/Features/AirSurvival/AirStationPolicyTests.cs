using BoscaliSummer.Features.AirSurvival.Domain;

namespace BoscaliSummer.Tests.Features.AirSurvival
{
    internal static class AirStationPolicyTests
    {
        public static void Run()
        {
            TestAssert.That(AirStationPolicy.ChooseObjectiveRank(2, 1000, 2000, 3000) == 2,
                "Nearby objectives spread aircraft by identity");
            TestAssert.That(AirStationPolicy.ChooseObjectiveRank(2, 1000, 30000, 40000) == 0,
                "Distant objectives do not pull aircraft away from local fighting");
            TestAssert.That(AirStationPolicy.TryOperationFix(0, 100, 200, 3000,
                out float x, out float z) && x == 1150 && z == 1250,
                "Operation stations spread assigned aircraft within a bounded footprint");
            TestAssert.That(!AirStationPolicy.TryOperationFix(1, 100, 200, 3000,
                out _, out _), "At least half of idle aircraft keep autonomous patrols");
            TestAssert.That(!AirStationPolicy.TryOperationFix(0, float.NaN, 200, 3000,
                out _, out _), "Invalid station positions fail closed");
            TestAssert.That(AirFlarePolicy.ShouldPop(1200f, 600f, .5f, false, 5f),
                "An imminent closing IR threat permits one emergency flare");
            TestAssert.That(!AirFlarePolicy.ShouldPop(1200f, 600f, .5f, true, 5f) &&
                !AirFlarePolicy.ShouldPop(1200f, 600f, .5f, false, 1f),
                "Native firing and cooldown prevent duplicate flares");
            TestAssert.That(!AirFlarePolicy.ShouldPop(1200f, -600f, .5f, false, 5f) &&
                !AirFlarePolicy.ShouldPop(6000f, 600f, .5f, false, 5f) &&
                !AirFlarePolicy.ShouldPop(1200f, 600f, 0f, false, 5f),
                "Receding, distant, and empty-flare threats leave native AI alone");
        }
    }
}
