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

            TestAssert.That(ClientTuningMath.PlanLodBias(1f, true) == ClientTuningMath.LodBiasFloor,
                "the floor raises a stock LOD bias");
            TestAssert.That(ClientTuningMath.PlanLodBias(2f, true) == 2f,
                "the floor never lowers an already-cheap bias");
            TestAssert.That(ClientTuningMath.PlanLodBias(1f, false) == 1f,
                "a disabled floor leaves the game value alone");

            TestAssert.That(ClientTuningMath.PlanShadowDistance(4000f, true) == ClientTuningMath.ShadowDistanceCapMeters,
                "the cap trims a long shadow distance");
            TestAssert.That(ClientTuningMath.PlanShadowDistance(1500f, true) == 1500f,
                "the cap never raises a short shadow distance");
            TestAssert.That(ClientTuningMath.PlanShadowDistance(0f, true) == 0f,
                "the cap leaves disabled shadows alone");
            TestAssert.That(ClientTuningMath.PlanShadowDistance(4000f, false) == 4000f,
                "a disabled cap leaves the game value alone");

            TestAssert.That(ClientTuningMath.PlanFrameRate(-1, true) == ClientTuningMath.FrameRateCapFps,
                "the cap applies over the platform default");
            TestAssert.That(ClientTuningMath.PlanFrameRate(120, false) == 120,
                "a disabled cap leaves the game value alone");
            TestAssert.That(ClientTuningMath.PlanFrameRate(30, true) == 30,
                "the cap never raises an existing lower frame limit");
            TestAssert.That(ClientTuningMath.PlanFrameRate(120, true) == 60 &&
                ClientTuningMath.PlanFrameRate(0, true) == 60,
                "the cap trims high and platform-default frame limits");

            TestAssert.That(ClientTuningMath.RestoreLodBias(1f, ClientTuningMath.PlanLodBias(1f, true)) == 1f &&
                ClientTuningMath.RestoreShadowDistance(4000f, ClientTuningMath.PlanShadowDistance(4000f, true)) == 4000f &&
                ClientTuningMath.RestoreFrameRate(-1, ClientTuningMath.PlanFrameRate(-1, true)) == -1,
                "a reset restores the baseline before the next scene captures it");
            TestAssert.That(ClientTuningMath.RestoreLodBias(1f, 2f) == 2f &&
                ClientTuningMath.RestoreShadowDistance(4000f, 1500f) == 1500f &&
                ClientTuningMath.RestoreFrameRate(-1, 30) == 30,
                "restoration preserves a new game graphics preset");
        }
    }
}
