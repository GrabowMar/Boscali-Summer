using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The ForceWeather mapping of spec 10.4: the sims' old arguments name a held, pinned tape.</summary>
    internal static class ForceWeatherMapTests
    {
        public static void Run()
        {
            RegimePinsTheFirstSkyOfTheClass();
            ConditionsNameTheClassWhenThereIsNoRegime();
            OtherArgumentsLandInTheKey();
            TheFrameAndTheSetsSurvive();
            EveryClassBuildsAValidField();
        }

        private static WeatherKey Basis() =>
            new WeatherKey(77u, 100f, true, 2, sets: Superstructures.SupercellSet, layoutSalt: 5, hasAnchor: true, anchorX: 1000f, anchorZ: -2000f, openingBase: 1500f, headingDeg: 233f, offset: 55000f, systemSpeed: 31f);

        private static void RegimePinsTheFirstSkyOfTheClass()
        {
            byte[] firsts = { 0, 7, 14, 21, 28, 34, 41 };   // skies 1, 8, 15, 22, 29, 35, 42
            for (int c = 0; c < 7; c++)
            {
                TestAssert.That(ForceWeatherMap.FirstSky((WeatherRegimeType)c) == firsts[c], "the first sky of class " + (WeatherRegimeType)c + " is sky " + (firsts[c] + 1));
                WeatherKey key = ForceWeatherMap.Key(Basis(), 500f, (WeatherRegimeType)c, 0.92f, null, null, null);
                TestAssert.That(key.SkyPin == firsts[c] && key.MissionClass == c && key.Story == WeatherKey.None && !key.Dynamic, "a regime pins its class and holds the tape (class " + c + ")");
            }
        }

        private static void ConditionsNameTheClassWhenThereIsNoRegime()
        {
            TestAssert.That(ForceWeatherMap.ClassFor(null, 0.92f, Basis()) == WeatherRegimeType.Storm, "0.92 is a storm");
            TestAssert.That(ForceWeatherMap.ClassFor(null, 0.05f, Basis()) == WeatherRegimeType.Clear, "0.05 is clear");
            TestAssert.That(ForceWeatherMap.ClassFor(null, null, Basis()) == WeatherRegimeType.Scattered, "no regime and no conditions keep the live mission class");
            TestAssert.That(ForceWeatherMap.ClassFor(null, float.NaN, null) == WeatherRegimeType.Fair, "garbage with no basis is Fair");
            TestAssert.That(ForceWeatherMap.ClassFor(WeatherRegimeType.Broken, 0.92f, Basis()) == WeatherRegimeType.Broken, "a regime beats the conditions");
        }

        private static void OtherArgumentsLandInTheKey()
        {
            WeatherKey key = ForceWeatherMap.Key(Basis(), 500f, null, 0.74f, 4500f, 1234u, 780f);
            TestAssert.That(key.OpeningBase == 4500f && key.Seed == 1234u, "cloudHeight is OpeningBase and seed is Seed");
            TestAssert.That(Math.Abs(key.Offset - 31f * 780f) < 0.5f && !key.Dynamic && key.Epoch == 500f, "modelAge is Offset = v x age with the tape held");
            TestAssert.That(ForceWeatherMap.Key(Basis(), 500f, null, 0.74f, 1800f, null, null).OpeningBase == 0f, "the editor default altitude is no altitude (OpeningBase 0)");
            TestAssert.That(ForceWeatherMap.Key(Basis(), 500f, null, null, null, null, null).OpeningBase == 1500f && ForceWeatherMap.Key(Basis(), 500f, null, null, null, null, null).Offset == 55000f, "absent arguments keep the live key's values");
            TestAssert.That(ForceWeatherMap.Key(Basis(), 500f, null, null, null, null, 1e9f).Offset == 31f * ForceWeatherMap.MaxAgeSeconds, "a huge model age is clamped to four hours");
        }

        private static void TheFrameAndTheSetsSurvive()
        {
            WeatherKey basis = Basis(), key = ForceWeatherMap.Key(basis, 500f, WeatherRegimeType.Overcast, null, null, null, null);
            TestAssert.That(key.HeadingDeg == basis.HeadingDeg && key.SystemSpeed == basis.SystemSpeed && key.LayoutSalt == basis.LayoutSalt && key.Sets == basis.Sets && key.HasAnchor && key.AnchorX == 1000f,
                "the frame, the speed, the layout salt, the set-pieces and the anchor are the live key's");
        }

        private static void EveryClassBuildsAValidField()
        {
            for (int c = 0; c < 7; c++)
            {
                WeatherKey key = ForceWeatherMap.Key(Basis(), 500f, (WeatherRegimeType)c, null, 4500f, null, 0f);
                var field = new WeatherField();
                field.Build(key, 500f, 81920f, 40960f);
                WeatherPoint p = field.Sample(0f, 0f);
                TestAssert.That(SkyLibrary.ClassOf(key.SkyPin) == (WeatherRegimeType)c && !float.IsNaN(p.SkyCover + p.NativeDeck), "class " + (WeatherRegimeType)c + " builds and samples");
            }
        }
    }
}
