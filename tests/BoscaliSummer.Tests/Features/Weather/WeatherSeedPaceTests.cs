using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class WeatherSeedPaceTests
    {
        public static void Run()
        {
            PaceTableMatchesTheSpec();
            PaceInputsAreClamped();
            SeedModes();
        }

        private static void PaceTableMatchesTheSpec()
        {
            // Spec 5.4: rows are pace 1-6, columns wind 6, 12, 18 m/s.
            float[][] expected =
            {
                new[] { 18f, 18f, 18f }, new[] { 18f, 20f, 23f }, new[] { 23f, 27f, 31f },
                new[] { 29f, 34f, 39f }, new[] { 34f, 40f, 45f }, new[] { 38f, 45f, 45f },
            };
            float[] winds = { 6f, 12f, 18f };
            for (int pace = 1; pace <= 6; pace++)
                for (int w = 0; w < 3; w++)
                {
                    float got = TapePace.Speed(pace, winds[w]);
                    TestAssert.That(Math.Abs(got - expected[pace - 1][w]) < 0.5f,
                        $"pace {pace} at wind {winds[w]}: got {got}, want {expected[pace - 1][w]}");
                }
            TestAssert.That(Math.Abs(TapePace.Speed(3, 12f) - 27f) < 1e-4f, "the default pace at wind 12 is 27 m/s");
        }

        private static void PaceInputsAreClamped()
        {
            TestAssert.That(TapePace.Speed(0, 12f) == TapePace.Speed(1, 12f) && TapePace.Speed(-5, 12f) == TapePace.Speed(1, 12f),
                "a pace below 1 reads as 1");
            TestAssert.That(TapePace.Speed(99, 12f) == TapePace.Speed(6, 12f), "a pace above 6 reads as 6");
            TestAssert.That(TapePace.Speed(3, 0f) == TapePace.Speed(3, 6f) && TapePace.Speed(3, 300f) == TapePace.Speed(3, 18f),
                "mission wind is clamped to 6-18 m/s");
            TestAssert.That(TapePace.Speed(3, float.NaN) == TapePace.Speed(3, 6f), "NaN wind reads as the slowest factor");
            for (int pace = -2; pace < 9; pace++)
                for (float wind = -5f; wind < 60f; wind += 3f)
                {
                    float v = TapePace.Speed(pace, wind);
                    TestAssert.That(v >= 18f && v <= 45f, "speed stays in the 18-45 m/s clamp");
                }
        }

        private static void SeedModes()
        {
            var ridge = new MissionSeedInputs("Ridge Run", "maps/Heartland", 10f, 0.5f, 1800f, 12f, 270f);
            var other = new MissionSeedInputs("Ridge Run 2", "maps/Heartland", 10f, 0.5f, 1800f, 12f, 270f);
            var windier = new MissionSeedInputs("Ridge Run", "maps/Heartland", 10f, 0.5f, 1800f, 13f, 270f);
            var free = default(MissionSeedInputs);

            uint m1 = WeatherSeed.Resolve(WeatherSeedMode.Mission, 0, 111UL, ridge);
            uint m2 = WeatherSeed.Resolve(WeatherSeedMode.Mission, 0, 999999UL, ridge);
            TestAssert.That(m1 == m2 && m1 != 0u, "Mission mode ignores the clock and is stable");
            TestAssert.That(m1 != WeatherSeed.Resolve(WeatherSeedMode.Mission, 0, 1UL, other), "a different mission name gives a different seed");
            TestAssert.That(m1 != WeatherSeed.Resolve(WeatherSeedMode.Mission, 0, 1UL, windier), "a different environment gives a different seed");
            TestAssert.That(WeatherSeed.Resolve(WeatherSeedMode.Mission, 0, 1UL, free) ==
                WeatherSeed.Resolve(WeatherSeedMode.Mission, 0, 2UL, default(MissionSeedInputs)), "Free Flight collapses to one constant");
            TestAssert.That(WeatherSeed.Resolve(WeatherSeedMode.Mission, 0, 1UL, free) != 16768u, "and it is not the legacy default seed 16768");
            // Vanilla's Free Flight is a real mission with a player-chosen map; ResolveSeed (Task 8) substitutes the
            // default inputs when this is true, so it cannot hash the map path into a per-map seed.
            TestAssert.That(WeatherSeed.IsFreeFlightName("Free Flight") && WeatherSeed.IsFreeFlightName("  free flight ") &&
                !WeatherSeed.IsFreeFlightName("Free Flight 2") && !WeatherSeed.IsFreeFlightName("Ridge Run") &&
                !WeatherSeed.IsFreeFlightName(null) && !WeatherSeed.IsFreeFlightName(string.Empty),
                "the default mission is recognised by its name");
            TestAssert.That(WeatherSeed.FromMission(new MissionSeedInputs(null, null, float.NaN, float.NaN, float.PositiveInfinity, 0f, 0f)) != 0u,
                "null names and non-finite environment values still hash");

            TestAssert.That(WeatherSeed.Resolve(WeatherSeedMode.Fixed, 4242, 1UL, ridge) == 4242u, "Fixed passes the seed through");
            TestAssert.That(WeatherSeed.Resolve(WeatherSeedMode.Fixed, WeatherSeed.MaxFixed, 1UL, ridge) == (uint)WeatherSeed.MaxFixed, "the top of the range is allowed");
            uint zero = WeatherSeed.Resolve(WeatherSeedMode.Fixed, 0, 77UL, ridge);
            TestAssert.That(zero == WeatherSeed.Mix(77UL), "FixedSeed 0 means Random");
            TestAssert.That(WeatherSeed.Resolve(WeatherSeedMode.Fixed, WeatherSeed.MaxFixed + 1, 77UL, ridge) == WeatherSeed.Mix(77UL), "out of range means Random");
            TestAssert.That(WeatherSeed.Resolve(WeatherSeedMode.Fixed, -3, 77UL, ridge) == WeatherSeed.Mix(77UL), "negative means Random");

            var seen = new System.Collections.Generic.HashSet<uint>();
            for (ulong entropy = 0; entropy < 2000; entropy++)
            {
                uint seed = WeatherSeed.Resolve(WeatherSeedMode.Random, 0, entropy, ridge);
                TestAssert.That(seed != 0u, "Random never returns 0");
                seen.Add(seed);
            }
            TestAssert.That(seen.Count > 1990, "Random spreads neighbouring clock values, got " + seen.Count);
            TestAssert.That(WeatherSeed.Mix(0UL) != 0u && WeatherSeed.Mix(ulong.MaxValue) != 0u, "edge entropies are nonzero");
        }
    }
}
