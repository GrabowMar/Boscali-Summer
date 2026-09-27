using System;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Fx;

namespace BoscaliSummer.Tests.Framework
{
    internal static class FxBusTests
    {
        public static void Run()
        {
            Budget_ClampsNegativeCeilings();
            RtBytes_CountsColourAndDepth();
            ColourBits_MapsKnownFormats();
            Quality_MapsLevelThirds();
            Scales_AreFullOnlyOnHigh();
        }

        private static void Budget_ClampsNegativeCeilings()
        {
            var budget = new FxBudget(-1, -2, -3, true);
            TestAssert.That(budget.MaxRtBytes == 0, "RT ceiling must clamp at 0");
            TestAssert.That(budget.MaxPasses == 0, "pass ceiling must clamp at 0");
            TestAssert.That(budget.MaxVoices == 0, "voice ceiling must clamp at 0");
            TestAssert.That(budget.ScalesWithQuality, "quality scaling flag must survive");
        }

        private static void RtBytes_CountsColourAndDepth()
        {
            // Canopy pane: 512x512 ARGB32, no depth = 1 MB exactly.
            TestAssert.That(FxBudget.RtBytes(512, 512, 32, 0) == 1024 * 1024,
                "a 512 ARGB32 pane costs 1 MB");
            TestAssert.That(FxBudget.RtBytes(512, 256, 32, 16) == 512 * 256 * 4 + 512 * 256 * 2,
                "depth bits add their own bytes");
            TestAssert.That(FxBudget.RtBytes(0, 64, 32, 0) == 0, "empty targets cost nothing");
        }

        private static void ColourBits_MapsKnownFormats()
        {
            TestAssert.That(FxBudget.ColourBits("ARGB32") == 32, "ARGB32 is 32-bit");
            TestAssert.That(FxBudget.ColourBits("ARGBHalf") == 64, "half float is 64-bit");
            TestAssert.That(FxBudget.ColourBits("ARGBFloat") == 128, "full float is 128-bit");
            TestAssert.That(FxBudget.ColourBits("R8") == 8, "R8 is 8-bit");
            TestAssert.That(FxBudget.ColourBits(null) == 32, "unknown formats default to 32-bit");
        }

        private static void Quality_MapsLevelThirds()
        {
            TestAssert.That(FxQualityMapper.Map(0, 6) == FxQuality.Low, "bottom level is Low");
            TestAssert.That(FxQualityMapper.Map(5, 6) == FxQuality.High, "top level is High");
            TestAssert.That(FxQualityMapper.Map(2, 6) == FxQuality.Medium, "middle level is Medium");
            TestAssert.That(FxQualityMapper.Map(0, 1) == FxQuality.High, "a single level never degrades");
            TestAssert.That(FxQualityMapper.Map(0, 0) == FxQuality.High, "missing levels never degrade");
        }

        private static void Scales_AreFullOnlyOnHigh()
        {
            FxScales high = FxQualityMapper.ScalesFor(FxQuality.High);
            TestAssert.That(Math.Abs(high.Particles - 1f) < 1e-6f, "High keeps full budgets");
            FxScales low = FxQualityMapper.ScalesFor(FxQuality.Low);
            TestAssert.That(low.RenderTargets < 1f && low.Particles < 1f &&
                low.Sway < 1f && low.Voices < 1f, "Low scales every budget down");
            FxScales medium = FxQualityMapper.ScalesFor(FxQuality.Medium);
            TestAssert.That(medium.Particles > low.Particles && medium.Particles < high.Particles,
                "Medium sits between Low and High");
        }
    }
}
