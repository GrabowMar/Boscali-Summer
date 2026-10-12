using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The ENV briefing (spec 8.4): sky name, METAR, story, the next marker as a distance and a bearing along the wind, HELD, AT POSITION, and the forecast read from the tape.</summary>
    internal static class EnvBriefingTests
    {
        public static void Run()
        {
            NextMarkerIsDistanceAndBearing();
            NoFrontAheadOnAHighPressureTape();
            SkyStoryAndMetarAreTheLocalOnes();
            HeldAndAtPositionFlags();
            ForecastIsReadFromTheTapeAtEachOffset();
        }

        private static WeatherKey Key(uint seed, byte story, float heading, float offset, bool dynamic = false, float speed = 30f) =>
            new WeatherKey(seed, 0f, dynamic, 1, story: story, headingDeg: heading, offset: offset, systemSpeed: speed);

        /// <summary>A key whose theater centre stands <paramref name="kmBefore"/> km (across the wavy line) before marker <paramref name="marker"/>.</summary>
        private static WeatherKey Before(uint seed, byte story, float heading, int marker, float kmBefore, out WeatherTape tape)
        {
            tape = WeatherTape.For(Key(seed, story, heading, 0f));
            TapeFrame zero = tape.Frame(heading, 0f);
            float slope = zero.MeanderSlope(zero.Across(0f, 0f));
            return Key(seed, story, heading, tape.Markers.Items[marker].S - kmBefore * 1000f * (float)Math.Sqrt(1f + slope * slope) - zero.Meander(0f));
        }

        private static void NextMarkerIsDistanceAndBearing()
        {
            foreach (float heading in new[] { 270f, 90f, 0f, 200f })
            {
                WeatherKey key = Before(11u, 1, heading, 0, 62f, out WeatherTape tape);
                var field = new WeatherField();
                field.Build(key, 0f, 81920f, 40960f);
                EnvBriefing b = EnvBriefings.Build(tape, field.Frame, field.Sample(0f, 0f), 0f, 0f, 0f, false);
                string word = MetOfficeText.FrontWord(MetOfficeTapeFill.KindOf(tape.Markers.Items[0].Kind));
                string want = word + " 62 KM " + WeatherWords.Cardinal(heading);
                TestAssert.That(b.NextMarker == want, "the next marker reads '" + want + "' (got '" + b.NextMarker + "', heading " + heading + ")");
            }
            WeatherKey near = Before(11u, 1, 270f, 0, 9f, out WeatherTape t2);
            var f2 = new WeatherField();
            f2.Build(near, 0f, 81920f, 40960f);
            TestAssert.That(EnvBriefings.Build(t2, f2.Frame, f2.Sample(0f, 0f), 0f, 0f, 0f, false).NextMarker.EndsWith(" 9 KM W"), "the distance follows the aircraft, not the theater centre");
            // The aircraft 4 km upwind of the centre is 4 km nearer the line; one 4 km downwind is 4 km farther.
            WeatherMath.HeadingToVector(270f, out float ux, out float uz);
            string closer = EnvBriefings.Build(t2, f2.Frame, f2.Sample(ux * 4000f, uz * 4000f), ux * 4000f, uz * 4000f, 0f, false).NextMarker;
            string farther = EnvBriefings.Build(t2, f2.Frame, f2.Sample(-ux * 4000f, -uz * 4000f), -ux * 4000f, -uz * 4000f, 0f, false).NextMarker;
            TestAssert.That(closer.Contains(" 5 KM ") && farther.Contains(" 13 KM "), "4 km along the wind moves the distance by 4 km (" + closer + " / " + farther + ")");
        }

        private static void NoFrontAheadOnAHighPressureTape()
        {
            WeatherKey key = Key(5u, 4, 250f, 120000f);
            WeatherTape tape = WeatherTape.For(key);
            TestAssert.That(tape.Markers.Count == 0, "the high-pressure story has no markers");
            var field = new WeatherField();
            field.Build(key, 0f, 81920f, 40960f);
            TestAssert.That(EnvBriefings.Build(tape, field.Frame, field.Sample(0f, 0f), 0f, 0f, 0f, false).NextMarker == EnvBriefings.NoFront, "no marker, no front ahead");
        }

        private static void SkyStoryAndMetarAreTheLocalOnes()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                WeatherKey key = Key(seed, 0, 250f, 40000f * seed);
                var field = new WeatherField();
                field.Build(key, 0f, 81920f, 40960f);
                WeatherTape tape = field.TapeSource;
                WeatherPoint at = field.Sample(1000f, 2000f);
                EnvBriefing b = EnvBriefings.Build(tape, field.Frame, at, 1000f, 2000f, 0f, false);
                int sky = tape.SkyIndexAt(field.Frame.S(1000f, 2000f));
                TestAssert.That(b.SkyNumber == sky + 1 && b.SkyName == SkyLibrary.Get(sky).Name, "the sky is the library sky under the aircraft");
                TestAssert.That(b.Metar == WeatherMetar.Line(at) && b.Metar.Length > 0, "the METAR is the line of the local layers");
                TestAssert.That(b.Story == "Frontal system" || b.Story == "Opening", "the story is the story of the segment (" + b.Story + ")");
                TestAssert.That(b.Role.Length > 0, "the segment's role is named");
            }
        }

        private static void HeldAndAtPositionFlags()
        {
            WeatherKey key = Key(3u, 0, 250f, 100000f);
            var field = new WeatherField();
            field.Build(key, 0f, 81920f, 40960f);
            WeatherPoint at = field.Sample(0f, 0f);
            EnvBriefing held = EnvBriefings.Build(field.TapeSource, field.Frame, at, 0f, 0f, 39.9f, false);
            EnvBriefing moving = EnvBriefings.Build(field.TapeSource, field.Frame, at, 0f, 0f, 40.1f, true);
            TestAssert.That(held.Held && held.AtPosition && !moving.Held && !moving.AtPosition, "HELD follows the tape, AT POSITION shows under about 40 m/s over the ground");
        }

        private static void ForecastIsReadFromTheTapeAtEachOffset()
        {
            int[] offsets = WeatherForecast.DefaultOffsetsMinutes;
            WeatherKey key = Before(7u, 0, 250f, 0, 40f, out _);
            WeatherKey running = Key(7u, 0, 250f, key.Offset, true, 30f);
            var scratch = new WeatherField();
            var movingSteps = new ForecastStep[offsets.Length]; var heldSteps = new ForecastStep[offsets.Length];
            EnvForecast.Fill(movingSteps, scratch, running, 0f, 81920f, 40960f, 12f, offsets, 0f, 0f, true, null);
            EnvForecast.Fill(heldSteps, scratch, running, 0f, 81920f, 40960f, 12f, offsets, 0f, 0f, false, null);
            bool changes = false;
            for (int i = 1; i < offsets.Length; i++)
            {
                TestAssert.That(heldSteps[i].Conditions == heldSteps[0].Conditions && heldSteps[i].CloudDeckMetres == heldSteps[0].CloudDeckMetres && heldSteps[i].Regime.Type == heldSteps[0].Regime.Type,
                    "a held tape forecasts the same sky at every offset (the screen says HELD)");
                if (movingSteps[i].Conditions != movingSteps[0].Conditions || movingSteps[i].Regime.Type != movingSteps[0].Regime.Type) changes = true;
            }
            TestAssert.That(changes, "a moving tape forecasts the front coming: the sky differs across the hour");
            TestAssert.That(movingSteps[0].OffsetMinutes == 0 && movingSteps[5].OffsetMinutes == 60, "the offsets are the default ones");
            var now = new WeatherField();
            now.Build(running, 0f, 81920f, 40960f, 12f, 1f, true);
            TestAssert.That(movingSteps[0].Conditions == now.Sample(0f, 0f).SkyCover, "offset 0 is the sky now");
            var forced = new ForecastStep[offsets.Length];
            EnvForecast.Fill(forced, scratch, running, 0f, 81920f, 40960f, 12f, offsets, 0f, 0f, true, 0.4f);
            TestAssert.That(forced[3].RainProbability == 0.4f, "a forced rain overrides the rain bar");
        }
    }
}
