using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Pinned play (spec 11 item 2): a SkyPin makes the whole field one library sky, held for ever; no pin is the legacy timeline on the class skies.</summary>
    internal static class PinnedSkyTests
    {
        public static void Run()
        {
            PinIsPartOfTheTapeIdentity();
            EveryLibrarySkyBuildsAndSamples();
            PinnedSkyIsHeldAndInstanced();
            UnpinnedPlayIsTheLegacyTimeline();
            LayersOfSpecificSkies();
            SkiesWithNoLowLayerStayHigh();
        }

        private static WeatherKey Pinned(int sky, uint seed = 73u, float openingBase = 0f, bool dynamic = true) =>
            new WeatherKey(seed, 0f, dynamic, skyPin: (byte)sky, openingBase: openingBase);

        private static void PinIsPartOfTheTapeIdentity()
        {
            var key = new WeatherKey(73u, 0f, true, (byte)WeatherRegimeType.Broken);
            WeatherKey pinned = key.WithSkyPin(7);
            TestAssert.That(pinned.SkyPin == 7 && pinned.TapeId != key.TapeId && !pinned.SameTape(key), "a pin is a cut: it changes the tape identity");
            TestAssert.That(pinned.WithSkyPin(WeatherKey.None).TapeId == key.TapeId, "unpinning returns to the same tape");
            TestAssert.That(pinned.WithSets(3).SkyPin == 7 && pinned.Rebased(100f, false).SkyPin == 7 && pinned.WithLayoutSalt(3).SkyPin == 7,
                "sets, rebases and re-rolls keep the pin");
            TestAssert.That(pinned.WithLayoutSalt(3).TapeId != pinned.TapeId, "and a re-roll still changes the tape");
            WeatherKey reset = WeatherScenarios.Apply(WeatherScenario.ResetAll, pinned, false, 0f, 0f, 0f, 1f);
            TestAssert.That(reset.SkyPin == WeatherKey.None, "the ResetAll scenario unpins");
            WeatherKey hurricane = WeatherScenarios.Apply(WeatherScenario.HurricaneEye, pinned, false, 0f, 0f, 0f, 1f);
            TestAssert.That(hurricane.SkyPin == 43, "a pin scenario pins its sky (the storm eye is sky 44)");
        }

        private static void EveryLibrarySkyBuildsAndSamples()
        {
            var field = new WeatherField();
            var meanLow = new float[7]; var meanHigh = new float[7];
            for (int c = 0; c < 7; c++) meanLow[c] = 1f;
            for (int i = 0; i < SkyLibrary.Count; i++)
            {
                field.Build(Pinned(i), 600f, 40000f, 40000f, 12f);
                // The class is the library's nominal one; the jittered instance's own row may sit one band over at a band edge.
                TestAssert.That(field.Key.SkyPin == i && Math.Abs((int)SkyMath.ClassOf(field.RowAt(0f, 0f)) - (int)SkyLibrary.ClassOf(i)) <= 1,
                    "sky " + (i + 1) + " holds its own class for ever");
                int rainy = 0;
                for (int z = -35000; z <= 35000; z += 5000)
                for (int x = -35000; x <= 35000; x += 5000)
                {
                    WeatherPoint p = field.Sample(x, z);
                    TestAssert.That(!float.IsNaN(p.Cover) && !float.IsNaN(p.CloudBase) && !float.IsNaN(p.DeckTop) && !float.IsNaN(p.RainRate) && !float.IsNaN(p.MidTop),
                        "sky " + (i + 1) + ": finite sample");
                    TestAssert.That(p.LowTop >= p.CloudBase && p.CloudTop >= p.LowTop && p.DeckTop >= p.CloudBase, "sky " + (i + 1) + ": truthful heights");
                    if (p.RainRate > 0.1f) rainy++;
                }
                // Per-sky MeanCover (spec 10.1): the legacy Cover mean, which is what vanilla's conditions follow until slice 4b replaces it by the
                // theater mean of SkyCover (8.1). Here it must be a usable number for every sky; its relation to the class belongs to 4b.
                float mean = field.MeanCover(5);
                int cls = (int)SkyLibrary.ClassOf(i);
                TestAssert.That(!float.IsNaN(mean) && mean >= 0f && mean <= 1f, "sky " + (i + 1) + ": MeanCover is finite and inside 0..1, got " + mean);
                meanLow[cls] = Math.Min(meanLow[cls], mean); meanHigh[cls] = Math.Max(meanHigh[cls], mean);
                SkyParams s = field.RowAt(0f, 0f);
                if (s.StratRain > 1f && s.Mid.Cover + s.SheetCover > 0.9f) TestAssert.That(rainy > 20, "sky " + (i + 1) + " rains over much of the field, got " + rainy);
                if (SkyLibrary.ClassOf(i) <= WeatherRegimeType.Overcast && s.Convective == 0f) TestAssert.That(rainy == 0, "sky " + (i + 1) + " is dry");
                if (s.Convective > 0.1f) TestAssert.That(StormLattice.FindNearest(field.Lattice, null, 0f, 0f, 10, 0.0001f, out _), "sky " + (i + 1) + " has storm cells");
            }
            string spread = "MeanCover of the pinned skies by class (printed, not asserted against the class until slice 4b):";
            for (int c = 0; c < 7; c++) spread += "\n  " + (WeatherRegimeType)c + " " + meanLow[c].ToString("0.000") + "-" + meanHigh[c].ToString("0.000");
            Console.WriteLine(spread);
        }

        private static void PinnedSkyIsHeldAndInstanced()
        {
            var a = new WeatherField(); var b = new WeatherField();
            WeatherKey key = Pinned(7, 4242u, 1700f);
            a.Build(key, 100f, 40000f, 40000f);
            b.Build(key, 7200f, 40000f, 40000f);
            TestAssert.That(a.RowAt(0f, 0f).CloudBase == b.RowAt(0f, 0f).CloudBase && a.RowAt(0f, 0f).CumulusCover == b.RowAt(0f, 0f).CumulusCover && a.RowAt(0f, 0f).Mid.Cover == b.RowAt(0f, 0f).Mid.Cover,
                "a pinned sky does not change with time, even on a moving key");
            SkyParams instance = SkyLibrary.Instance(7, SkyLibrary.PinSeed(key), 1700f);
            TestAssert.That(a.RowAt(0f, 0f).CloudBase == instance.CloudBase && a.RowAt(0f, 0f).CumulusCover == instance.CumulusCover && a.RowAt(0f, 0f).LobeRung == instance.LobeRung,
                "the field shows the library instance of the key's seed, salt and opening base");
            TestAssert.That(Math.Abs(a.RowAt(0f, 0f).CloudBase - 1700f) < 0.01f, "the mission altitude replaces the humilis' base");
            var c = new WeatherField();
            c.Build(key.WithLayoutSalt(1), 100f, 40000f, 40000f);
            TestAssert.That(c.RowAt(0f, 0f).CloudBase != a.RowAt(0f, 0f).CloudBase || c.RowAt(0f, 0f).CumulusCover != a.RowAt(0f, 0f).CumulusCover || c.RowAt(0f, 0f).LobeRung != a.RowAt(0f, 0f).LobeRung ||
                c.RowAt(0f, 0f).High.Cover != a.RowAt(0f, 0f).High.Cover, "Re-roll gives another instance of the same sky");
            WeatherPoint p1 = a.Sample(1234f, -5678f), p2 = b.Sample(1234f, -5678f);
            TestAssert.That(p1.Cover == p2.Cover && p1.CloudBase == p2.CloudBase, "and the same sky at the same place");
        }

        private static void UnpinnedPlayIsTheLegacyTimeline()
        {
            foreach (byte bad in new byte[] { 47, 100, 254 })
            {
                var field = new WeatherField();
                var key = new WeatherKey(73u, 0f, true, (byte)WeatherRegimeType.Fair, skyPin: bad);
                field.Build(key, 1300f, 40000f, 40000f);
                TestAssert.That(field.TapeSource.Keyframes.Length > 1, "a pin outside the library (" + bad + ") builds the auto tape");
            }
        }

        private static void SkiesWithNoLowLayerStayHigh()
        {
            // Skies 1-7, 33 and 38 have no low layer. The legacy patch noise still draws a faint deck for them, at 2400 m like the old Clear
            // state; never down on the ground, and never as real cover.
            var field = new WeatherField();
            foreach (int sky in new[] { 0, 1, 2, 3, 4, 5, 6, 32, 37 })
            {
                field.Build(Pinned(sky), 100f, 40000f, 40000f);
                TestAssert.That(!SkyLibrary.Get(sky).HasLow && field.RowAt(0f, 0f).CloudBase == 2400f && field.RowAt(0f, 0f).SheetCover == 0f && field.RowAt(0f, 0f).CumulusCover == 0f,
                    "sky " + (sky + 1) + " has no low layer: its low columns are the faint humilis at 2400 m");
                for (int z = -30000; z <= 30000; z += 10000)
                    for (int x = -30000; x <= 30000; x += 10000)
                    {
                        WeatherPoint p = field.Sample(x, z);
                        TestAssert.That(p.CloudBase >= 2000f && p.Cover < 0.07f, "sky " + (sky + 1) + ": no low cloud near the ground, got base " + p.CloudBase + " cover " + p.Cover);
                    }
            }
        }

        private static void LayersOfSpecificSkies()
        {
            var field = new WeatherField();
            field.Build(Pinned(3), 100f, 40000f, 40000f); // sky 4, cirrostratus veil
            WeatherPoint veil = field.Sample(0f, 0f);
            TestAssert.That(veil.HighCover > 0.3f && veil.HighCover < 0.6f && veil.HighBase > 8000f && veil.MidCover == 0f, "sky 4 is a high veil and nothing else");
            field.Build(Pinned(32), 100f, 40000f, 40000f); // sky 33, altostratus opacus
            WeatherPoint alto = field.Sample(0f, 0f);
            TestAssert.That(alto.MidCover > 0.9f && alto.MidBase < 4000f && alto.MidTop - alto.MidBase > 1500f, "sky 33 is a thick opaque altostratus");
            field.Build(Pinned(37), 100f, 40000f, 40000f); // sky 38, embedded showers
            WeatherPoint showers = field.Sample(0f, 0f);
            TestAssert.That(showers.MidCover > 0.9f && field.RowAt(0f, 0f).StratRain == 2.5f && field.RowAt(0f, 0f).RainBase == 3300f, "sky 38 rains out of its altostratus");
        }
    }
}
