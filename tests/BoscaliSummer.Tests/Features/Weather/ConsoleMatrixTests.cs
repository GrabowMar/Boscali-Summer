using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// What the console's buttons do to the world, as a matrix (spec 10.4): every story (auto and the five) times every pin (none, the first sky of each class, the storm eye's sky) times every
    /// force mask (all 64 combinations of the six set-pieces). It replaced the 7 x 64 state matrix of the legacy timeline.
    /// </summary>
    internal static class ConsoleMatrixTests
    {
        private static readonly byte[] Stories = { WeatherKey.None, 0, 1, 2, 3, 4 };
        private static readonly byte[] Pins = { WeatherKey.None, 0, 7, 14, 21, 28, 34, 41, 43 };

        public static void Run()
        {
            var field = new WeatherField();
            int combinations = 0;
            foreach (byte story in Stories)
                foreach (byte pin in Pins)
                    for (byte sets = 0; sets < 64; sets++)
                    {
                        var key = new WeatherKey(90210u, 0f, false, 2, sets: sets, story: story, skyPin: pin, offset: 40000f);
                        field.Build(key, 900f, 60000f, 60000f, 13f);
                        combinations++;
                        TestAssert.That(field.SuperstructureCount <= 5, "console formations stay bounded");
                        byte found = 0;
                        for (int h = 0; h < field.SuperstructureCount; h++)
                        {
                            Superstructure hero = field.SuperstructureAt(h);
                            found |= hero.Set;
                            TestAssert.That(!float.IsNaN(hero.X + hero.Z + hero.Top) && hero.Top > 0f, "finite formation");
                            if ((sets & hero.Set) != 0) TestAssert.That(hero.Strength == 1f, "forced formation at full strength");
                        }
                        TestAssert.That((found & sets & 31) == (sets & 31), "every forced cloud formation is present (story " + story + ", pin " + pin + ", sets " + sets + ")");
                        WeatherPoint centre = field.Sample(0f, 0f);
                        TestAssert.That(!float.IsNaN(centre.SkyCover + centre.NativeDeck + centre.RainRate) && centre.SkyCover >= 0f && centre.SkyCover <= 1f, "a finite, valid centre sample");
                        if (sets != 0 && sets != 63) continue;
                        // A held tape forecasts one sky: the class at +60 minutes is the class now, whatever the local gaps say.
                        WeatherRegimeType now = SkyMath.ClassOf(field.RowAt(0f, 0f));
                        foreach (int offset in WeatherForecast.DefaultOffsetsMinutes)
                        {
                            field.Build(key, 900f + offset * 60f, 60000f, 60000f, 13f);
                            var step = new ForecastStep(offset, field.Sample(0f, 0f).SkyCover, field.Sample(0f, 0f).NativeDeck, 0f, SkyMath.ClassOf(field.RowAt(0f, 0f)));
                            TestAssert.That(step.Regime.Type == now, "the held forecast keeps the class despite local gaps");
                        }
                    }
            TestAssert.That(combinations == Stories.Length * Pins.Length * 64, "the whole matrix ran");
            Console.WriteLine("Weather console matrix: " + combinations + " story/pin/set combinations passed.");
        }
    }
}
