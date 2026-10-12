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
            FieldReadsTheBakedTape();
            FixtureTapeVariesTheGenusAndLayers();
            MapsEncloseTheLocalDeck();
            BodiesReadTheirRowFromTheTape();
        }

        private static void FieldReadsTheBakedTape()
        {
            foreach (int sky in new[] { 0, 7, 36 })
            {
                var field = new WeatherField();
                field.Build(new WeatherKey(73u, 0f, false, 2, skyPin: (byte)sky), 600f, 40000f, 40000f);
                TestAssert.That(field.Tape.IsValid && !field.Tape.IsConstant && field.Tape.Tape.Texels == BakedTape.FullTexels && ReferenceEquals(field.Tape.Tape, field.TapeSource.Baked), "the live field reads the tape's baked array");
                float s = field.Frame.S(5000f, -3000f);
                TestAssert.That(field.Tape.At(s).CloudBase == field.RowAt(5000f, -3000f).CloudBase, "RowAt is the baked row at that s");
            }
            var again = new WeatherField();
            var key = new WeatherKey(73u, 0f, false, (byte)WeatherRegimeType.Broken);
            again.Build(key, 600f, 40000f, 40000f);
            WeatherPoint before = again.Sample(1000f, 1000f);
            again.UseFixtureTape(TapeSampler.Constant(WithLobe(again.RowAt(0f, 0f), 5f)), new TapeFrame(0f, 0f));
            again.Build(key, 600f, 40000f, 40000f);
            TestAssert.That(!again.Tape.IsConstant && again.Sample(1000f, 1000f).DeckTop == before.DeckTop, "the next Build restores the baked tape");
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
