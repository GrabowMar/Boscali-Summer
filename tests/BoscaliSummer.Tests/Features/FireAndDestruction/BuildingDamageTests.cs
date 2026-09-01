using BoscaliSummer.Fire;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class BuildingDamageTests
    {
        public static void Run()
        {
            TestAssert.That(
                BuildingDamagePolicy.FromHitPoints(71f, 100f) == BuildingDamageStage.Intact,
                "healthy building entered a damage stage");
            TestAssert.That(
                BuildingDamagePolicy.FromHitPoints(70f, 100f) == BuildingDamageStage.Minor,
                "minor HP threshold changed");
            TestAssert.That(
                BuildingDamagePolicy.FromHitPoints(40f, 100f) == BuildingDamageStage.Major,
                "major HP threshold changed");
            TestAssert.That(
                BuildingDamagePolicy.FromHitPoints(15f, 100f) == BuildingDamageStage.Critical,
                "critical HP threshold changed");
            TestAssert.That(
                BuildingDamagePolicy.FromHitPoints(100f, 200f) == BuildingDamageStage.Minor,
                "normalized HP must respect tougher building baselines");
            TestAssert.That(
                BuildingDamagePolicy.FromHitPoints(55f, 55f) == BuildingDamageStage.Minor,
                "minimum baseline must preserve damage for an already weakened building");

            TestAssert.That(
                BuildingDamagePolicy.FromFireProgress(0f) == BuildingDamageStage.Minor,
                "new fire must establish minor damage");
            TestAssert.That(
                BuildingDamagePolicy.FromFireProgress(0.33f) == BuildingDamageStage.Major,
                "mid-fire threshold changed");
            TestAssert.That(
                BuildingDamagePolicy.FromFireProgress(0.66f) == BuildingDamageStage.Critical,
                "late-fire threshold changed");

            TestAssert.That(
                BuildingDamagePolicy.Severity(BuildingDamageStage.Minor) <
                BuildingDamagePolicy.Severity(BuildingDamageStage.Major) &&
                BuildingDamagePolicy.Severity(BuildingDamageStage.Major) <
                BuildingDamagePolicy.Severity(BuildingDamageStage.Critical),
                "damage severities must remain monotonic");
            TestAssert.That(
                BuildingDamagePolicy.FromSeverity(0.46f) == BuildingDamageStage.Minor &&
                BuildingDamagePolicy.FromSeverity(0.70f) == BuildingDamageStage.Major &&
                BuildingDamagePolicy.FromSeverity(0.92f) == BuildingDamageStage.Critical,
                "wire severity no longer reconstructs its stage");
        }
    }
}
