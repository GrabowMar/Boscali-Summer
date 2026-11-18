using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Weather.Domain;

namespace WeatherClimateTests
{
    /// <summary>What the host does once at capture (the class remap, the set pieces) and the client-local air: temperature, haze, snow, text.</summary>
    internal static class ClimateRemapTests
    {
        private const int Seeds = 10000;

        private static ClimateProfile Profile(string name, byte id, float seaLevel, float diurnal, float haze) =>
            new ClimateProfile { profile = name, id = id, seaLevelTempC = seaLevel, diurnalC = diurnal, hazeScale = haze };

        public static void Run()
        {
            WeatherClimate.Local = null;

            // The remap over 10,000 seeds follows bias times kernel, and the arctic never opens on a storm.
            var want = new float[WeatherKey.ClassCount];
            for (byte climate = 1; climate < WeatherClimate.Count; climate++)
                for (byte cls = 0; cls < WeatherKey.ClassCount; cls++)
                {
                    var got = new int[WeatherKey.ClassCount];
                    for (uint seed = 1; seed <= Seeds; seed++) got[WeatherClimate.RemapClass(climate, cls, seed)]++;
                    WeatherClimate.RemapWeights(climate, cls, want);
                    for (int k = 0; k < got.Length; k++)
                    {
                        TestAssert.That(Math.Abs(got[k] / (float)Seeds - want[k]) < 0.02f,
                            "climate " + climate + " class " + cls + " -> " + k + ": " + got[k] + " of " + Seeds + ", want share " + want[k]);
                        if (Math.Abs(k - cls) > 3) TestAssert.Eq(got[k], 0, "the kernel ends three classes away");
                    }
                    if (climate == WeatherClimate.Arctic) TestAssert.Eq(got[(int)WeatherRegimeType.Storm], 0, "arctic class " + cls + " never remaps to Storm");
                }

            // The table itself, one row by hand: an arctic mission set to Overcast (the map's own 0.60).
            WeatherClimate.RemapWeights(WeatherClimate.Arctic, (int)WeatherRegimeType.Overcast, want);
            float[] byHand = { 0f, 0.8f * 0.08f, 0.6f * 0.25f, 1.3f * 0.6f, 1.8f, 0.5f * 0.6f, 0f };
            float sum = 0f;
            foreach (float w in byHand) sum += w;
            for (int k = 0; k < byHand.Length; k++) TestAssert.Near(want[k], byHand[k] / sum, "arctic Overcast weight " + k);
            WeatherClimate.RemapWeights(WeatherClimate.Tropical, (int)WeatherRegimeType.Scattered, want);
            TestAssert.That(want[(int)WeatherRegimeType.Scattered] > want[(int)WeatherRegimeType.Fair] && want[(int)WeatherRegimeType.Fair] > want[(int)WeatherRegimeType.Clear],
                "the mission's own class stays the centre");

            // No climate, an unknown climate and no class leave the mission's setting alone.
            for (uint seed = 1; seed <= 200; seed++)
                for (byte cls = 0; cls < WeatherKey.ClassCount; cls++)
                {
                    TestAssert.Eq(WeatherClimate.RemapClass(0, cls, seed), cls, "no climate, no remap");
                    TestAssert.Eq(WeatherClimate.RemapClass(77, cls, seed), cls, "unknown climate, no remap");
                }
            TestAssert.Eq(WeatherClimate.RemapClass(WeatherClimate.Alpine, WeatherKey.None, 5u), WeatherKey.None, "no class stays no class");

            // Set pieces: lenticulars 0.6 and fog 0.35 on alpine, fog 0.35 on arctic, none elsewhere.
            int alpineLens = 0, alpineFog = 0, arcticFog = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                byte alpine = WeatherClimate.Sets(WeatherClimate.Alpine, seed), arctic = WeatherClimate.Sets(WeatherClimate.Arctic, seed);
                if ((alpine & Superstructures.LenticularSet) != 0) alpineLens++;
                if ((alpine & Superstructures.FogBankSet) != 0) alpineFog++;
                if ((arctic & Superstructures.FogBankSet) != 0) arcticFog++;
                TestAssert.Eq(arctic & ~Superstructures.FogBankSet, 0, "arctic sets only the fog bank");
                TestAssert.Eq(alpine & ~(Superstructures.FogBankSet | Superstructures.LenticularSet), 0, "alpine sets only lenticulars and fog");
                TestAssert.Eq(WeatherClimate.Sets(WeatherClimate.Tropical, seed) | WeatherClimate.Sets(0, seed) | WeatherClimate.Sets(200, seed), 0, "no set pieces without a table row");
            }
            TestAssert.That(Math.Abs(alpineLens / (float)Seeds - 0.6f) < 0.02f, "alpine lenticular share " + alpineLens);
            TestAssert.That(Math.Abs(alpineFog / (float)Seeds - 0.35f) < 0.02f, "alpine fog share " + alpineFog);
            TestAssert.That(Math.Abs(arcticFog / (float)Seeds - 0.35f) < 0.02f, "arctic fog share " + arcticFog);

