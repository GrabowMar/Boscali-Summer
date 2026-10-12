using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>What reads the sky summary (spec 8.2): <c>IWeatherView</c> gets the opacity-weighted cover and the native deck, and the vertical rain profile follows the rain layer.</summary>
    internal static class SkySummaryConsumerTests
    {
        public static void Run()
        {
            ViewSampleIsTheSummary();
            RainFallsFromTheRainLayer();
            EveryFieldPointMapsToAValidViewSample();
        }

        private static void ViewSampleIsTheSummary()
        {
            var point = new WeatherPoint { LowCover = 0.1f, SkyCover = 0.9f, CloudBase = 900f, CloudTop = 1500f, NativeDeck = 3300f, NativeDeckTop = 4600f, RainRate = 10f };
            WeatherViewSample day = SkySummary.View(point, false), night = SkySummary.View(point, true);
            TestAssert.That(day.Cover == 0.9f && day.CloudBase == 3300f && day.CloudTop == 4600f, "IWeatherView.Cover is SkyCover and the bounds are the native deck, not the low layer");
            TestAssert.Near(day.Rain01, 0.5f, "rain 10 mm/h is half of the 20 mm/h scale");
            TestAssert.That(!day.Night && night.Night, "the night flag passes through");
            var wild = new WeatherPoint { SkyCover = 3f, RainRate = -4f, NativeDeck = 100f, NativeDeckTop = 50f };
            WeatherViewSample clamped = SkySummary.View(wild, false);
            TestAssert.That(clamped.Cover == 1f && clamped.Rain01 == 0f && clamped.CloudTop >= clamped.CloudBase, "a sensor never sees a cover above 1, a negative rain or a deck with its top under its base");
        }

        private static void RainFallsFromTheRainLayer()
        {
            var embedded = new WeatherPoint { RainRate = 12f, CloudBase = 700f, CloudTop = 900f, RainBase = 3300f, RainTop = 4800f };
            TestAssert.That(FlightWeatherAirMass.VerticalRain(embedded, 1000f) == 1f, "below the altostratus of embedded showers the rain is full, though a low layer is far under it");
            TestAssert.That(FlightWeatherAirMass.VerticalRain(embedded, 5000f) == 0f, "above its top the air is dry");
            float mid = FlightWeatherAirMass.VerticalRain(embedded, 4050f);
            TestAssert.That(mid > 0.2f && mid < 0.8f, "inside the layer the rain fades smoothly (" + mid + ")");
            TestAssert.That(Math.Abs(FlightWeatherAirMass.VerticalRain(embedded, 3300f) - FlightWeatherAirMass.VerticalRain(embedded, 3300.01f)) < 0.001f, "and continuously at the base");
            var steady = new WeatherPoint { RainRate = 20f, CloudBase = 800f, CloudTop = 3000f, RainBase = 800f, RainTop = 3000f };
            TestAssert.That(FlightWeatherAirMass.Evaluate(steady, 500f, false, null).Rain == 1f && FlightWeatherAirMass.Evaluate(steady, 3000f, false, null).Rain == 0f, "a nimbostratus rains from its own base to its top");
        }

        private static void EveryFieldPointMapsToAValidViewSample()
        {
            for (int sky = 0; sky < SkyLibrary.Count; sky++)
            {
                var field = new WeatherField();
                field.Build(new WeatherKey(3u, 0f, false, (byte)SkyLibrary.ClassOf(sky), skyPin: (byte)sky), 600f, 40960f, 40960f);
                WeatherViewSample s = SkySummary.View(field.Sample(2000f, -1000f), false);
                TestAssert.That(s.Cover >= 0f && s.Cover <= 1f && s.CloudBase >= SkySummary.MinDeck && s.CloudTop >= s.CloudBase && !float.IsNaN(s.Rain01), "sky " + (sky + 1) + ": a valid sensor sample");
            }
        }
    }
}
