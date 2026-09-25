using System;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class WeatherForecastTests
    {
        public static void Run()
        {
            // 1. Forecast sampling checks
            int seed = 42;
            float baseConditions = 0.35f;
            float baseDeck = 3000f;

            ForecastStep step0 = WeatherForecast.SampleForecast(baseConditions, baseDeck, seed, elapsedSeconds: 0f, offsetMinutes: 0);
            ForecastStep step15 = WeatherForecast.SampleForecast(baseConditions, baseDeck, seed, elapsedSeconds: 0f, offsetMinutes: 15);
            ForecastStep step60 = WeatherForecast.SampleForecast(baseConditions, baseDeck, seed, elapsedSeconds: 0f, offsetMinutes: 60);

            TestAssert.That(step0.OffsetMinutes == 0, "Step 0 offset must match");
            TestAssert.That(step15.OffsetMinutes == 15, "Step 15 offset must match");
            TestAssert.That(step60.OffsetMinutes == 60, "Step 60 offset must match");

            TestAssert.That(step0.Conditions >= 0f && step0.Conditions <= 1f, "Conditions within [0,1]");
            TestAssert.That(step15.Conditions >= 0f && step15.Conditions <= 1f, "Conditions within [0,1]");
            TestAssert.That(step60.Conditions >= 0f && step60.Conditions <= 1f, "Conditions within [0,1]");

            TestAssert.That(step0.CloudDeckMetres >= 800f, "Cloud deck has minimum ceiling floor");
            TestAssert.That(step15.CloudDeckMetres >= 800f, "Cloud deck has minimum ceiling floor");

            // Deterministic reproduction with same seed & time
            ForecastStep repeatStep = WeatherForecast.SampleForecast(baseConditions, baseDeck, seed, elapsedSeconds: 0f, offsetMinutes: 15);
            TestAssert.That(Math.Abs(repeatStep.Conditions - step15.Conditions) < 0.0001f, "Forecast must be deterministic");

            // 2. Solar calculations
            // Noon sun directly overhead in equinox: sun direction = (0, 1, 0), axis = (0, 0, 1)
            SolarData noon = WeatherForecast.ComputeSolarData(0f, 1f, 0f, 0f, 0f, 1f, timeOfDayHours: 12f);
            TestAssert.That(Math.Abs(noon.ElevationDegrees - 90f) < 1f, "Overhead sun must be ~90 deg elevation");
            TestAssert.That(noon.SunriseHour >= 0f && noon.SunsetHour >= 0f, "Equatorial equinox must have sunrise & sunset");
            TestAssert.That(!noon.PolarDay && !noon.PolarNight, "Equinox is not polar day or night");

            // Midnight sun test (polar day): sun is elevated above horizon all day
            // Axis pointing towards pole with high summer sun
            SolarData polar = WeatherForecast.ComputeSolarData(0f, 0.5f, 0.5f, 0f, 1f, 0f, timeOfDayHours: 12f);
            TestAssert.That(polar.PolarDay, "Continuous elevation must detect polar day");

            // 3. Lunar calculations
            LunarData newMoon = WeatherForecast.ComputeLunarData(0.0f);
            TestAssert.That(newMoon.PhaseName == "New Moon", "Phase 0.0 must be New Moon");
            TestAssert.That(newMoon.IsMoonless, "New Moon is moonless");
            TestAssert.That(newMoon.IlluminationFraction < 0.01f, "New Moon illumination near 0");

            LunarData fullMoon = WeatherForecast.ComputeLunarData(0.5f);
            TestAssert.That(fullMoon.PhaseName == "Full Moon", "Phase 0.5 must be Full Moon");
            TestAssert.That(!fullMoon.IsMoonless, "Full Moon is not moonless");
            TestAssert.That(Math.Abs(fullMoon.IlluminationFraction - 1.0f) < 0.01f, "Full Moon illumination ~1.0");

            LunarData firstQuarter = WeatherForecast.ComputeLunarData(0.25f);
            TestAssert.That(firstQuarter.PhaseName == "First Quarter", "Phase 0.25 must be First Quarter");
            TestAssert.That(Math.Abs(firstQuarter.IlluminationFraction - 0.5f) < 0.05f, "Quarter moon is ~50% illuminated");

            // 4. Wind formatting
            // Wind blowing East at 10 m/s: vx = 10, vz = 0
            WeatherForecast.FormatWind(10f, 0f, out float speedKts, out int towardsDeg, out int fromDeg);
            TestAssert.That(Math.Abs(speedKts - 19.44f) < 0.1f, "10 m/s is ~19.4 kts");
            TestAssert.That(towardsDeg == 90, "Wind blowing east has heading 90");
            TestAssert.That(fromDeg == 270, "Wind blowing east comes from west (270)");
        }
    }
}
