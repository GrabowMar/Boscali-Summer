using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class WeatherConsoleTests
    {
        public static void Run()
        {
            var field = new WeatherField();
            // Every possible combination of the seven state buttons and six force toggles.
            for (byte state = 0; state < 7; state++)
                for (byte sets = 0; sets < 64; sets++)
                {
                    var key = new WeatherKey(90210u, 0f, false, state, sets: sets);
                    field.Build(key, 900f, 60000f, 60000f, 13f);
                    TestAssert.That(field.SuperstructureCount <= 5, "console formations stay bounded");
                    byte found = 0;
                    for (int h = 0; h < field.SuperstructureCount; h++)
                    {
                        Superstructure hero = field.SuperstructureAt(h);
                        found |= hero.Set;
                        TestAssert.That(!float.IsNaN(hero.X + hero.Z + hero.Top) && hero.Top > 0f, "finite formation");
                        if ((sets & hero.Set) != 0) TestAssert.That(hero.Strength == 1f, "forced formation at full strength");
                    }
                    TestAssert.That((found & sets & 31) == (sets & 31), "every forced cloud formation is present");
                    foreach (int offset in WeatherForecast.DefaultOffsetsMinutes)
                    {
                        field.Build(key, 900f + offset * 60f, 60000f, 60000f, 13f);
                        WeatherPoint point = field.Sample(0f, 0f);
                        var step = new ForecastStep(offset, point.Cover, point.CloudBase, 0f, field.Timeline.Dominant);
                        TestAssert.That(step.Regime.Type == (WeatherRegimeType)state, "held forecast keeps selected state despite local gaps");
                    }
                }

            var dirty = new WeatherKey(90210u, 900f, false, 6, 5f, 60f, 63, 7, true, -12000f, 18000f, 5);
            byte[] states = { 6, 5, 1, 0, 4, 6 }, setsWanted = { 8, 3, 16, 32, 0, 0 };
            foreach (WeatherScenario scenario in (WeatherScenario[])Enum.GetValues(typeof(WeatherScenario)))
            {
                WeatherKey key = WeatherScenarios.Apply(scenario, dirty, true, 1000f, 2000f, 0f, 0.5f);
                int i = (int)scenario;
                TestAssert.That(key.StartState == states[i] && key.Sets == setsWanted[i], "scenario selects its exact state and force bits");
                TestAssert.That(key.FrontTurn == 0 && key.Seed == dirty.Seed && key.LayoutSalt == dirty.LayoutSalt,
                    "scenario resets front orientation and retains deterministic layout");
                TestAssert.That(key.Dynamic == (scenario == WeatherScenario.ResetAll), "only reset resumes changing weather");
                bool anchored = scenario == WeatherScenario.HurricaneEye || scenario == WeatherScenario.MountainWave;
                TestAssert.That(key.HasAnchor == anchored, "scenario clears unrelated previous anchors");
                if (anchored) TestAssert.That(key.AnchorX == 1000f && key.AnchorZ == (i == 0 ? 2000f : 27000f), "camera heading is normalized horizontally");
                TestAssert.That(!WeatherScenarios.Apply(scenario, dirty, false, 0f, 0f, 0f, 0f).HasAnchor, "no camera uses default placement");
            }
            var wave = WeatherScenarios.Apply(WeatherScenario.MountainWave, dirty, true, 0f, 0f, 0f, 1f);
            field.Build(wave, 900f, 60000f, 60000f, 13f);
            Superstructure lens = Find(field, Superstructures.LenticularSet);
            TestAssert.That(lens.X == 0f && lens.Z == 25000f, "mountain wave is 25 km ahead without unintended sideways displacement");
            field.Build(wave.WithSets(24), 900f, 60000f, 60000f, 13f);
            lens = Find(field, Superstructures.LenticularSet);
            TestAssert.That(Math.Abs(Math.Sqrt(lens.X * lens.X + (lens.Z - 25000f) * (lens.Z - 25000f)) - 30000f) < 1f,
                "combined lenses sit beside the eye");
            TestAssert.That(!wave.WithoutAnchor().HasAnchor && wave.WithAnchor(12f, 34f).AnchorZ == 34f, "placement and default placement");
            TestAssert.That(wave.WithFrontTurn(8).FrontTurn == 0 && wave.WithFrontTurn(4).FrontTurn == 4, "rotate wraps and flip turns half a circle");
            field.Build(wave, 900f, 60000f, 60000f, 13f);
            uint layout = field.Timeline.Layout;
            field.Build(wave.WithLayoutSalt(8), 900f, 60000f, 60000f, 13f);
            TestAssert.That(field.Timeline.Layout != layout && field.Timeline.Dominant == WeatherRegimeType.Fair, "reroll changes layout without changing state");

            var eyeKey = WeatherScenarios.Apply(WeatherScenario.HurricaneEye, dirty, true, 0f, 0f, 0f, 1f);
            field.Build(eyeKey, 900f, 60000f, 60000f, 13f);
            var bodies = new CloudBodies(CloudNoise3D.Generate(64, 47), 64, field.Params, field.PrevailingHeading, field.Split, 0f, field);
            Superstructure eye = Find(field, Superstructures.StormEyeSet);
            foreach (float y in new[] { 2500f, 4100f, 7000f, 10000f })
                TestAssert.That(bodies.Density(field.Sample(0f, 0f), 0f, y, 0f, 0f) < 0.001f, "upper eye stays clear through low and middle decks");
            float wallX = eye.Size + eye.Extent * 0.3f;
            TestAssert.That(bodies.Density(field.Sample(wallX, 0f), wallX, 5000f, 0f, 0f) > 0.1f, "eyewall remains opaque");
            Console.WriteLine("Weather console: 448 state/set combinations, 6 complete scenarios, forecast, placement, reroll and eye checks passed.");
        }

        private static Superstructure Find(WeatherField field, byte set)
        {
            for (int i = 0; i < field.SuperstructureCount; i++)
                if (field.SuperstructureAt(i).Set == set) return field.SuperstructureAt(i);
            throw new Exception("Missing formation " + set);
        }
    }
}