            // Storms and ids.
            TestAssert.That(WeatherClimate.Storms(0) && WeatherClimate.Storms(WeatherClimate.Tropical) && WeatherClimate.Storms(WeatherClimate.Alpine) && WeatherClimate.Storms(99), "storms everywhere but the arctic");
            TestAssert.That(!WeatherClimate.Storms(WeatherClimate.Arctic), "no storms in the arctic");
            TestAssert.Eq(WeatherClimate.Id(null), (byte)0, "no profile is climate 0");
            TestAssert.Eq(WeatherClimate.Id(Profile("mars", 9, 0f, 0f, 1f)), (byte)0, "an unknown id is climate 0");
            TestAssert.Eq(WeatherClimate.StoryBias(0, 2), 1f, "no climate, no story bias");
            TestAssert.Eq(WeatherClimate.StoryBias(WeatherClimate.Arctic, (int)StoryKind.ConvectiveDay), 0f, "no convective day in the arctic");
            TestAssert.Eq(WeatherClimate.StoryBias(WeatherClimate.Tropical, (int)StoryKind.ConvectiveDay), 3f, "tropical convection");

            // Snow: none at 3 C, all of it at -1 C; never on a map with no climate.
            TestAssert.Eq(WeatherClimate.Snow(3f), 0f, "snow at 3 C");
            TestAssert.Eq(WeatherClimate.Snow(-1f), 1f, "snow at -1 C");
            TestAssert.Near(WeatherClimate.Snow(0.5f), 0.5f, "snow at 0.5 C");
            TestAssert.Eq(WeatherClimate.SnowNow(-20f), 0f, "a vanilla map keeps its rain at altitude");

            // METAR temperature groups print below zero.
            TestAssert.Eq(WeatherClimate.MetarDegrees(-9.4f), "M09", "minus nine");
            TestAssert.Eq(WeatherClimate.MetarDegrees(-12.5f), "M13", "minus twelve and a half rounds away from zero");
            TestAssert.Eq(WeatherClimate.MetarDegrees(14.2f), "14", "fourteen");
            TestAssert.Eq(WeatherClimate.MetarDegrees(-0.4f), "00", "minus zero is zero");

