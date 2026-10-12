using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The ENV screen's METAR-style line (spec 8.4): the oktas rule, heights, rain and thunder, derived only from the layers.</summary>
    internal static class WeatherMetarTests
    {
        public static void Run()
        {
            OktasRule();
            HeightsAndWeather();
            LineIsDerivedFromTheLayers();
            EveryPinnedSkyHasALine();
        }

        private static void OktasRule()
        {
            TestAssert.That(WeatherMetar.Oktas(0f) == "" && WeatherMetar.Oktas(0.049f) == "", "under 0.05 is none");
            TestAssert.That(WeatherMetar.Oktas(0.05f) == "FEW" && WeatherMetar.Oktas(0.189f) == "FEW", "0.05 up to 0.19 is FEW");
            TestAssert.That(WeatherMetar.Oktas(0.19f) == "SCT" && WeatherMetar.Oktas(0.439f) == "SCT", "0.19 up to 0.44 is SCT");
            TestAssert.That(WeatherMetar.Oktas(0.44f) == "BKN" && WeatherMetar.Oktas(0.809f) == "BKN", "0.44 up to 0.81 is BKN");
            TestAssert.That(WeatherMetar.Oktas(0.81f) == "OVC" && WeatherMetar.Oktas(1f) == "OVC", "0.81 and up is OVC");
        }

        private static void HeightsAndWeather()
        {
            TestAssert.That(WeatherMetar.Height(457f) == "015" && WeatherMetar.Height(2590f) == "085" && WeatherMetar.Height(8800f) == "289", "metres to hundreds of feet");
            TestAssert.That(WeatherMetar.Height(-20f) == "000" && WeatherMetar.Height(99999f) == "999", "heights clamp to three digits");
            TestAssert.That(WeatherMetar.Rain(0.05f) == "" && WeatherMetar.Rain(0.1f) == "-RA" && WeatherMetar.Rain(2.4f) == "-RA" &&
                WeatherMetar.Rain(2.5f) == "RA" && WeatherMetar.Rain(9.9f) == "RA" && WeatherMetar.Rain(10f) == "+RA", "rain by rate");
        }

        private static void LineIsDerivedFromTheLayers()
        {
            TestAssert.That(WeatherMetar.Line(default) == "SKC", "no layer is a clear sky");
            var p = new WeatherPoint { Cover = 0.30f, CloudBase = 1500f, MidCover = 0.5f, MidBase = 4200f, HighCover = 0.9f, HighBase = 8800f };
            TestAssert.That(WeatherMetar.Line(p) == "SCT049 BKN138 OVC289", "three layers, lowest first: " + WeatherMetar.Line(p));
            p.MidCover = 0.02f; p.Cover = 0.04f;
            TestAssert.That(WeatherMetar.Line(p) == "OVC289", "a layer under 0.05 is not reported");
            p.RainRate = 12f; p.LightningRate = 0.5f;
            TestAssert.That(WeatherMetar.Line(p) == "OVC289 +RA TS", "weather follows the cloud groups: " + WeatherMetar.Line(p));
            var thin = new WeatherPoint { HighCover = 0.45f, HighBase = 8500f };
            TestAssert.That(WeatherMetar.Line(thin) == "SCT279" || WeatherMetar.Line(thin) == "BKN279", "a veil alone is one group: " + WeatherMetar.Line(thin));
        }

        private static void EveryPinnedSkyHasALine()
        {
            var field = new WeatherField();
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                field.Build(new WeatherKey(73u, 0f, false, skyPin: (byte)i), 100f, 40000f, 40000f);
                string line = WeatherMetar.Line(field.Sample(0f, 0f));
                string[] parts = line.Split(' ');
                TestAssert.That(line.Length > 0 && parts.Length <= 5, "sky " + (i + 1) + " reads '" + line + "'");
                if (field.RowAt(0f, 0f).Mid.Cover > 0.9f) TestAssert.That(line.Contains("OVC"), "sky " + (i + 1) + ": an opaque deck reads OVC, got " + line);
            }
        }
    }
}
