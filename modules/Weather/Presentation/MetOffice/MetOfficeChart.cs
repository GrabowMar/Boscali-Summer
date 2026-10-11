using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// The synoptic chart (R4, R7, C8's picture): north-up, ±150 km around the theater centre. Fronts with their symbols, storm cells, set-pieces with labels, the anchor,
    /// the camera with its heading, the wind barb, the theater frame and a legend. One mesh (<see cref="EnvCanvas"/>) plus a pool of labels; it reads only the
    /// <see cref="MetOfficeModel"/>, so the tape slices change the adapter and leave this untouched.
    /// </summary>
    internal sealed class MetSynopticChart : EnvPart
    {
        private const float S = MetOfficeLayout.ChartSquare, Inset = 1f, Plot = S - 2f * Inset;
        private readonly TMP_Text[] setLabel = new TMP_Text[MetOfficeModel.SetCount], axisLabel = new TMP_Text[4];
        private readonly TMP_Text theaterLabel, camLabel, anchorLabel, windFrom, windSpeed, north, scale;
        private readonly TMP_Text[] legendWord = new TMP_Text[8];
        private static readonly string[] LegendNames = { "WARM", "COLD", "OCCL.", "SQUALL", "CELL", "SET-PC", "CAM", "ANCHOR" };
        private static readonly int[] AxisKm = { -50, 0, 50, 100 };
        private readonly float[] sx = new float[16], sz = new float[16], tx = new float[16], tz = new float[16];
        // The wind box (text left, barb right) and the plot rectangle labels must stay inside; the bottom 31 px hold the scale, axis and north labels.
        private const float WindW = 173f, WindH = 58f;
        private static readonly MetBox WindBox = new MetBox(S - 3f - WindW, 3f, WindW, WindH);
        private static readonly MetBox labelBounds = new MetBox(2f, 2f, S - 4f, S - 33f);
        private readonly MetBox[] taken = new MetBox[12];
        private MetOfficeModel model;

        public MetSynopticChart(RectTransform parent) : base(parent, "Synoptic chart", "inert", true)
        {
            MakeArt(Paint);
            for (int i = 0; i < setLabel.Length; i++) setLabel[i] = Txt("Set" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "caution", false, true);
            for (int i = 0; i < axisLabel.Length; i++)
            {
                axisLabel[i] = Txt("Axis" + i, AvTextRole.Micro, TextAlignmentOptions.Center, "ink-muted");
                axisLabel[i].text = (AxisKm[i] > 0 ? "+" : AxisKm[i] < 0 ? "−" : "") + System.Math.Abs(AxisKm[i]);
            }
            theaterLabel = Txt("Theater", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
            camLabel = Txt("Cam", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
            anchorLabel = Txt("Anchor", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "key");
            anchorLabel.text = "ANCHOR";
            windFrom = Txt("WindFrom", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
            windSpeed = Txt("WindSpeed", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink");
            north = Txt("North", AvTextRole.Micro, TextAlignmentOptions.Center, "ink-dim");
            north.text = "N";
            scale = Txt("Scale", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
            scale.text = "50 KM";
            for (int i = 0; i < legendWord.Length; i++)
            {
                legendWord[i] = Txt("Legend" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
                legendWord[i].text = LegendNames[i];
            }
            Restyle();
        }

        public override float Measure(float width) => MetOfficeLayout.ChartPart;

        public void Set(MetOfficeModel m)
        {
            model = m;
            AvText.Set(windFrom, "FROM " + m.WindFromDeg.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + "°");
            AvText.Set(windSpeed, AvNum.Fixed(m.WindMps, 0) + " M/S " + AvNum.Fixed(m.WindMps * 1.943844f, 0) + " KT");
            AvText.Set(camLabel, m.HasCamera ? "CAM " + Mathf.RoundToInt(m.CamHeadingDeg).ToString("000") + "°" : "");
            AvText.Set(theaterLabel, m.HalfXKm > 0f ? "THEATER " + AvNum.Fixed(m.HalfXKm * 2f, 2) + " x " + AvNum.Fixed(m.HalfZKm * 2f, 2) + " KM" : "");
            float offset = m.HasAnchor && m.HasCamera ? Mathf.Sqrt((m.AnchorX - m.CamX) * (m.AnchorX - m.CamX) + (m.AnchorZ - m.CamZ) * (m.AnchorZ - m.CamZ)) : 0f;
            AvText.Set(anchorLabel, m.HasAnchor ? MetOfficeText.AnchorLabel(offset) : "");
            for (int i = 0; i < setLabel.Length; i++)
                AvText.Set(setLabel[i], m.Sets[i].Present && m.Sets[i].Placed ? MetCatalog.SetNames[i] + " " + MetOfficeText.Cover(m.Sets[i].Strength) : "");
            Arrange();
            Redraw();
        }

        protected override void Layout(float w, float h) => Arrange();

        private float Px(float km) => Inset + MetChartMath.PxX(MetChartMath.Clamp(km), Plot);
        private float Py(float km) => Inset + MetChartMath.PxY(MetChartMath.Clamp(km), Plot);

        private static MetBox LabelBox(TMP_Text label, float x, float y) => new MetBox(x, y, Mathf.Ceil(AvText.Width(label)) + 2f, 16f);

        /// <summary>Puts a label next to its marker on the first free spot (see <see cref="MetChartMath.Place"/>) and records it as taken.</summary>
        private void PlaceLabel(TMP_Text label, float markerX, float markerY, bool below, ref int n)
        {
            bool on = label.text.Length > 0;
            label.gameObject.SetActive(on);
            if (!on) return;
            MetBox size = LabelBox(label, 0f, 0f);
            MetBox box = MetChartMath.Place(markerX, markerY, size.W, size.H, below, labelBounds, taken, n);
            taken[n++] = box;
            AvLay.Place(label, box.X, box.Y, box.W, box.H);
        }

        private void Arrange()
        {
            for (int i = 0; i < legendWord.Length; i++)
            {
                float x = 4f + (i % 4) * 74f, y = S + 6f + (i / 4) * 16f;
                AvLay.Place(legendWord[i], x + 27f, y, 46f, 15f);
            }
            for (int i = 0; i < axisLabel.Length; i++) AvLay.Place(axisLabel[i], Px(AxisKm[i]) - 16f, S - 17f, 32f, 15f);
            AvLay.Place(north, S - 18f, S - 17f, 16f, 15f);
            AvLay.Place(scale, 8f, S - 29f, 50f, 15f);
            AvLay.Place(windFrom, WindBox.X + 5f, 5f, 108f, 15f);
            AvLay.Place(windSpeed, WindBox.X + 5f, 20f, 108f, 16f);
            if (model == null) return;
            // The labels, one after the other against what is already taken: the wind box first, then the theater, the camera, the anchor (opposite the camera label) and the set-pieces.
            int n = 0;
            taken[n++] = WindBox;
            float hx = model.HalfXKm, hz = model.HalfZKm;
            PlaceLabel(theaterLabel, Px(-hx) - 6f, Py(hz) + 4f, false, ref n);        // first choice: above the frame's north-west corner
            PlaceLabel(camLabel, Px(model.CamX), Py(model.CamZ), true, ref n);          // first choice: right of and below the camera
            PlaceLabel(anchorLabel, Px(model.AnchorX), Py(model.AnchorZ), false, ref n); // first choice: right of and above the anchor, so it never lands on the camera label
            for (int i = 0; i < setLabel.Length; i++)
                PlaceLabel(setLabel[i], Px(model.Sets[i].X), Py(model.Sets[i].Z), false, ref n);
        }

        private void Paint(EnvCanvas c)
        {
            Color sunken = EnvInk.Role("surface-sunken"), grid = EnvInk.Alpha(EnvInk.Role("hairline"), 0.55f), muted = EnvInk.Role("ink-muted");
            Color key = EnvInk.Role("key"), caution = EnvInk.Role("caution"), danger = EnvInk.Role("danger"), info = EnvInk.Role("info"), ink = EnvInk.Role("ink");
            c.Quad(Inset, Inset, Plot, Plot, sunken);
            for (int km = -100; km <= 100; km += 50)
            {
                c.Line(Px(km), Inset, Px(km), S - Inset, 0.6f, grid);
                c.Line(Inset, Py(km), S - Inset, Py(km), 0.6f, grid);
            }
            float cx = Px(0f), cy = Py(0f);
            float r50 = Plot / 6f;
            c.Arc(cx, cy, r50, r50, 0f, 360f, 0.7f, EnvInk.Alpha(muted, 0.5f), 2);
            c.Arc(cx, cy, 2f * r50, 2f * r50, 0f, 360f, 0.7f, EnvInk.Alpha(muted, 0.5f), 2);
            // Legend divider, the scale bar and the north arrow.
            c.Quad(Inset, S, Plot, 1f, EnvInk.Role("hairline"));
            c.Line(8f, S - 12f, 8f + r50, S - 12f, 1f, EnvInk.Role("ink-dim"));
            c.Line(8f, S - 15f, 8f, S - 9f, 1f, EnvInk.Role("ink-dim")); c.Line(8f + r50, S - 15f, 8f + r50, S - 9f, 1f, EnvInk.Role("ink-dim"));
            c.Line(S - 9f, S - 14f, S - 9f, S - 36f, 1f, EnvInk.Role("ink-dim")); c.Tri(S - 13f, S - 31f, S - 5f, S - 31f, S - 9f, S - 38f, EnvInk.Role("ink-dim"));
            DrawLegend(c);
            if (model == null) return;

            // Theater frame.
            float hx = model.HalfXKm, hz = model.HalfZKm;
            if (hx > 0f && hz > 0f)
            {
                Color th = EnvInk.Role("ink-dim");
                c.Dashed(Px(-hx), Py(hz), Px(hx), Py(hz), 0.8f, 5f, 3f, th); c.Dashed(Px(hx), Py(hz), Px(hx), Py(-hz), 0.8f, 5f, 3f, th);
                c.Dashed(Px(hx), Py(-hz), Px(-hx), Py(-hz), 0.8f, 5f, 3f, th); c.Dashed(Px(-hx), Py(-hz), Px(-hx), Py(hz), 0.8f, 5f, 3f, th);
            }
            // Fronts and their symbols (on the side they travel toward).
            for (int i = 0; i < model.FrontCount; i++)
            {
                MetFront f = model.Fronts[i];
                Color col = MetFrontGlyph.Ink(f.Kind);
                for (int p = 0; p + 1 < f.Count; p++)
                {
                    if (f.Kind == MetFrontKind.Squall) c.Dashed(Px(f.X[p]), Py(f.Z[p]), Px(f.X[p + 1]), Py(f.Z[p + 1]), 0.9f, 6f, 3f, col);
                    else c.Line(Px(f.X[p]), Py(f.Z[p]), Px(f.X[p + 1]), Py(f.Z[p + 1]), 1.5f, col);
                }
                if (f.Kind == MetFrontKind.Squall) continue;
                int n = MetChartMath.Symbols(f, 16f, sx, sz, tx, tz);
                for (int s = 0; s < n; s++)
                {
                    // Normal on the travel side; chart y runs down, so z is negated.
                    float nx = -tz[s], nz = tx[s];
                    if (nx * f.DirX + nz * f.DirZ < 0f) { nx = -nx; nz = -nz; }
                    float px = Px(sx[s]), py = Py(sz[s]), ux = tx[s], uy = -tz[s], vx = nx, vy = -nz;
                    if (f.Kind == MetFrontKind.Warm) c.Disc(px + vx * 2f, py + vy * 2f, 3.2f, col, 10);
                    else c.Tri(px - ux * 3.5f, py - uy * 3.5f, px + ux * 3.5f, py + uy * 3.5f, px + vx * 6.5f, py + vy * 6.5f, col);
                }
            }
            for (int i = 0; i < model.CellCount; i++)
            {
                MetCell cell = model.Cells[i];
                float rr = Mathf.Max(2.5f, MetChartMath.Fin(cell.Radius) / (2f * MetChartMath.RangeKm) * Plot);
                c.Disc(Px(cell.X), Py(cell.Z), rr, EnvInk.Alpha(danger, 0.28f), 14);
                c.Ring(Px(cell.X), Py(cell.Z), rr, 0.8f, danger);
            }
            for (int i = 0; i < model.Sets.Length; i++)
            {
                MetSetPiece sp = model.Sets[i];
                if (!sp.Present || !sp.Placed) continue;   // a forced fog bank is Present but has no site: nothing is drawn for it
                float x = Px(sp.X), y = Py(sp.Z);
                c.Arc(x, y, 9f, 9f, 0f, 360f, 0.9f, caution, 2);
                c.Disc(x, y, 3.5f, EnvInk.Alpha(caution, 0.5f + 0.4f * Mathf.Clamp01(MetChartMath.Fin(sp.Strength))), 10);
            }
            if (model.HasAnchor)
            {
                float x = Px(model.AnchorX), y = Py(model.AnchorZ);
                c.Ring(x, y, 7f, 1f, key);
                c.Line(x - 12f, y, x - 4f, y, 1f, key); c.Line(x + 4f, y, x + 12f, y, 1f, key);
                c.Line(x, y - 12f, x, y - 4f, 1f, key); c.Line(x, y + 4f, x, y + 12f, 1f, key);
            }
            if (model.HasCamera)
            {
                float x = Px(model.CamX), y = Py(model.CamZ), a = model.CamHeadingDeg * Mathf.Deg2Rad;
                float fx = Mathf.Sin(a), fy = -Mathf.Cos(a);
                c.Dashed(x, y, x + fx * 22f, y + fy * 22f, 0.8f, 3f, 3f, key);
                c.Tri(x + fx * 9f, y + fy * 9f, x - fx * 6f - fy * 5.5f, y - fy * 6f + fx * 5.5f, x - fx * 6f + fy * 5.5f, y - fy * 6f - fx * 5.5f, key);
            }
            DrawBarb(c, ink);
        }

        private void DrawBarb(EnvCanvas c, Color ink)
        {
            // The wind box: the text sits on its left, the barb on a 58 px square at its right. The shaft leaves the station circle toward the heading the wind comes FROM
            // (MetChartMath.BarbAxis), so a wind from 090 points east; the feathers sit on its clockwise side and walk from the tip toward the station.
            c.Quad(WindBox.X, WindBox.Y, WindBox.W, WindBox.H, EnvInk.Alpha(EnvInk.Role("ground"), 0.9f));
            float cx = WindBox.Right - 31f, cy = WindBox.Y + WindBox.H * 0.5f;
            c.Ring(cx, cy, 2.4f, 1f, ink);
            MetChartMath.Barb(model.WindMps * 1.943844f, out int pennants, out int full, out bool half);
            if (pennants + full == 0 && !half) return;                       // calm: the circle alone
            MetChartMath.BarbAxis(model.WindFromDeg, out float ux, out float uy);
            float vx = -uy, vy = ux;
            const float shaft = 22f;
            c.Line(cx + ux * 2.4f, cy + uy * 2.4f, cx + ux * shaft, cy + uy * shaft, 1f, ink);
            float at = shaft;
            for (int i = 0; i < pennants && at > 4f; i++)
            {
                float px = cx + ux * at, py = cy + uy * at;
                c.Tri(px, py, px - ux * 5f, py - uy * 5f, px + vx * 9f, py + vy * 9f, ink); at -= 7f;
            }
            for (int i = 0; i < full && at > 4f; i++)
            {
                float px = cx + ux * at, py = cy + uy * at;
                c.Line(px, py, px + ux * 3f + vx * 9f, py + uy * 3f + vy * 9f, 1f, ink); at -= 6f;
            }
            if (half && at > 2f)
            {
                float px = cx + ux * at, py = cy + uy * at;
                c.Line(px, py, px + ux * 1.6f + vx * 4.5f, py + uy * 1.6f + vy * 4.5f, 1f, ink);
            }
        }

        private void DrawLegend(EnvCanvas c)
        {
            MetFrontKind[] kinds = { MetFrontKind.Warm, MetFrontKind.Cold, MetFrontKind.Occluded, MetFrontKind.Squall };
            for (int i = 0; i < 8; i++)
            {
                float x = 4f + (i % 4) * 74f, y = S + 5f + (i / 4) * 16f;
                if (i < 4) { MetFrontGlyph.Draw(c, kinds[i], x - 3f, y - 1f); continue; }
                switch (i)
                {
                    case 4: c.Disc(x + 9f, y + 7f, 4.5f, EnvInk.Alpha(EnvInk.Role("danger"), 0.3f), 12); c.Ring(x + 9f, y + 7f, 4.5f, 0.9f, EnvInk.Role("danger")); break;
                    case 5: c.Arc(x + 9f, y + 7f, 5f, 5f, 0f, 360f, 0.9f, EnvInk.Role("caution"), 2); c.Disc(x + 9f, y + 7f, 2.5f, EnvInk.Role("caution"), 8); break;
                    case 6: c.Tri(x + 9f, y, x + 14f, y + 12f, x + 4f, y + 12f, EnvInk.Role("key")); break;
                    default: c.Ring(x + 9f, y + 7f, 4f, 0.9f, EnvInk.Role("key")); c.Line(x, y + 7f, x + 5f, y + 7f, 0.9f, EnvInk.Role("key")); c.Line(x + 13f, y + 7f, x + 18f, y + 7f, 0.9f, EnvInk.Role("key")); break;
                }
            }
        }
    }
}