            // The air of a field under each climate, against the same field with none.
            var key = new WeatherKey(4000000007u, 10f, false, (byte)WeatherRegimeType.RainSquall, skyPin: 34, headingDeg: 250f); // sky 35, Steady rain
            const float hour = 15f;
            float swing = (float)Math.Sin((hour - 9f) / 24f * 2f * Math.PI);
            WeatherPoint Sample()
            {
                var field = new WeatherField();
                field.Build(key, 10f, 40000f, 40000f, hour);
                // The wettest point of a coarse grid: the same one under every climate, since a climate does not touch the rain.
                WeatherPoint wettest = field.Sample(0f, 0f);
                for (int x = -40; x <= 40; x += 8)
                    for (int z = -40; z <= 40; z += 8)
                    {
                        WeatherPoint p = field.Sample(x * 1000f, z * 1000f);
                        if (p.RainRate > wettest.RainRate) wettest = p;
                    }
                return wettest;
            }
            WeatherPoint plain = Sample();
            string plainMetar = WeatherMetar.Line(plain);
            TestAssert.That(plain.RainRate > 0.1f && plainMetar.Contains("RA") && !plainMetar.Contains("/"), "the vanilla line is the old line: " + plainMetar);
            try
            {
                (ClimateProfile Profile, bool Snow)[] maps =
                {
                    (Profile("tropical", 1, 28f, 4f, 0.8f), false), (Profile("alpine", 2, 14f, 8f, 1.3f), false), (Profile("arctic", 3, -9f, 3f, 1.6f), true),
                };
                foreach (var map in maps)
                {
                    WeatherClimate.Local = map.Profile;
                    WeatherPoint p = Sample();
                    float shift = map.Profile.seaLevelTempC - 21f + (map.Profile.diurnalC - 5f) * swing;
                    TestAssert.That(Math.Abs(p.Temperature - (plain.Temperature + shift)) < 0.001f, map.Profile.profile + " temperature " + p.Temperature + ", want " + (plain.Temperature + shift));
                    TestAssert.That(Math.Abs(p.Dewpoint - (plain.Dewpoint + shift)) < 0.001f, map.Profile.profile + " dewpoint follows");
                    TestAssert.That(map.Profile.hazeScale > 1f ? p.VisibilityKm > plain.VisibilityKm : p.VisibilityKm < plain.VisibilityKm, map.Profile.profile + " visibility " + p.VisibilityKm + " against " + plain.VisibilityKm);
                    TestAssert.Eq(p.RainRate, plain.RainRate, "the climate does not touch the rain");
                    TestAssert.Eq(WeatherClimate.SnowNow(p.Temperature) > 0.5f, map.Snow, map.Profile.profile + " snow at the surface");
                    string metar = WeatherMetar.Line(p);
                    TestAssert.That(metar.Contains(map.Snow ? "SN" : "RA") && !metar.Contains(map.Snow ? "RA" : "SN"), map.Profile.profile + " precipitation code: " + metar);
                    TestAssert.That(metar.EndsWith(" " + WeatherClimate.MetarDegrees(p.Temperature) + "/" + WeatherClimate.MetarDegrees(p.Dewpoint), StringComparison.Ordinal), map.Profile.profile + " temperature group: " + metar);
                    if (map.Snow) TestAssert.That(p.Temperature < 0f && metar.Contains(" M"), "arctic prints a negative temperature: " + metar);
                }

                // Lightning: a storm sky still strikes on an alpine map, never on an arctic one.
                var storm = new WeatherKey(77u, 10f, false, (byte)WeatherRegimeType.Storm, skyPin: 44, headingDeg: 250f); // sky 45, Cold-front storm line
                float Strikes()
                {
                    var field = new WeatherField();
                    field.Build(storm, 10f, 40000f, 40000f, hour);
                    float most = 0f;
                    for (int x = -40; x <= 40; x += 4)
                        for (int z = -40; z <= 40; z += 4) most = Math.Max(most, field.Sample(x * 1000f, z * 1000f).LightningRate);
                    return most;
                }
                WeatherClimate.Local = maps[1].Profile;
                TestAssert.That(Strikes() > 0f, "alpine storms carry lightning");
                WeatherClimate.Local = maps[2].Profile;
                TestAssert.Eq(Strikes(), 0f, "arctic has no lightning");
            }
            finally { WeatherClimate.Local = null; }
            TestAssert.Eq(WeatherMetar.Line(Sample()), plainMetar, "the profile gone, the vanilla line is back");
        }
    }
}
