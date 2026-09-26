using System;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class WeatherRegimeTests
    {
        public static void Run()
        {
            // Verify regime mapping from conditions float
            TestAssert.That(RegimeSnapshot.FromConditions(0.0f).Type == WeatherRegimeType.Clear, "0.0 must be Clear");
            TestAssert.That(RegimeSnapshot.FromConditions(0.08f).Type == WeatherRegimeType.Clear, "0.08 must be Clear");
            TestAssert.That(RegimeSnapshot.FromConditions(0.20f).Type == WeatherRegimeType.Fair, "0.20 must be Fair");
            TestAssert.That(RegimeSnapshot.FromConditions(0.40f).Type == WeatherRegimeType.Scattered, "0.40 must be Scattered");
            TestAssert.That(RegimeSnapshot.FromConditions(0.55f).Type == WeatherRegimeType.Broken, "0.55 must be Broken");
            TestAssert.That(RegimeSnapshot.FromConditions(0.70f).Type == WeatherRegimeType.Overcast, "0.70 must be Overcast");
            TestAssert.That(RegimeSnapshot.FromConditions(0.85f).Type == WeatherRegimeType.RainSquall, "0.85 must be RainSquall");
            TestAssert.That(RegimeSnapshot.FromConditions(0.95f).Type == WeatherRegimeType.Storm, "0.95 must be Storm");
            TestAssert.That(RegimeSnapshot.FromConditions(1.0f).Type == WeatherRegimeType.Storm, "1.0 must be Storm");

            // Verify boundary conditions clamp correctly
            TestAssert.That(RegimeSnapshot.FromConditions(-1.0f).Type == WeatherRegimeType.Clear, "Negative condition must clamp to Clear");
            TestAssert.That(RegimeSnapshot.FromConditions(2.5f).Type == WeatherRegimeType.Storm, "Over 1.0 condition must clamp to Storm");

            // Verify regime target progression
            RegimeSnapshot clear = RegimeSnapshot.FromType(WeatherRegimeType.Clear);
            RegimeSnapshot storm = RegimeSnapshot.FromType(WeatherRegimeType.Storm);

            TestAssert.That(clear.TargetConditions < storm.TargetConditions, "Storm conditions must exceed Clear");
            TestAssert.That(clear.TargetCloudHeight > storm.TargetCloudHeight, "Clear ceiling must be higher than Storm ceiling");
            TestAssert.That(clear.TargetTurbulence < storm.TargetTurbulence, "Storm turbulence must exceed Clear");
            TestAssert.That(!string.IsNullOrEmpty(clear.TacticalBriefing), "Tactical briefing must not be empty");
            TestAssert.That(!string.IsNullOrEmpty(storm.TacticalBriefing), "Tactical briefing must not be empty");
        }
    }
}
