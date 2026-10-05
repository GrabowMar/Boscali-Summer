using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Tests;

namespace BoscaliSummer.Tests;

internal static class FxBudgetFormatsTests
{
    public static void Run()
    {
        TestAssert.That(FxBudget.ColourBits("RGFloat") == 64, "cloud distance MRT is 64 bits, not 128");
        TestAssert.That(FxBudget.ColourBits("RFloat") == 32, "one float channel");
        TestAssert.That(FxBudget.ColourBits("ARGBFloat") == 128, "four float channels");
        TestAssert.That(FxBudget.ColourBits("RHalf") == 16, "one half channel");
        TestAssert.That(FxBudget.ColourBits("RGHalf") == 32, "two half channels");
        TestAssert.That(FxBudget.ColourBits("ARGBHalf") == 64, "four half channels");
        TestAssert.That(FxBudget.ColourBits("R8") == 8, "single byte channel");
        TestAssert.That(FxBudget.RtBytes(960, 540, FxBudget.ColourBits("RGFloat"), 0) == 4147200, "1080p half-resolution cloud depth bytes");
    }
}
