using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The console's view of the tape: front lines lie on s = s_k along the meander, ETA is distance over speed while the tape moves and STATIC while it is held,
    /// and the story strip follows the segments around the camera.</summary>
    internal static class MetOfficeTapeFillTests
    {
        public static void Run()
        {
            LinesLieOnTheMarker();
            EtaIsDistanceOverSpeed();
            StripFollowsTheSegments();
        }

        private static (WeatherTape, WeatherKey) Frontal(float offset = 0f)
        {
            var key = new WeatherKey(77u, 0f, true, 1, story: 0, headingDeg: 200f, offset: offset, systemSpeed: 30f);
            return (WeatherTape.For(key), key);
        }

        private static void LinesLieOnTheMarker()
        {
            (WeatherTape tape, WeatherKey key) = Frontal(5000f);
            TapeFrame frame = tape.Frame(key.HeadingDeg, key.Offset);
            var f = new MetFront();
            TapeMarker marker = tape.Markers.Items[0];
            MetOfficeTapeFill.Line(marker, frame, f);
            for (int i = 0; i < f.Count; i++)
                TestAssert.That(Math.Abs(frame.S(f.X[i] * 1000f, f.Z[i] * 1000f) - marker.S) < 2f, "every polyline point stands on the marker's line (point " + i + ")");
            TestAssert.That(Math.Abs(f.DirX + frame.UX) < 1e-5f && f.Kind == MetOfficeTapeFill.KindOf(marker.Kind), "the line travels downwind");
        }

        private static void EtaIsDistanceOverSpeed()
        {
            (WeatherTape tape, WeatherKey key) = Frontal();
            TapeMarker marker = tape.Markers.Items[0];
            var m = new MetOfficeModel();
            // The camera at the origin stands 60 km downwind of the marker along the tape: D = s_k - 60 km - M(0).
            float meander = tape.Frame(key.HeadingDeg, 0f).Meander(0f);
            TapeFrame frame = tape.Frame(key.HeadingDeg, marker.S - 60000f - meander);
            MetOfficeTapeFill.Fronts(m, tape, frame, 0f, 0f, 30f, true);
            TestAssert.That(m.FrontCount >= 1 && Math.Abs(m.Fronts[0].EtaMinutes - 60000f / 30f / 60f) < 0.01f, "ETA = distance along the tape / speed (got " + m.Fronts[0].EtaMinutes + ")");
            MetOfficeTapeFill.Fronts(m, tape, frame, 0f, 0f, 30f, false);
            TestAssert.That(float.IsNaN(m.Fronts[0].EtaMinutes) && MetOfficeText.Eta(m.Fronts[0].EtaMinutes) == "STATIC", "a held tape reads STATIC");
        }

        private static void StripFollowsTheSegments()
        {
            (WeatherTape tape, WeatherKey key) = Frontal();
            var m = new MetOfficeModel();
            float sCam = tape.Segments[3].S0 + 5000f;
            MetOfficeTapeFill.Story(m, tape, sCam, 30f, true);
            TestAssert.That(m.HasStory && m.BlockCount >= 2 && m.BlockCount <= MetOfficeModel.MaxBlocks && m.StoryName == "Frontal system" && m.SkyRole == tape.Segments[3].Label, "story name and role under the cursor");
            TestAssert.That(m.Blocks[0].FromMin >= -93f && m.Blocks[m.BlockCount - 1].ToMin <= 93f && m.Segments[0].Present, "blocks stay inside the strip, pips are filled");
            MetOfficeTapeFill.Story(m, tape, sCam, 30f, false);
            TestAssert.That(m.StripNote == "HELD", "a held tape says HELD");
        }
    }
}
