using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The words, the chart geometry, the sky picker and the log of the Met Office window: everything it prints or draws that needs no engine.</summary>
    internal static class MetOfficeChartTests
    {
        public static void Run()
        {
            Words();
            LogLinesFitTheCard();
            ChartMapping();
            WindBarb();
            LabelsNeverCollideInACorner();
            NearestFront();
            NextFrontsAreNearestFirst();
            FrontSymbolsFollowTheLine();
            SkyPickerWalksClasses();
            LogKeepsFourNewestFirst();
        }

        private static MetFront Line(MetOfficeModel m, int slot, MetFrontKind kind, params float[] xz)
        {
            MetFront f = m.Fronts[slot];
            f.Kind = kind; f.Count = xz.Length / 2; f.EtaMinutes = float.NaN;
            for (int i = 0; i < f.Count; i++) { f.X[i] = xz[2 * i]; f.Z[i] = xz[2 * i + 1]; }
            m.FrontCount = Math.Max(m.FrontCount, slot + 1);
            return f;
        }

        private static void Words()
        {
            TestAssert.Eq(MetOfficeText.PaceValue(3, TapePace.Speed(3, 12f)), "x3 · 27 M/S", "pace 3 at 12 m/s wind is 27 m/s");
            TestAssert.Eq(MetOfficeText.TapeLine(false, 2, 20f), "HELD // TAPE x2 · 20 M/S", "held tape line");
            TestAssert.Eq(MetOfficeText.HoldLabel(true) + "/" + MetOfficeText.HoldLabel(false), "HOLD TAPE/RESUME TAPE", "the hold button names its next action");
            TestAssert.Eq(MetOfficeText.Where(62.4f, 270f), "62 KM W", "front distance and bearing words");
            TestAssert.Eq(MetOfficeText.Eta(30f) + "|" + MetOfficeText.Eta(float.NaN), "ETA 30'|STATIC", "a still front reads STATIC");
            TestAssert.Eq(MetOfficeText.FrontWord(MetFrontKind.Cold), "COLD FRONT", "front word");
            TestAssert.Eq(MetOfficeText.OutlookLabel(0) + MetOfficeText.OutlookLabel(15), "NOW+15", "outlook labels");
            TestAssert.Eq(MetOfficeText.PaceValue(3, float.NaN), "x3 · -- M/S", "an unknown speed prints the kit no-data mark, never NaN");
            TestAssert.Eq(MetOfficeText.PaceValue(0, 40f), "40 M/S", "a client does not know the host's pace: the speed alone, no x<pace>");
            TestAssert.Eq(MetOfficeText.TapeLine(true, 0, 40f), "RUNNING // TAPE 40 M/S", "so the tape line of a client has no pace either");
            TestAssert.Eq(MetOfficeText.PaceRow(3, 27f, 12f), "x3 · 27 M/S · WIND 12", "the pace row shows the wind the speed was scaled by (mockup 'wind 12')");
            TestAssert.Eq(MetOfficeText.PaceRow(0, 40f, 12f), "40 M/S · WIND 12", "and so does a client's");
            TestAssert.Eq(MetOfficeText.AnchorLabel(0.2f) + "|" + MetOfficeText.AnchorLabel(30.4f), "ANCHOR|ANCHOR +30 KM", "the anchor label carries its offset (mockup '+30 KM')");
            TestAssert.Eq(MetOfficeText.RerollLine(255), "NEXT RE-ROLL ▲ CUT · LAYOUT 255→0", "the identity card says what RE-ROLL will do, in words and the glyph");
            TestAssert.That(!MetOfficeText.WindLine(0, float.NaN, float.NaN).Contains("NaN") && MetOfficeText.Cover(float.NaN) == "—", "unknown wind and cover print marks");
            TestAssert.Eq(MetOfficeText.Hex8(0x9A3F21C7u), "9A3F21C7", "seed shows as hex");
            TestAssert.Eq(MetOfficeText.LayerGroup(0.5f, 3200f), "BKN105", "METAR layer group: 0.50 is BKN, 3200 m is 105 hundreds of feet");
            TestAssert.Eq(MetOfficeText.LayerGroup(0.02f, 3200f), "", "an empty layer has no group");
            TestAssert.Eq(MetOfficeText.BaseKm(8300f), "8.3 KM", "layer base in km");
            TestAssert.Eq(MetOfficeText.StepperValue(27, "BKN", 6, 7), "#27 BKN 6 / 7", "sky stepper value");
            TestAssert.Eq(MetOfficeText.RerollSub(255), "255→0", "layout salt wraps like the byte it is");
            TestAssert.Eq(MetOfficeText.MissionClock(2467f), "T+00:41:07", "mission clock");
            TestAssert.Eq(MetOfficeText.LogLine(2528f, "HOST", "PACE 2 → 3"), "00:42:08 HOST PACE 2 → 3", "log line");
            TestAssert.That(MetOfficeText.Dtg(new DateTime(2026, 10, 11, 15, 38, 0, DateTimeKind.Utc)) == "111538Z OCT 26", "DTG is cosmetic but well formed");
            TestAssert.Eq(MetOfficeText.WindLine(270, 12f, 23.3f), "FROM 270° · 12 M/S 23 KT", "wind line");
            var m = new MetOfficeModel { HasAnchor = false };
            TestAssert.Eq(MetOfficeText.AnchorChoice(m), 2, "no anchor reads DEFAULT");
            m.HasAnchor = true; m.AnchorMark = 0; m.CamX = 40f; m.CamZ = 90f;
            TestAssert.Eq(MetOfficeText.AnchorChoice(m), 0, "HERE stays latched however far the camera has flown since: the choice is recorded, not recomputed");
            m.AnchorMark = 1;
            TestAssert.Eq(MetOfficeText.AnchorChoice(m), 1, "and so does 30 KM AHEAD");
            m.AnchorMark = -1;
            TestAssert.Eq(MetOfficeText.AnchorChoice(m), -1, "an anchor the adapter did not place (a scenario, a client) latches nothing");
        }

        private static void LogLinesFitTheCard()
        {
            var notes = new System.Collections.Generic.List<string>();
            notes.Add(MetOfficeText.HoldNote(true)); notes.Add(MetOfficeText.HoldNote(false)); notes.Add(MetOfficeText.AutoNote()); notes.Add(MetOfficeText.DefaultNote());
            for (int sky = 0; sky < SkyLibrary.Count; sky++) notes.Add(MetOfficeText.PinNote(sky));
            for (int a = 1; a <= 6; a++) for (int b = 1; b <= 6; b++) notes.Add(MetOfficeText.PaceNote(a, b));
            for (int salt = 0; salt < 256; salt += 51) notes.Add(MetOfficeText.RerollNote(salt));
            notes.Add(MetOfficeText.RerollNote(255));
            for (int i = 0; i < MetOfficeModel.SetCount; i++) { notes.Add(MetOfficeText.SetNote(i, true)); notes.Add(MetOfficeText.SetNote(i, false)); }
            foreach (float metres in new[] { 0f, 30000f, 150000f, 300000f }) notes.Add(MetOfficeText.AnchorNote(metres));
            foreach (float? rain in MetCatalog.RainValues) notes.Add(MetOfficeText.RainNote(rain));
            for (int i = 0; i < MetCatalog.Scenarios.Length; i++) notes.Add(MetOfficeText.ScenarioNote(i));
            foreach (string what in notes)
            {
                TestAssert.That(what.Length > 0 && what.Length <= MetOfficeText.LogMaxWhat, "log note '" + what + "' is " + what.Length + " characters (limit " + MetOfficeText.LogMaxWhat + ")");
                string line = MetOfficeText.LogLine(35999f, "HOST", what);
                TestAssert.That(line.Length <= MetOfficeText.LogColumns && line.EndsWith(what), "'" + line + "' fits the 43 columns of the log card (" + line.Length + ")");
            }
            TestAssert.Eq(MetOfficeText.LogLine(1f, "HOST", new string('X', 60)).Length, 14 + MetOfficeText.LogMaxWhat, "a longer note is cut, never allowed to wrap out of the card");
            TestAssert.Eq(MetOfficeText.PinNote(0).Substring(0, 5), "PIN #", "pin notes name the number and the class code");
        }

        private static void ChartMapping()
        {
            TestAssert.Near(MetChartMath.PxX(0f, 300f), 150f, "centre is the middle");
            TestAssert.Near(MetChartMath.PxX(-150f, 300f), 0f, "west edge");
            TestAssert.Near(MetChartMath.PxX(150f, 300f), 300f, "east is right");
            TestAssert.Near(MetChartMath.PxY(150f, 300f), 0f, "north is up");
            TestAssert.Near(MetChartMath.PxY(-150f, 300f), 300f, "south is down");
            TestAssert.That(MetChartMath.Inside(149f, -149f) && !MetChartMath.Inside(151f, 0f), "range");
            TestAssert.Near(MetChartMath.Clamp(400f), 150f, "clamp");
            TestAssert.That(MetChartMath.Clamp(float.NaN) == 0f && MetChartMath.Clamp(float.PositiveInfinity) == 0f && MetChartMath.Clamp(float.NegativeInfinity) == 0f,
                "a non-finite coordinate sits at the centre, never in a vertex as NaN");
            TestAssert.That(!float.IsNaN(MetChartMath.PxX(float.NaN, 300f)) && !float.IsNaN(MetChartMath.PxY(float.NaN, 300f)) && MetChartMath.PxX(float.NaN, 300f) == 150f, "PxX and PxY are finite for NaN");
        }

        private static void WindBarb()
        {
            MetChartMath.Barb(23.3f, out int p, out int f, out bool h);
            TestAssert.That(p == 0 && f == 2 && h, "23 kt is 25 kt: two full barbs and a half (the mockup 270 / 12 m/s)");
            MetChartMath.Barb(0f, out p, out f, out h);
            TestAssert.That(p == 0 && f == 0 && !h, "calm draws nothing");
            MetChartMath.Barb(62f, out p, out f, out h);
            TestAssert.That(p == 1 && f == 1 && !h, "60 kt is a pennant and a full barb");
            MetChartMath.Barb(-5f, out p, out f, out h);
            TestAssert.That(p == 0 && f == 0 && !h, "a negative wind is calm");
            MetChartMath.Barb(float.NaN, out p, out f, out h);
            TestAssert.That(p == 0 && f == 0 && !h, "an unknown wind is calm, not a garbage int");
            MetChartMath.Barb(float.PositiveInfinity, out p, out f, out h);
            TestAssert.That(p == 0 && f == 0 && !h, "an infinite wind is calm");
            // The shaft points to where the wind blows FROM (chart x right, y down), so the barb is never a picture of one wind only.
            foreach (var c in new[] { new[] { 270, -1, 0 }, new[] { 90, 1, 0 }, new[] { 0, 0, -1 }, new[] { 180, 0, 1 }, new[] { -90, -1, 0 }, new[] { 450, 1, 0 }, new[] { 360, 0, -1 } })
            {
                MetChartMath.BarbAxis(c[0], out float dx, out float dy);
                TestAssert.That(Math.Abs(dx - c[1]) < 1e-4f && Math.Abs(dy - c[2]) < 1e-4f, "wind from " + c[0] + " points the shaft to chart (" + c[1] + ", " + c[2] + "), got (" + dx + ", " + dy + ")");
            }
            MetChartMath.BarbAxis(45, out float ex, out float ey);
            TestAssert.That(ex > 0.7f && ey < -0.7f, "a north-east wind points the shaft up and to the right");
        }

        /// <summary>Review Focus 5: a storm eye, its lenticulars and a supercell in the north-east corner, the anchor standing on the camera, next to the wind box. No two labels share a pixel and all stay on the plot.</summary>
        private static void LabelsNeverCollideInACorner()
        {
            const float S = 300f;
            var bounds = new MetBox(2f, 2f, S - 4f, S - 33f);
            var wind = new MetBox(S - 176f, 3f, 173f, 58f);
            var taken = new MetBox[12];
            taken[0] = wind;
            int n = 1;
            var placed = new System.Collections.Generic.List<MetBox>();
            // camera and anchor on the same pixel (ANCHOR HERE); then the eye, its lenticulars 30 km east of it and a supercell next to both, all at 100 %.
            float[] px = { 268f, 268f, 268f, 296f, 281f }, py = { 28f, 28f, 28f, 28f, 41f }, w = { 70f, 96f, 104f, 104f, 112f };
            bool[] below = { true, false, false, false, false };
            for (int i = 0; i < px.Length; i++)
            {
                MetBox b = MetChartMath.Place(px[i], py[i], w[i], 16f, below[i], bounds, taken, n);
                taken[n++] = b;
                placed.Add(b);
                TestAssert.That(b.X >= bounds.X - 0.01f && b.Right <= bounds.Right + 0.01f && b.Y >= bounds.Y - 0.01f && b.Bottom <= bounds.Bottom + 0.01f, "label " + i + " stays on the plot");
                TestAssert.That(MetChartMath.Overlap(b, wind) <= 0f, "label " + i + " stays off the wind box");
            }
            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                    TestAssert.That(MetChartMath.Overlap(placed[i], placed[j]) <= 0f, "labels " + i + " and " + j + " do not overlap");
            // ANCHOR HERE in the middle of the chart: the camera label goes below its marker, the anchor label above it.
            var mid = new MetBox[3];
            mid[0] = wind;
            MetBox camBox = MetChartMath.Place(150f, 150f, 70f, 16f, true, bounds, mid, 1);
            mid[1] = camBox;
            MetBox anchorBox = MetChartMath.Place(150f, 150f, 96f, 16f, false, bounds, mid, 2);
            TestAssert.That(anchorBox.Y < camBox.Y && MetChartMath.Overlap(camBox, anchorBox) <= 0f, "the anchor label sits opposite the camera label when both stand on one point");
            var lone = MetChartMath.Place(150f, 150f, 100f, 16f, false, bounds, taken, 1);
            TestAssert.That(lone.X > 150f && lone.Y < 150f, "an uncrowded label keeps its first choice: right of its marker and above it");
            var edge = MetChartMath.Place(float.NaN, float.NaN, 100f, 16f, false, bounds, taken, 1);
            TestAssert.That(!float.IsNaN(edge.X) && !float.IsNaN(edge.Y), "a NaN marker still gets a finite label box");
        }

        private static void NearestFront()
        {
            var m = new MetOfficeModel();
            MetFront f = Line(m, 0, MetFrontKind.Cold, -60f, -150f, -60f, 150f);
            TestAssert.That(MetChartMath.Nearest(0f, 0f, f, out float d, out float b), "a line has a nearest point");
            TestAssert.Near(d, 60f, "distance to a north-south line 60 km west");
            TestAssert.Near(b, 270f, "bearing west");
            MetChartMath.Nearest(-60f, 400f, f, out d, out b);
            TestAssert.Near(d, 250f, "the nearest point clamps to the end of the segment");
            TestAssert.That(!MetChartMath.Nearest(0f, 0f, new MetFront { Count = 1 }, out _, out _), "a single point is not a line");
            MetChartMath.Nearest(float.NaN, float.NaN, f, out d, out b);
            TestAssert.That(d < 1000f && !float.IsNaN(b), "an unknown camera is the chart centre, not a distance of float.MaxValue");
            MetChartMath.Nearest(-60f, 0f, f, out d, out b);
            TestAssert.Near(d, 0f, "standing on the line is distance zero");
        }

        private static void NextFrontsAreNearestFirst()
        {
            var m = new MetOfficeModel();
            Line(m, 0, MetFrontKind.Cold, -106f, -150f, -106f, 150f);
            Line(m, 1, MetFrontKind.Warm, -48f, -150f, -48f, 150f);
            Line(m, 2, MetFrontKind.Squall, 20f, -150f, 20f, 150f);
            var order = new int[4]; var dist = new float[4];
            int n = MetChartMath.NextFronts(m, order, dist);
            TestAssert.That(n == 3 && order[0] == 2 && order[1] == 1 && order[2] == 0, "fronts list nearest first");
            TestAssert.Near(dist[0], 20f, "distances travel with the order");
        }

        private static void FrontSymbolsFollowTheLine()
        {
            var m = new MetOfficeModel();
            MetFront f = Line(m, 0, MetFrontKind.Cold, 0f, 0f, 100f, 0f);
            var x = new float[32]; var z = new float[32]; var tx = new float[32]; var tz = new float[32];
            int n = MetChartMath.Symbols(f, 20f, x, z, tx, tz);
            TestAssert.That(n == 5 && Math.Abs(x[0] - 10f) < 0.01f && Math.Abs(x[4] - 90f) < 0.01f, "five symbols along 100 km at 20 km spacing, centred");
            TestAssert.That(Math.Abs(tx[0] - 1f) < 1e-4f && Math.Abs(tz[0]) < 1e-4f, "tangent is the line direction");
            TestAssert.That(MetChartMath.Symbols(f, 0f, x, z, tx, tz) == 0 && MetChartMath.Symbols(null, 20f, x, z, tx, tz) == 0, "degenerate inputs draw nothing");
            var small = new float[2];
            TestAssert.That(MetChartMath.Symbols(f, 5f, small, small, small, small) == 2, "the symbol buffer bounds the count");
        }

        private static void SkyPickerWalksClasses()
        {
            int[] expected = { 7, 7, 7, 7, 6, 7, 6 };
            int total = 0;
            for (int c = 0; c < 7; c++)
            {
                var cls = (WeatherRegimeType)c;
                TestAssert.That(MetSkyPicker.CountOf(cls) == expected[c], cls + " has " + expected[c] + " skies");
                total += MetSkyPicker.CountOf(cls);
                int first = MetSkyPicker.FirstOf(cls);
                for (int i = 0; i < expected[c]; i++) TestAssert.That(SkyLibrary.ClassOf(first + i) == cls, cls + " skies are contiguous in number order");
            }
            TestAssert.That(total == SkyLibrary.Count, "the classes cover the library");
            var picker = new MetSkyPicker();
            var model = new MetOfficeModel { Class = WeatherRegimeType.Broken };
            picker.Sync(model);
            TestAssert.That(picker.Selected == 21 && picker.IndexInClass == 1 && picker.CountInClass == 7, "an unpinned sky starts on the first sky of the class shown (BKN is sky 22)");
            TestAssert.That(picker.Step(1) == 22 && picker.Step(-1) == 21 && picker.Step(-1) == 27, "stepping wraps inside the class");
            TestAssert.That(picker.SelectClass(WeatherRegimeType.Overcast) == 28 && picker.Class == WeatherRegimeType.Overcast, "a class click selects its first sky (OVC is sky 29)");
            model.Class = WeatherRegimeType.Storm;
            picker.Sync(model);
            TestAssert.That(picker.Selected == 28, "once touched the selection stays where the host left it");
            model.PinnedSky = 26;
            picker.Sync(model);
            TestAssert.That(picker.Selected == 26 && picker.IndexInClass == 6, "a pinned sky always wins (sky 27 is the 6th broken sky)");
            TestAssert.That(MetOfficeText.StepperValue(picker.Selected + 1, MetCatalog.ClassCodes[(int)picker.Class], picker.IndexInClass, picker.CountInClass) == "#27 BKN 6 / 7",
                "the mockup stepper reads #27 BKN 6 / 7");
        }

        private static void LogKeepsFourNewestFirst()
        {
            var log = new MetOfficeLog();
            TestAssert.That(log.Count == 0 && log[0] == "" && log.Joined() == "", "an empty log");
            for (int i = 1; i <= 6; i++) log.Add("line " + i);
            TestAssert.That(log.Count == 4 && log[0] == "line 6" && log[3] == "line 3" && log[4] == "", "four lines, newest first");
            TestAssert.That(log.Joined() == "line 6\nline 5\nline 4\nline 3", "joined for the card");
            log.Clear();
            TestAssert.That(log.Count == 0, "clear");
        }
    }
}
