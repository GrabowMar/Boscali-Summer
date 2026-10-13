using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Performance.Domain;
using BoscaliSummer.Tests.Framework;

namespace BoscaliSummer.Tests.Features.Performance
{
    internal static class ClientTuningTests
    {
        internal static void Run()
        {
            TestAssert.That(FxBudget.ScaleCount(32, 1f) == 32, "full quality keeps the full cap");
            TestAssert.That(FxBudget.ScaleCount(32, 0.5f) == 16, "low quality halves the cap");
            TestAssert.That(FxBudget.ScaleCount(24, 0.75f) == 18, "medium quality scales the cap");
            TestAssert.That(FxBudget.ScaleCount(2, 0.5f) == 1, "a drain never drops to zero");
            TestAssert.That(FxBudget.ScaleCount(4, 0f) == 1, "a zero scale keeps one slot");
            TestAssert.That(FxBudget.ScaleCount(64, 1.5f) == 64, "scales above full do not grow caps");
            TestAssert.That(FxBudget.ScaleCount(0, 0.5f) == 0, "a disabled budget stays disabled");

            TestAssert.That(ClientTuningMath.TracerKeep(1000f * 1000f, false) == 1f &&
                ClientTuningMath.TracerKeep(3000f * 3000f, true) == 1f &&
                ClientTuningMath.TracerKeep(3000f * 3000f, false) == 0.5f &&
                ClientTuningMath.TracerKeep(8000f * 8000f, true) == 0.5f &&
                ClientTuningMath.TracerKeep(16000f * 16000f, true) == 0f,
                "near bullets all draw, far ones thin out, tracers last longest");
            TestAssert.That(!ClientTuningMath.ShouldSleep(14500f * 14500f, false) &&
                ClientTuningMath.ShouldSleep(14500f * 14500f, true) &&
                ClientTuningMath.ShouldSleep(15000f * 15000f, false) &&
                !ClientTuningMath.ShouldSleep(13900f * 13900f, true),
                "a far unit sleeps at 15 km and stays asleep until 14 km");

            TestAssert.That(ClientTuningMath.PlanLodBias(4f, true) == ClientTuningMath.LodBiasCap,
                "the cap lowers the game's stock LOD bias");
            TestAssert.That(ClientTuningMath.PlanLodBias(1f, true) == 1f,
                "the cap never raises an already-cheap bias");
            TestAssert.That(ClientTuningMath.PlanLodBias(4f, false) == 4f,
                "a disabled cap leaves the game value alone");

            TestAssert.That(ClientTuningMath.PlanShadowDistance(4000f, true) == ClientTuningMath.ShadowDistanceCapMeters,
                "the cap trims a long shadow distance");
            TestAssert.That(ClientTuningMath.PlanShadowDistance(800f, true) == 800f,
                "the cap never raises a short shadow distance");
            TestAssert.That(ClientTuningMath.PlanShadowDistance(0f, true) == 0f,
                "the cap leaves disabled shadows alone");
            TestAssert.That(ClientTuningMath.PlanShadowDistance(4000f, false) == 4000f,
                "a disabled cap leaves the game value alone");

            TestAssert.That(ClientTuningMath.RestoreLodBias(4f, ClientTuningMath.PlanLodBias(4f, true)) == 4f &&
                ClientTuningMath.RestoreShadowDistance(4000f, ClientTuningMath.PlanShadowDistance(4000f, true)) == 4000f,
                "a reset restores the baseline before the next scene captures it");
            TestAssert.That(ClientTuningMath.RestoreLodBias(4f, 2f) == 2f &&
                ClientTuningMath.RestoreShadowDistance(4000f, 800f) == 800f,
                "restoration preserves a new game graphics preset");
        }
    }
}
