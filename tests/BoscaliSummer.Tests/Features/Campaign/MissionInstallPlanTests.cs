using BoscaliSummer.Features.Campaign.Domain;

namespace BoscaliSummer.Tests.Features.Campaign
{
    internal static class MissionInstallPlanTests
    {
        public static void Run()
        {
            TestAssert.That(MissionInstallPlan.Decide(false, false, 0) == MissionInstallAction.Install &&
                MissionInstallPlan.Decide(false, true, 1) == MissionInstallAction.Install,
                "A missing campaign mission is written whatever the marker says");

            TestAssert.That(MissionInstallPlan.Decide(true, true, MissionInstallPlan.Revision) == MissionInstallAction.Skip &&
                MissionInstallPlan.Decide(true, true, MissionInstallPlan.Revision + 40) == MissionInstallAction.Skip,
                "The shipped revision is left alone and a newer marker is never downgraded");

            TestAssert.That(MissionInstallPlan.Decide(true, true, MissionInstallPlan.Revision - 1) == MissionInstallAction.Update &&
                MissionInstallPlan.Decide(true, true, 0) == MissionInstallAction.Update,
                "The mod's own older copy is refreshed");

            TestAssert.That(MissionInstallPlan.Decide(true, false, 0) == MissionInstallAction.Foreign,
                "A mission of the same name that Boscali did not write is never overwritten");
        }
    }
}
