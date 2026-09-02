using BoscaliSummer.Features.Progression.Runtime;

namespace BoscaliSummer.Tests.Features.Progression
{
    internal static class ProgressionTests
    {
        public static void Run()
        {
            var state = new ProgressionState();
            TestAssert.That(state.AvailablePoints(0) == 0, "starting rank granted a point");
            TestAssert.That(state.AvailablePoints(1) == 1, "rank one did not grant one point");
            TestAssert.That(!state.TryUnlock(SkillId.FireMission, 1), "locked high-rank skill was accepted");
            TestAssert.That(state.TryUnlock(SkillId.VehicleRequisition, 1), "rank-one support unlock failed");
            TestAssert.That(state.AvailablePoints(1) == 0, "spent point remained available");
            TestAssert.That(!state.TryUnlock(SkillId.CombatEngineering, 1), "rank gate was ignored");
            TestAssert.That(state.TryUnlock(SkillId.CombatEngineering, 2), "valid prerequisite chain failed");
            TestAssert.That(!state.TryUnlock(SkillId.CombatEngineering, 3), "duplicate skill was accepted");

            var fuel = new ProgressionState();
            TestAssert.That(!fuel.TryUnlock(SkillId.FuelConservation2, 3), "prerequisite was ignored");
            TestAssert.That(fuel.TryUnlock(SkillId.FuelConservation1, 1), "fuel tier one failed");
            TestAssert.That(fuel.TryUnlock(SkillId.FuelConservation2, 2), "fuel tier two failed");
            TestAssert.That(fuel.SpentPoints == 2, "skill mask point count is wrong");
        }
    }
}
