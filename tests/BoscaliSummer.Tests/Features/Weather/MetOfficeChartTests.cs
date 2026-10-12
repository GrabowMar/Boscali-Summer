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
            LabelsClearEverythingTheChartDraws();
            LeadersAreShortAndNeverCross();
            AnUnchangedChartCostsNothing();
            ThePlacerIsFast();
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
            TestAssert.Eq(MetOfficeText.SetPieceWhere(148f, 125f, 118f, 125f), "30 KM E", "a set-piece reads from the camera, not the theatre centre");
            TestAssert.Eq(MetOfficeText.Eta(30f) + "|" + MetOfficeText.Eta(float.NaN), "ETA 30'|STATIC", "a still front reads STATIC");
            TestAssert.Eq(MetOfficeText.FrontWord(MetFrontKind.Cold), "COLD FRONT", "front word");
            TestAssert.Eq(MetOfficeText.OutlookLabel(0) + MetOfficeText.OutlookLabel(15), "NOW+15", "outlook labels");
            TestAssert.Eq(MetOfficeText.PaceValue(3, float.NaN), "x3 · -- M/S", "an unknown speed prints the kit no-data mark, never NaN");
            TestAssert.Eq(MetOfficeText.PaceValue(0, 40f), "40 M/S", "a client does not know the host's pace: the speed alone, no x<pace>");
            TestAssert.Eq(MetOfficeText.TapeLine(true, 0, 40f), "RUNNING // TAPE 40 M/S", "so the tape line of a client has no pace either");
            TestAssert.Eq(MetOfficeText.PaceRow(3, 27f, 12f), "x3 · 27 M/S · WIND 12", "the pace row shows the wind the speed was scaled by (mockup 'wind 12')");
            TestAssert.Eq(MetOfficeText.PaceRow(0, 40f, 12f), "40 M/S · WIND 12", "and so does a client's");
            TestAssert.Eq(MetOfficeText.AnchorLabel(0.2f) + "|" + MetOfficeText.AnchorLabel(30.4f), "ANCHOR|ANCHOR +30 KM", "the anchor label carries its offset (mockup '+30 KM')");
            TestAssert.Eq(MetOfficeText.WindStrip(270, 12f), "WIND 270° · 12 M/S 23 KT", "the chart's wind strip");
            TestAssert.That(MetOfficeText.WindStrip(180, 0f) == "WIND CALM" && MetOfficeText.WindStrip(90, float.NaN) == "WIND CALM", "no wind reads CALM, never a bearing of nothing");
            TestAssert.Eq(MetOfficeText.Theater(81.92f, 40.96f) + "|" + MetOfficeText.Theater(0f, 0f), "THEATER\n164 X 82 KM|", "the theater label is two lines of whole kilometres and empty without a frame");
            TestAssert.Eq(MetOfficeText.IdentityHead("", "9A3F21C7") + "|" + MetOfficeText.IdentityHead("Mission", "9A3F21C7"), "SEED 9A3F21C7|MISSION · SEED 9A3F21C7", "a client prints only the seed, never a dash and a dot");
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

        /// <summary>The Busy and Cluttered fixtures of the Unity check, as models.</summary>
        private static MetOfficeModel ChartFixture(bool cluttered)
        {
            var m = new MetOfficeModel { HalfXKm = 81.92f, HalfZKm = 40.96f, HasCamera = true, CamHeadingDeg = cluttered ? 40f : 45f, HasAnchor = true, AnchorX = 21f, AnchorZ = 21f };
            Wavy(m, 0, MetFrontKind.Warm, -48f, 0.4f); Wavy(m, 1, MetFrontKind.Cold, -106f, 1.2f); Wavy(m, 2, MetFrontKind.Squall, 20f, 2.1f);
            m.CellCount = 6;
            for (int i = 0; i < 6; i++) m.Cells[i] = new MetCell { X = -118f + (i % 3) * 3f, Z = -96f + i * 36f, Radius = 6f + i, Severe = i == 2 };
            m.Sets[1] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = -95f, Z = 22f, Strength = 0.62f };
            m.Sets[4] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 21f, Z = 21f, Strength = 1f };
            m.Sets[0] = new MetSetPiece { Present = true, Placed = true, X = 20f, Z = 60f, Strength = 0.4f };
            if (cluttered)
            {
                m.CamX = 118f; m.CamZ = 125f; m.AnchorX = 118f; m.AnchorZ = 125f;                                                  // ANCHOR HERE: anchor and camera on one pixel
                m.Sets[3] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 118f, Z = 125f, Strength = 1f };   // a storm eye on the camera
                m.Sets[4] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 148f, Z = 125f, Strength = 1f };   // lenticulars 30 km east of it
                m.Sets[1] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 133f, Z = 112f, Strength = 1f };   // a supercell between them
                Wavy(m, 3, MetFrontKind.Cold, 128f, 0.9f);                                                                           // and a front across all three
            }
            return m;
        }

        private static int LongestLine(string text)
        {
            int longest = 0;
            foreach (string line in text.Split('\n')) longest = Math.Max(longest, line.Length);
            return longest;
        }

        private static void Wavy(MetOfficeModel m, int slot, MetFrontKind kind, float x, float phase)
        {
            MetFront f = m.Fronts[slot];
            f.Kind = kind; f.Count = MetFront.MaxPoints; f.DirX = 1f;
            for (int i = 0; i < f.Count; i++) { float z = -150f + i * 300f / (f.Count - 1); f.X[i] = x + 6f * (float)Math.Sin(z * 0.05f + phase); f.Z[i] = z; }
            m.FrontCount = Math.Max(m.FrontCount, slot + 1);
        }

        private const float Ox = 1f, Oy = 19f, PlotPx = 298f;

        private static float EstimateWidth(string text) => (float)Math.Ceiling(LongestLine(text) * 5.6f) + 2f;

        /// <summary>One line per label: what it says, where it stands, how it got there. For the failure messages.</summary>
        private static string Describe(MetChartPlanner p)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < MetChartLayout.Labels; i++)
                if (p.Box[i].W > 0f)
                    sb.Append(" [").Append(i).Append(" '").Append(p.Text[i].Replace((char)10, (char)47)).Append("' ").Append(p.Box[i].X.ToString("0")).Append(',').Append(p.Box[i].Y.ToString("0")).Append(p.Short[i] ? " short" : "").Append(p.Straddle[i] ? " straddles" : "").Append(p.Forced[i] ? " FORCED" : "").Append(p.Leader[i] ? " leader" : "").Append(']');
            return sb.ToString();
        }

        private static MetChartPlanner NewPlanner() => new MetChartPlanner(Ox, Oy, PlotPx, EstimateWidth);

        /// <summary>The states of the Unity check that differ on the chart, as models: the Busy host, the cluttered corner, a forced fog bank (nothing placed), a storm, a model full of NaN and an empty clear day.</summary>
        private static MetOfficeModel NamedFixture(string name)
        {
            MetOfficeModel m = ChartFixture(name == "cluttered");
            switch (name)
            {
                case "storm":
                    m.HasAnchor = false; m.Sets[3] = new MetSetPiece { Forced = true, Present = true, Placed = true, X = 0f, Z = 0f, Strength = 1f };
                    break;
                case "fog":
                    Array.Clear(m.Sets, 0, m.Sets.Length); m.Sets[5] = new MetSetPiece { Forced = true, Present = true };
                    break;
                case "nan":
                    m.CamX = float.NaN; m.Sets[1].Strength = float.NaN; m.Cells[2].X = float.NaN;
                    break;
                case "empty":
                    m.FrontCount = 0; m.CellCount = 0; m.HasAnchor = false; m.HasCamera = false; Array.Clear(m.Sets, 0, m.Sets.Length);
                    break;
            }
            return m;
        }

        /// <summary>
        /// What every plan must hold, whatever the model: each label on the plot at the width of its own text, off the hard obstacles (frame, markers, heading line), off the soft ones (fronts, cells) unless it says it
        /// straddles them, off every other label, and its leader short, off every other label and off every other leader. A label that fell back to the least bad spot is allowed to break these, and says so.
        /// </summary>
        private static void AssertPlan(string name, MetOfficeModel m, MetChartPlanner p)
        {
            var obstacles = new MetBox[MetChartLayout.MaxObstacles];
            int count = MetChartLayout.Obstacles(m, Ox, Oy, PlotPx, obstacles, out int hard);
            for (int i = 0; i < MetChartLayout.Labels; i++)
            {
                MetBox b = p.Box[i];
                if (b.W <= 0f) { TestAssert.That(p.Text[i].Length == 0 && !p.Leader[i], name + ": slot " + i + " has no box, so no text and no leader"); continue; }
                string tag = name + ": '" + p.Text[i].Replace("\n", " / ") + "' at " + b.X.ToString("0") + "," + b.Y.ToString("0");
                TestAssert.That(p.Text[i].Length > 0 && b.W == EstimateWidth(p.Text[i]) && b.H == MetChartLayout.Height(p.Text[i]), tag + " has the size of its text");
                TestAssert.That(b.X >= Ox && b.Right <= Ox + PlotPx && b.Y >= Oy && b.Bottom <= Oy + PlotPx, tag + " stays on the plot");
                TestAssert.That(MetChartMath.IsFinite(b.X) && MetChartMath.IsFinite(b.Y), tag + " is finite");
                if (p.Forced[i]) continue;
                for (int o = 0; o < count; o++)
                    if (o < hard || !p.Straddle[i])
                        TestAssert.That(MetChartMath.Overlap(b, obstacles[o]) <= 0f, tag + " crosses " + (o < hard ? "the frame or a marker" : "a front or a cell") + " at " + obstacles[o].X.ToString("0") + "," + obstacles[o].Y.ToString("0"));
                if (!MetChartLayout.Marker(m, i, Ox, Oy, PlotPx, out float mx, out float my, out _)) { TestAssert.That(false, tag + " has no marker"); continue; }
                float gap = MetChartLayout.Gap(b, mx, my, out _, out _);
                TestAssert.That(p.Leader[i] == gap > MetChartLayout.LeaderAfter(i), tag + " has a leader exactly when it stands off its marker (gap " + gap.ToString("0.0") + ")");
                if (p.Leader[i])
                {
                    float len = (float)Math.Sqrt((p.LeaderX1[i] - p.LeaderX0[i]) * (p.LeaderX1[i] - p.LeaderX0[i]) + (p.LeaderY1[i] - p.LeaderY0[i]) * (p.LeaderY1[i] - p.LeaderY0[i]));
                    TestAssert.That(len <= MetChartLayout.LeaderMax + 0.01f, tag + " has a leader of " + len.ToString("0") + " px (cap " + MetChartLayout.LeaderMax + ")");
                }
                for (int j = 0; j < MetChartLayout.Labels; j++)
                {
                    if (j == i || p.Box[j].W <= 0f || p.Forced[j]) continue;
                    if (j > i) TestAssert.That(MetChartMath.Overlap(b, p.Box[j]) <= 0f, tag + " overlaps '" + p.Text[j] + "'");
                    if (p.Leader[i]) TestAssert.That(!MetChartLayout.SegmentHitsBox(p.LeaderX0[i], p.LeaderY0[i], p.LeaderX1[i], p.LeaderY1[i], p.Box[j], 0f), tag + ": its leader runs through '" + p.Text[j] + "'");
                    if (j > i && p.Leader[i] && p.Leader[j])
                        TestAssert.That(MetChartLayout.SegmentDistance(p.LeaderX0[i], p.LeaderY0[i], p.LeaderX1[i], p.LeaderY1[i], p.LeaderX0[j], p.LeaderY0[j], p.LeaderX1[j], p.LeaderY1[j]) > 0f, tag + ": its leader crosses the leader of '" + p.Text[j] + "'");
                }
            }
        }

        /// <summary>
        /// The reviewed defects: labels stood on the theater frame, the fronts and the markers; and then a later label's leader ran through an earlier label's text, and labels flew 170-260 px from their markers on dashed
        /// leaders across fronts, the frame and each other. Every fixture of the Unity check that differs on the chart now places every label on the plot at real label widths, clear of the frame, the markers, the fronts,
        /// each other and each other's leaders, within a leader's reach of its marker, with no label forced onto a bad spot.
        /// </summary>
        private static void LabelsClearEverythingTheChartDraws()
        {
            foreach (string name in new[] { "busy", "cluttered", "fog", "storm", "nan", "empty" })
            {
                MetOfficeModel m = NamedFixture(name);
                MetChartPlanner p = NewPlanner();
                TestAssert.That(p.Update(m), name + ": the first update places");
                AssertPlan(name, m, p);
                int shown = 0, forced = 0, straddling = 0;
                for (int i = 0; i < MetChartLayout.Labels; i++) { if (p.Box[i].W > 0f) shown++; if (p.Forced[i]) forced++; if (p.Straddle[i]) straddling++; }
                var obstacles = new MetBox[MetChartLayout.MaxObstacles];
                int count = MetChartLayout.Obstacles(m, Ox, Oy, PlotPx, obstacles, out _);
                TestAssert.That(name == "empty" ? count == 4 : count > 100 && count < MetChartLayout.MaxObstacles, name + ": the obstacle list holds the frame, the fronts and the markers (" + count + ")");
                TestAssert.That(forced == 0, name + ": no label fell back to a bad spot (" + forced + ")" + Describe(p));
                int expect = name == "cluttered" ? 7 : name == "busy" ? 6 : name == "storm" ? 6 : name == "nan" ? 6 : name == "fog" ? 3 : 1;
                TestAssert.That(shown == expect, name + ": " + expect + " labels (" + shown + ")");
                TestAssert.That(!(name == "busy" || name == "fog" || name == "empty") || straddling == 0, name + ": an uncrowded chart straddles no front (" + straddling + ")");
                // The same input places the same boxes: no hidden state.
                MetChartPlanner again = NewPlanner(); again.Update(m);
                for (int i = 0; i < MetChartLayout.Labels; i++) TestAssert.That(again.Box[i].X == p.Box[i].X && again.Box[i].Y == p.Box[i].Y && again.Text[i] == p.Text[i], name + ": placement is deterministic");
            }
            // A cell with an unknown position is not drawn, so it blocks nothing.
            var nan = new MetOfficeModel { CellCount = 1 };
            nan.Cells[0] = new MetCell { X = float.NaN, Z = 3f, Radius = 8f };
            TestAssert.That(MetChartLayout.Obstacles(nan, 1f, 19f, 298f, new MetBox[MetChartLayout.MaxObstacles], out _) == 0, "a cell with a non-finite position leaves no obstacle at the chart centre");
            // The short forms are the spec's: SC 62%, LENT 100%, EYE, CB 58%, SQL 40%, FOG.
            TestAssert.Eq(MetOfficeText.SetShortLabel(1, 0.62f) + "|" + MetOfficeText.SetShortLabel(4, 1f) + "|" + MetOfficeText.SetShortLabel(3, 1f) + "|" + MetOfficeText.SetShortLabel(2, 0.58f) + "|" + MetOfficeText.SetShortLabel(0, 0.4f) + "|" + MetOfficeText.SetShortLabel(5, 1f),
                "SC 62%|LENT 100%|EYE|CB 58%|SQL 40%|FOG", "the short set-piece labels");
        }

        /// <summary>A few hundred random charts: whatever the clutter, no plan lets a leader cross a label or a leader or run past its cap, and a label that cannot be kept clear says so rather than hiding a defect.</summary>
        private static void LeadersAreShortAndNeverCross()
        {
            uint seed = 12345u;
            float Rnd(float lo, float hi) { seed = seed * 1664525u + 1013904223u; return lo + (hi - lo) * ((seed >> 8) / 16777216f); }
            int forced = 0, labels = 0, short_ = 0, straddling = 0;
            MetChartPlanner p = NewPlanner();
            for (int k = 0; k < 300; k++)
            {
                var m = new MetOfficeModel { HalfXKm = Rnd(10f, 120f), HalfZKm = Rnd(10f, 120f), HasCamera = Rnd(0f, 1f) > 0.1f, CamX = Rnd(-150f, 150f), CamZ = Rnd(-150f, 150f), CamHeadingDeg = Rnd(0f, 360f), HasAnchor = Rnd(0f, 1f) > 0.3f };
                m.AnchorX = Rnd(-150f, 150f); m.AnchorZ = Rnd(-150f, 150f);
                int fronts = (int)Rnd(0f, 4.99f);
                for (int f = 0; f < fronts; f++) Wavy(m, f, (MetFrontKind)(int)Rnd(0f, 3.99f), Rnd(-140f, 140f), Rnd(0f, 6f));
                m.CellCount = (int)Rnd(0f, 12.99f);
                for (int c = 0; c < m.CellCount; c++) m.Cells[c] = new MetCell { X = Rnd(-150f, 150f), Z = Rnd(-150f, 150f), Radius = Rnd(3f, 14f) };
                for (int s = 0; s < MetOfficeModel.SetCount; s++)
                    if (Rnd(0f, 1f) > 0.5f) m.Sets[s] = new MetSetPiece { Present = true, Placed = true, X = Rnd(-150f, 150f), Z = Rnd(-150f, 150f), Strength = Rnd(0f, 1f) };
                p.Invalidate(); p.Update(m);
                AssertPlan("random " + k, m, p);
                for (int i = 0; i < MetChartLayout.Labels; i++) { if (p.Box[i].W > 0f) labels++; if (p.Forced[i]) forced++; if (p.Short[i]) short_++; if (p.Straddle[i]) straddling++; }
            }
            Console.WriteLine("  random charts: " + labels + " labels, " + short_ + " short, " + straddling + " straddling a front, " + forced + " forced");
            TestAssert.That(labels > 1000, "the random charts carry labels (" + labels + ")");
            TestAssert.That(forced * 100 <= labels * 3, "no more than 3% of the labels of crowded random charts fall back to a bad spot (" + forced + " of " + labels + ")");
        }

        /// <summary>The performance defect: the placer reran on every 0.25 s refresh. A model that did not change (or only jittered below what the eye can tell apart) does zero placements and allocates nothing.</summary>
        private static void AnUnchangedChartCostsNothing()
        {
            MetOfficeModel m = ChartFixture(true);
            MetChartPlanner p = NewPlanner();
            TestAssert.That(p.Update(m) && p.Runs == 1, "the first update places once");
            for (int i = 0; i < 20; i++) p.Update(m);
            TestAssert.That(p.Runs == 1, "twenty updates of an unchanged model place nothing more (" + p.Runs + ")");
            float cx = m.CamX, cz = m.CamZ, ax = m.AnchorX, az = m.AnchorZ, heading = m.CamHeadingDeg;
            m.WindMps = 12f;
            p.Update(m);
            int baseline = p.Runs;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 400; i++)
            {
                float jitter = (i % 5 - 2) * 0.1f;                                     // +-0.2 km and +-0.2 degrees: below a pixel, below a printed degree
                m.CamX = cx + jitter; m.CamZ = cz - jitter; m.CamHeadingDeg = heading + jitter; m.AnchorX = ax + jitter; m.AnchorZ = az - jitter; m.WindMps = 12f + jitter * 0.1f;
                p.Update(m);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            TestAssert.That(p.Runs == baseline, "idle jitter (the camera creeping a fifth of a kilometre and degree) does not re-place (" + (p.Runs - baseline) + " placements)");
            TestAssert.That(after - before == 0, "an unchanged chart allocates nothing per refresh (" + (after - before) + " bytes in 400)");
            m.CamX = cx; m.CamZ = cz; m.CamHeadingDeg = heading; m.AnchorX = ax; m.AnchorZ = az; m.WindMps = 12f;
            p.Update(m);
            int runs = p.Runs;
            m.CamX = cx + 3f; p.Update(m);
            TestAssert.That(p.Runs == runs + 1, "the camera moving three kilometres places again");
            m.Sets[1].Strength = 0.5f; p.Update(m);
            TestAssert.That(p.Runs == runs + 2, "a set-piece's strength changing a printed percent places again");
            m.Fronts[2].X[10] += 4f; p.Update(m);
            TestAssert.That(p.Runs == runs + 3, "a front moving four kilometres places again");
            m.WindFromDeg += 10; p.Update(m);
            TestAssert.That(p.Runs == runs + 4, "a new wind bearing places again");
            p.Invalidate(); p.Update(m);
            TestAssert.That(p.Runs == runs + 5, "an invalidated plan (a new font) places again");
        }

        /// <summary>The worst fixture (the cluttered corner: anchor on the camera, an eye, lenticulars, a supercell and a front across them) is placed, text and grids and all, well inside a frame's budget even before Unity's slower runtime.</summary>
        private static void ThePlacerIsFast()
        {
            foreach (string name in new[] { "busy", "cluttered" })
            {
                MetOfficeModel m = NamedFixture(name);
                MetChartPlanner p = NewPlanner();
                for (int i = 0; i < 200; i++) { p.Invalidate(); p.Update(m); }          // warm the JIT
                double ms = double.MaxValue;                                          // the best of twelve batches: a pause by the machine is not the placer's cost
                for (int batch = 0; batch < 12; batch++)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 50; i++) { p.Invalidate(); p.Update(m); }
                    ms = Math.Min(ms, watch.Elapsed.TotalMilliseconds / 50);
                }
                Console.WriteLine("  chart placer, " + name + ": " + ms.ToString("0.000") + " ms per placement (CoreCLR)");
                TestAssert.That(ms < 1.5, name + ": a placement takes " + ms.ToString("0.00") + " ms (limit 1.5)");
            }
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
