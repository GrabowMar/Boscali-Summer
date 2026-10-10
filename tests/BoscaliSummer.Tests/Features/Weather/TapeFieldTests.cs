using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The interim driver (spec 11 item 2): the seven class skies as SkyParams, the field reading its rows through a tape, and a
    /// hand-built tape making the genus and the layers vary in space.</summary>
    internal static class TapeFieldTests
    {
        public static void Run()
        {
            ClassSkiesAreDesignatedRows();
            ClassRowsAreExemptFromClassOf();
            FieldReadsOneKeyframeTape();
            FixtureTapeVariesTheGenusAndLayers();
            MapsEncloseTheLocalDeck();
            BodiesReadTheirRowFromTheTape();
        }

        private static void ClassSkiesAreDesignatedRows()
        {
            float[] lobes = { 2f, 1f, 3f, 4f, 3f, 3f, 5f };
            for (int i = 0; i < StateTable.Count; i++)
            {
                SkyParams row = StateTable.Get((WeatherRegimeType)i);
                TestAssert.That(row.LobeRung == lobes[i], "class " + i + " snaps its old puff scale to rung " + lobes[i] + ", got " + row.LobeRung);
                TestAssert.That(Math.Abs(row.Mid.Strength - 0.42f) < 1e-6f && Math.Abs(row.High.Strength - 0.12f) < 1e-6f,
                    "class " + i + " keeps today's layer strengths 0.42 and 0.12");
                TestAssert.That(row.HorizonDepth >= 300f && row.CapacityCover == 0f, "class rows have a horizon depth and no lattice capacity");
                TestAssert.That(Math.Abs(row.HorizonCover - Math.Max(0.9f * row.SheetCover, 0.3f * row.Cumulus + 0.2f * row.Convective)) < 1e-6f,
                    "class " + i + " keeps today's horizon uniform (INTERIM), not the baked library formula");
            }
            SkyParams overcast = StateTable.Get(WeatherRegimeType.Overcast);
            TestAssert.That(Math.Abs(overcast.Mid.Base - 3390f) < 0.5f && Math.Abs(overcast.Mid.Thick - 1570f) < 0.5f && overcast.Mid.Cover == 0.45f,
                "the Overcast row's old middle layer (cover 0.45, sheet 0.90) is a deck at 3390 m, 1570 m thick");
            TestAssert.That(Math.Abs(overcast.High.Thick - 880f) < 0.5f && Math.Abs(overcast.High.Aspect - 1.3f) < 1e-4f && overcast.High.Base == 8600f,
                "and its old high veil (0.95) is a deck 880 m thick with aspect 1.3");
            TestAssert.That(overcast.Mid.Veil > 0.8f && overcast.Mid.Streaks + overcast.Mid.Ripples + overcast.Mid.Veil > 0.99f,
                "a near-sheet middle layer is mostly veil");
            SkyParams fair = StateTable.Get(WeatherRegimeType.Fair);
            TestAssert.That(Math.Abs(fair.CumulusCover - 0.21f) < 1e-6f && Math.Abs(fair.Cumulus - 0.42f) < 1e-6f, "Fair keeps the legacy cumulus level and carries half of it as cover");
            SkyParams halfway = StateTable.At(2.5f);
            TestAssert.That(Math.Abs(halfway.LobeRung - 3.5f) < 1e-5f && halfway.SheetCover > StateTable.Get(WeatherRegimeType.Scattered).SheetCover, "a fade between two class skies blends their columns, the lobe rung going fractional for the ladder");
            TestAssert.That(Math.Abs(halfway.HorizonCover - Math.Max(0.9f * halfway.SheetCover, 0.3f * halfway.Cumulus + 0.2f * halfway.Convective)) < 1e-6f,
                "and the horizon cover of a fade is the uniform on the faded row, not the fade of two uniforms");
        }

        private static void ClassRowsAreExemptFromClassOf()
        {
            // INTERIM(slice 4): the seven class rows are the old state table back-derived (opacity 0.7 / 0.15, CumulusCover = Cumulus * 0.5, the old
            // Convective term), not library skies, and ClassConditions reads four of them one class up. Nominal readings: Clear 0.046, Fair 0.253,
            // Scattered 0.504 (Broken), Broken 0.639 (Overcast), Overcast 0.768 (Rain), Rain 1.000 (Storm), Storm 1.000. They cannot be retuned
            // to pass without changing today's picture: the layer strengths are pinned at 0.42 and 0.12, Overcast still reads 0.766 (Rain) with no
            // cumulus cover at all, Broken reads Broken only with essentially none, and the Rain row's convective term alone adds 0.21. Slice 4 deletes the rows with the legacy
            // timeline, and the class of a point is read from library skies only. This test pins the exemption, so that slice 4's ClassSampled
            // and airbase match cannot inherit the offset unnoticed: a row never reads lower than its own class and at most one higher,
            // and Clear, Fair and Storm read as themselves.
            for (int i = 0; i < StateTable.Count; i++)
            {
                var own = (WeatherRegimeType)i;
                WeatherRegimeType read = SkyMath.ClassOf(StateTable.Get(own));
                TestAssert.That(read >= own && (int)read - i <= 1, "class row " + own + " reads as " + read + ": never lower, at most one class up");
            }
            foreach (WeatherRegimeType c in new[] { WeatherRegimeType.Clear, WeatherRegimeType.Fair, WeatherRegimeType.Storm })
                TestAssert.That(SkyMath.ClassOf(StateTable.Get(c)) == c, c + " reads as its own class");
        }

        private static void FieldReadsOneKeyframeTape()
        {
            foreach (WeatherRegimeType state in (WeatherRegimeType[])Enum.GetValues(typeof(WeatherRegimeType)))
            {
                var field = new WeatherField();
                field.Build(new WeatherKey(73u, 0f, false, (byte)state), 600f, 40000f, 40000f);
                TestAssert.That(field.Tape.IsValid && field.Tape.IsConstant, "the live field has a one-keyframe tape");
                SkyParams row = field.Tape.At(12345f);
                TestAssert.That(row.CloudBase == field.Params.CloudBase && row.LobeRung == field.Params.LobeRung && row.Mid.Cover == field.Params.Mid.Cover,
                    "its row is the sky the timeline shows");
                WeatherPoint p = field.Sample(5000f, -3000f);
                TestAssert.That(p.DeckTop >= p.CloudBase + 280f && p.MidCover == field.Params.Mid.Cover && p.HighBase == field.Params.High.Base,
                    "a sample carries the local deck top and thin layers");
            }
            var again = new WeatherField();
            var key = new WeatherKey(73u, 0f, false, (byte)WeatherRegimeType.Broken);
            again.Build(key, 600f, 40000f, 40000f);
            WeatherPoint before = again.Sample(1000f, 1000f);
            again.UseFixtureTape(TapeSampler.Constant(WithLobe(again.Params, 5f)), new TapeFrame(0f, 0f));
            again.Build(key, 600f, 40000f, 40000f);
            TestAssert.That(again.Tape.IsConstant && again.Tape.At(0f).LobeRung == again.Params.LobeRung && again.Sample(1000f, 1000f).DeckTop == before.DeckTop,
                "the next Build restores the one-keyframe tape");
        }

        private static SkyParams WithLobe(SkyParams s, float rung) { s.LobeRung = rung; return s; }

        /// <summary>A hand-built sky with a deck of the given puff depth and wobble and a middle layer (no library needed).</summary>
        private static SkyParams Deck(float puffDepth, float wobble, float midCover)
        {
            var sky = new SkyParams { CloudBase = 1500f, PuffDepth = puffDepth, BaseWobble = wobble, BaseSharp = 50f, Dome = 0.9f, Billow = 0.6f,
                LobeRung = 2f, SheetCover = 0.4f, OpacityMid = 0.6f };
            sky.Mid = new SkyLayer { Base = 4200f, Thick = 500f, Cover = midCover, PeriodRung = 4f, Detail = 0.7f, Streaks = 1f, Strength = 0.36f, Aspect = 1.4f, RippleWavelength = 900f };
            return sky;
        }

        private static void FixtureTapeVariesTheGenusAndLayers()
        {
            var field = new WeatherField();
            field.Build(new WeatherKey(73u, 0f, false, (byte)WeatherRegimeType.Scattered), 600f, 80000f, 80000f);
            SkyParams humilis = Deck(500f, 30f, 0.8f), cb = Deck(2000f, 80f, 0f);
            var frame = new TapeFrame(90f, 1500000f);
            field.UseFixtureTape(new TapeSampler(BakedTape.TwoKeyframes(humilis, cb, 1500000f, 12000f)), frame);
            WeatherPoint west = field.Sample(-30000f, 0f), east = field.Sample(30000f, 0f);
            TestAssert.That(Math.Abs(west.DeckTop - west.CloudBase - (500f + 15f)) < 1f, "downwind of the boundary the deck is a humilis: puff 500 m, wobble 30 m");
            TestAssert.That(Math.Abs(east.DeckTop - east.CloudBase - (2000f + 40f)) < 1f, "upwind it is a cumulonimbus: puff 2000 m, wobble 80 m");
            TestAssert.That(west.MidCover > 0.7f && west.MidBase == 4200f && west.MidTop == 4700f && east.MidCover < 0.01f,
                "the middle deck exists on one side of the boundary only");
            float nearBoundary = field.Sample(0f, 0f).DeckTop - field.Sample(0f, 0f).CloudBase;
            TestAssert.That(nearBoundary > 515f && nearBoundary < 2040f, "on the boundary the deck is between the two genera: " + nearBoundary);
        }

        private static void MapsEncloseTheLocalDeck()
        {
            SkyParams humilis = Deck(500f, 30f, 0f), cb = Deck(2000f, 80f, 0f);
            var fixture = new TapeFixture(BakedTape.TwoKeyframes(humilis, cb, 1500000f, 12000f), new TapeFrame(90f, 1500000f));
            var key = new WeatherKey(73u, 0f, false, (byte)WeatherRegimeType.Overcast);
            CloudMaps maps = CloudMaps.Build(key, 600f, 40000f, 40000f, 12f, 50000f, 220000f, fixture);
            var field = new WeatherField();
            field.Build(key, 600f, 40000f, 40000f, 12f);
            field.UseFixtureTape(fixture.Sampler, fixture.Frame);
            for (int z = 16; z < CloudMaps.NearSize; z += 32)
            for (int x = 16; x < CloudMaps.NearSize; x += 32)
            {
                float u = (x + 0.5f) / CloudMaps.NearSize, v = (z + 0.5f) / CloudMaps.NearSize;
                WeatherPoint cell = CloudBodies.SampleMap(maps.Near, maps.NearProfiles, CloudMaps.NearSize, u, v);
                WeatherPoint truth = field.Sample((u * 2f - 1f) * 50000f, (v * 2f - 1f) * 50000f);
                TestAssert.That(cell.LowTop + 16000f / 255f >= Math.Max(truth.LowTop, truth.DeckTop),
                    "the map's stored top encloses the local row's deck top, so empty-space skipping cannot cut a tower off");
            }
        }

        private static void BodiesReadTheirRowFromTheTape()
        {
            byte[] noise = CloudNoise3D.Generate(64, 47);
            SkyParams withDeck = Deck(500f, 30f, 0.9f), without = Deck(500f, 30f, 0f);
            var tape = new TapeSampler(BakedTape.TwoKeyframes(withDeck, without, 1500000f, 12000f));
            var bodies = new CloudBodies(noise, 64, tape, new TapeFrame(90f, 1500000f));
            int deckWest = 0, deckEast = 0;
            for (int i = 0; i < 60; i++)
            {
                if (bodies.MidLayer(-40000f + i * 311f, 4400f, i * 173f) > 0.02f) deckWest++;
                if (bodies.MidLayer(40000f + i * 311f, 4400f, i * 173f) > 0.02f) deckEast++;
            }
            TestAssert.That(deckWest > 20 && deckEast == 0, "the middle deck is on the west of the boundary only: " + deckWest + " west, " + deckEast + " east");
            var constant = new CloudBodies(noise, 64, withDeck);
            TestAssert.That(constant.MidLayer(-40000f, 4400f, 0f) == bodies.MidLayer(-40000f, 4400f, 0f), "a one-keyframe tape and a row give the same layer");
            TestAssert.That(bodies.MidLayer(-40000f, 3000f, 0f) == 0f && bodies.MidLayer(-40000f, 6000f, 0f) == 0f, "the layer stays in its own altitude band");
        }
    }
}
