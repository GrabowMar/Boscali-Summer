using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>The ORBIT page and station: fixed layout arithmetic, track ids, bird cells, button labels, per-surface paging.</summary>
    internal static class OrbitTests
    {
        public static void Run()
        {
            Layouts();
            Words();
            Paging();
        }

        private static void Inside(FeedBox b, float w, float h, string name) =>
            TestAssert.That(b.X >= -0.01f && b.Y >= -0.01f && b.Right <= w + 0.01f && b.Bottom <= h + 0.01f && b.W > 0f && b.H > 0f,
                name + " inside the board " + b.X + "," + b.Y + " " + b.W + "x" + b.H);

        private static bool Apart(FeedBox a, FeedBox b) =>
            a.Right <= b.X + 0.01f || b.Right <= a.X + 0.01f || a.Bottom <= b.Y + 0.01f || b.Bottom <= a.Y + 0.01f;

        private static void Layouts()
        {
            // The ORBIT page under the chrome: 896 console -> 766 high, 596 console -> 466 high.
            foreach (float h in new[] { 466f, 766f })
            {
                SpaceFeedLayout l = SpaceFeedLayout.Compute(480f, h, false);
                string tag = "page " + h + ": ";
                TestAssert.That(!l.Full, tag + "not the station");
                var boxes = new List<FeedBox> { l.Threat, l.Constellation, l.Sensor, l.Track, l.Buttons, l.Tasked };
                for (int i = 0; i < boxes.Count; i++)
                {
                    Inside(boxes[i], 480f, h, tag + "box " + i);
                    for (int j = i + 1; j < boxes.Count; j++) TestAssert.That(Apart(boxes[i], boxes[j]), tag + "boxes " + i + "/" + j + " do not overlap");
                }
                TestAssert.That(l.Bottom <= h + 0.01f && l.Bottom >= h - 8f, tag + "the page fits with no dead band: " + l.Bottom);
                TestAssert.That(l.Image.W == 478f && l.Image.H >= 100f, tag + "the picture keeps a usable size: " + l.Image.W + "x" + l.Image.H);
                TestAssert.That(l.Toolbar.Bottom <= l.Image.Y && l.Toolbar.Y > l.Sensor.Y, tag + "the toolbar sits between the header and the picture");
                TestAssert.That(l.Image.Bottom <= l.Sensor.Bottom, tag + "the picture is inside its frame");
                TestAssert.That(l.CardRows >= 1 && l.TrackRows >= 4, tag + "at least 1 TASKED and 4 track rows");
                for (int i = 0; i < l.TrackRows; i++) { Inside(l.TrackRow(i), 480f, h, tag + "track row " + i); TestAssert.That(l.TrackRow(i).Bottom <= l.Track.Bottom, tag + "track row in its box"); }
                for (int i = 0; i < l.CardRows; i++) { Inside(l.CardRow(i), 480f, h, tag + "card row " + i); TestAssert.That(l.CardRow(i).Bottom <= l.Tasked.Bottom, tag + "card row in its box"); }
                for (int i = 0; i < 3; i++) Inside(l.BirdCell(i), 480f, h, tag + "bird cell " + i);
                TestAssert.That(Apart(l.ActionButton(0), l.ActionButton(1)), tag + "the two buttons do not overlap");
                TestAssert.That(l.Threat.H >= 22f && l.Buttons.H >= 28f && l.TrackRow(0).H >= 24f, tag + "rows are tall enough for 10 px words");
                TestAssert.That(l.ConsoleLines == 0, tag + "no console on the page (it lives on CAP)");
            }
            SpaceFeedLayout tall = SpaceFeedLayout.Compute(480f, 766f, false), shortPage = SpaceFeedLayout.Compute(480f, 466f, false);
            TestAssert.That(tall.Art && !shortPage.Art, "the orbit art shows on the tall page only");
            TestAssert.That(tall.TrackRows == 5 && shortPage.TrackRows == 4, "5 track rows tall, 4 short");
            TestAssert.That(shortPage.CardRows == 1 && tall.CardRows >= 3, "one TASKED row short, three or more tall");
            TestAssert.That(tall.Image.H > shortPage.Image.H + 30f, "a taller page gives the picture the height");

            // The station below its chrome: 1858 x 806.
            SpaceFeedLayout f = SpaceFeedLayout.Compute(1858f, 806f, true);
            TestAssert.That(f.Full && f.Art, "station flags");
            var all = new List<FeedBox> { f.Threat, f.Constellation, f.Sensor, f.Track, f.Buttons, f.Tasked };
            if (f.ConsoleLines > 0) all.Add(f.Console);
            for (int i = 0; i < all.Count; i++)
            {
                Inside(all[i], 1858f, 806f, "station box " + i);
                for (int j = i + 1; j < all.Count; j++) TestAssert.That(Apart(all[i], all[j]), "station boxes " + i + "/" + j + " do not overlap");
            }
            TestAssert.That(f.Bottom <= 806.01f, "the station fits: " + f.Bottom);
            TestAssert.That(f.Sensor.W / 1858f > 0.7f && f.Sensor.W / 1858f < 0.78f, "the sensor frame takes about 74% of the width: " + f.Sensor.W / 1858f);
            TestAssert.That(f.Threat.H == 36f && f.Threat.W == 1858f, "the warning bar is 36 px and spans the screen");
            TestAssert.That(f.TrackRows == 6 && f.CardRows == 3, "six tracks, three posts");
            TestAssert.That(f.ConsoleLines >= 8, "the console fills the rest of the column: " + f.ConsoleLines + " lines");
            TestAssert.That(f.Console.Bottom <= 806.01f && f.Console.X == f.Track.X, "the console sits in the control column");
            SpaceFeedLayout wide = SpaceFeedLayout.Compute(2440f, 806f, true);
            TestAssert.That(wide.Sensor.W > f.Sensor.W && wide.Track.W == f.Track.W, "an ultrawide screen gives the picture the extra width");
        }

        private static void Words()
        {
            TestAssert.That(C2Orbit.TrackId(7) == "T7" && C2Orbit.TrackId(1234) == "T1234", "track id is the wire id");
            TestAssert.That(C2Orbit.ConfirmLabel(0, false) == "CONFIRM", "confirm with nothing selected");
            TestAssert.That(C2Orbit.ConfirmLabel(12, false) == "CONFIRM T12", "confirm names the selected track");
            TestAssert.That(C2Orbit.ConfirmLabel(12, true) == "MARKS FULL", "confirm at twelve marks");
            TestAssert.That(C2Orbit.TransmitLabel(0) == "TRANSMIT" && C2Orbit.TransmitLabel(1) == "TRANSMIT 1 MARK" && C2Orbit.TransmitLabel(3) == "TRANSMIT 3 MARKS", "transmit labels");
            TestAssert.That(C2Orbit.PageMeta(0, 2) == "PAGE 1/2" && C2Orbit.PageMeta(0, 0) == "PAGE 1/1", "page meta");
            TestAssert.That(C2Orbit.SensorMeta(BirdKind.Optical, 1, true) == "OPTICAL · ZOOM MID", "optical meta");
            TestAssert.That(C2Orbit.SensorMeta(BirdKind.Radar, 0, false) == "RADAR · SAR", "radar meta");
            TestAssert.That(C2Orbit.TaskedMeta(2).StartsWith("CLAIM") && C2Orbit.TaskedMeta(0).StartsWith("NOTHING POSTED"), "tasked meta");
            TestAssert.That(C2Orbit.BirdName(BirdKind.Kinetic) == "KINETIC", "bird name");
            TestAssert.That(C2Orbit.Telemetry(BirdKind.Optical) == C2Orbit.Telemetry(BirdKind.Optical) && C2Orbit.Telemetry(BirdKind.Optical) != C2Orbit.Telemetry(BirdKind.Radar), "telemetry is fixed per bird");

            C2Tone tone;
            TestAssert.That(C2Orbit.BirdState(BirdKind.Optical, false, true, SpaceFamilyState.Normal, 0, false, out tone) == "NO LINK" && tone == C2Tone.Danger, "no link");
            TestAssert.That(C2Orbit.BirdState(BirdKind.Optical, true, false, SpaceFamilyState.Normal, 0, false, out tone) == "NO BIRD" && tone == C2Tone.Danger, "no bird");
            TestAssert.That(C2Orbit.BirdState(BirdKind.Kinetic, true, true, SpaceFamilyState.Dark, 0, false, out tone) == "OFFLINE" && tone == C2Tone.Danger, "dark family");
            TestAssert.That(C2Orbit.BirdState(BirdKind.Optical, true, true, SpaceFamilyState.Normal, 0, false, out tone) == "READY" && tone == C2Tone.Info, "optical ready");
            TestAssert.That(C2Orbit.BirdState(BirdKind.Radar, true, true, SpaceFamilyState.Normal, 75, false, out tone) == "1:15" && tone == C2Tone.Warn, "radar counts down");
            TestAssert.That(C2Orbit.BirdState(BirdKind.Radar, true, true, SpaceFamilyState.Normal, 0, true, out tone) == "UNAVAIL" && tone == C2Tone.Warn, "radar unavailable");
            TestAssert.That(C2Orbit.BirdState(BirdKind.Optical, true, true, SpaceFamilyState.Degraded, 99, true, out tone) == "DEGRADED" && tone == C2Tone.Warn, "optical ignores the radar timer");
            TestAssert.That(C2Orbit.ConstellationMeta(true, true, 2, 3, SpaceFamilyState.Degraded) == "UPLINKS 2/3 · DEGRADED", "constellation meta");
            TestAssert.That(C2Orbit.ConstellationMeta(true, false, 0, 0, SpaceFamilyState.Normal) == "NO SPACE LINK" && C2Orbit.ConstellationMeta(false, false, 0, 0, SpaceFamilyState.Normal) == "NO HOST LINK", "constellation meta without a link");
        }

        private static void Paging()
        {
            TestAssert.That(SpaceFeedRules.PageCount(0, 5) == 1 && SpaceFeedRules.PageCount(5, 5) == 1 && SpaceFeedRules.PageCount(6, 5) == 2, "five per page");
            TestAssert.That(SpaceFeedRules.PageCount(13, 4) == 4 && SpaceFeedRules.PageCount(13, 6) == 3, "four and six per page");
            TestAssert.That(SpaceFeedRules.ClampPage(9, 13, 6) == 2 && SpaceFeedRules.ClampPage(-1, 13, 6) == 0, "the page index is clamped per surface");
            TestAssert.That(SpaceFeedRules.PageCount(7) == SpaceFeedRules.PageCount(7, SpaceFeedRules.ContactsPerPage), "the default page size is six");
        }
    }
}
