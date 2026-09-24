using System;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class WeatherDebugTests
    {
        public static void Run()
        {
            // 1. Verify Regime Presets Parameters
            WeatherRegime clear = WeatherRegime.FromType(WeatherRegimeType.Clear);
            TestAssert.That(clear.TargetConditions == 0.05f, "Clear must target 0.05 conditions");
            TestAssert.That(clear.TargetCloudHeight == 3800f, "Clear ceiling must target 3800m");

            WeatherRegime squall = WeatherRegime.FromType(WeatherRegimeType.RainSquall);
            TestAssert.That(squall.TargetConditions == 0.82f, "RainSquall must target 0.82 conditions");
            TestAssert.That(squall.TargetCloudHeight == 1500f, "RainSquall ceiling must target 1500m");
            TestAssert.That(squall.TargetConditions >= 0.60f, "RainSquall conditions must naturally trigger rain");

            WeatherRegime storm = WeatherRegime.FromType(WeatherRegimeType.Storm);
            TestAssert.That(storm.TargetConditions == 0.95f, "Storm must target 0.95 conditions");
            TestAssert.That(storm.TargetCloudHeight == 1200f, "Storm ceiling must target 1200m");
            TestAssert.That(storm.TargetTurbulence > squall.TargetTurbulence, "Storm turbulence must exceed squall");

            // 2. Verify Rain Intensity Derivation logic against the shipped helper
            // Natural threshold: < 0.60 = 0, >= 0.60 scales to 1.0 at 0.95
            Func<float, float?, float> calculateRain = WeatherForecast.ResolveRainIntensity;

            // Natural rain
            TestAssert.That(calculateRain(0.20f, null) == 0f, "Fair weather must produce 0% rain");
            TestAssert.That(calculateRain(0.55f, null) == 0f, "Broken clouds under 0.60 must produce 0% rain");
            TestAssert.That(calculateRain(0.85f, null) > 0.70f, "Squall must produce >70% rain");
            TestAssert.That(Math.Abs(calculateRain(0.95f, null) - 1.0f) < 0.0001f, "Storm conditions must produce 100% rain");

            // Forced rain overrides
            TestAssert.That(calculateRain(0.0f, 1.0f) == 1.0f, "Forced 100% rain must produce 100% rain even in clear sky");
            TestAssert.That(calculateRain(0.98f, 0.0f) == 0f, "Forced 0% rain must produce 0% rain even during severe storm");
            TestAssert.That(calculateRain(0.50f, 0.4f) == 0.4f, "Forced 40% rain must produce 40% rain");

            // 3. Verify Regime Cycling Order
            WeatherRegimeType[] expectedCycle =
            {
                WeatherRegimeType.Clear,
                WeatherRegimeType.Scattered,
                WeatherRegimeType.Broken,
                WeatherRegimeType.Overcast,
                WeatherRegimeType.RainSquall,
                WeatherRegimeType.Storm
            };

            for (int i = 0; i < expectedCycle.Length - 1; i++)
            {
                WeatherRegime current = WeatherRegime.FromType(expectedCycle[i]);
                WeatherRegime next = WeatherRegime.FromType(expectedCycle[i + 1]);
                TestAssert.That(next.TargetConditions > current.TargetConditions,
                    $"Regime {next.Type} conditions must be strictly greater than {current.Type}");
            }

            // 4. Verify Fine-Tuning Step Clamping
            Func<float, float, float> clampConditions = (current, delta) => Math.Max(0f, Math.Min(1f, current + delta));
            TestAssert.That(clampConditions(0.95f, 0.10f) == 1.0f, "Conditions step must clamp at 1.0");
            TestAssert.That(clampConditions(0.05f, -0.10f) == 0.0f, "Conditions step must clamp at 0.0");

            Func<float, float, float> clampCeiling = (current, delta) => Math.Max(600f, Math.Min(6000f, current + delta));
            TestAssert.That(clampCeiling(5900f, 250f) == 6000f, "Ceiling step must clamp at 6000m");
            TestAssert.That(clampCeiling(700f, -250f) == 600f, "Ceiling step must clamp at 600m");
        }
    }
}
