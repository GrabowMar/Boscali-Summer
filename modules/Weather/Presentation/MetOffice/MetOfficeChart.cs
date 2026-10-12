using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// The synoptic chart (R4, R7, C8's picture): north-up, ±150 km around the theater centre. A wind strip on top, the plot (fronts with their symbols, storm cells, set-pieces with labels, the anchor,
    /// the camera with its heading, the theater frame), an axis strip with the km ticks and north below it, then a legend. One mesh (<see cref="EnvCanvas"/>) plus a pool of labels; it reads only the
    /// <see cref="MetOfficeModel"/>, so the tape slices change the adapter and leave this untouched. Nothing but data is drawn over the plot: the labels are placed clear of every front, the frame
    /// and the markers by <see cref="MetChartLayout"/>, sit on a plate, and a leader line joins one that had to move away from its marker.
    /// </summary>
    internal sealed class MetSynopticChart : EnvPart
    {
        private const float S = MetOfficeLayout.ChartSquare, Inset = 1f, Plot = S - 2f * Inset, Top = MetOfficeLayout.WindStrip, AxisTop = Top + S, LegendTop = AxisTop + MetOfficeLayout.AxisStrip;
        private static readonly string[] LegendNames = { "WARM", "COLD", "OCCL.", "SQUALL", "CELL", "SET-PC", "CAM", "ANCHOR" };
        private static readonly int[] AxisKm = { -100, -50, 0, 50, 100 };
        private readonly TMP_Text[] axisLabel = new TMP_Text[AxisKm.Length];
        private readonly TMP_Text[] label = new TMP_Text[MetChartLayout.Labels];
        private readonly TMP_Text windText, north, scale;
        private readonly TMP_Text[] legendWord = new TMP_Text[8];
        private readonly float[] sx = new float[16], sz = new float[16], tx = new float[16], tz = new float[16];
        private readonly float[] widths = new float[MetChartLayout.Labels];
        private readonly MetBox[] boxes = new MetBox[MetChartLayout.Labels], scratch = new MetBox[MetChartLayout.MaxObstacles + MetChartLayout.Labels];
        private MetOfficeModel model;

        public MetSynopticChart(RectTransform parent) : base(parent, "Synoptic chart", "inert", true)
        {
            MakeArt(Paint);
            for (int i = 0; i < label.Length; i++)
            {
                bool set = i >= MetChartLayout.FirstSet;
                string name = set ? "Set" + (i - MetChartLayout.FirstSet) : i == MetChartLayout.Theater ? "Theater" : i == MetChartLayout.Camera ? "Cam" : "Anchor";
                label[i] = Txt(name, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, set ? "caution" : i == MetChartLayout.Anchor ? "key" : "ink-dim", false, set);
            }
            label[MetChartLayout.Anchor].text = "ANCHOR";
            for (int i = 0; i < axisLabel.Length; i++)
            {
                axisLabel[i] = Txt("Axis" + i, AvTextRole.Micro, TextAlignmentOptions.Center, "ink-muted");
                axisLabel[i].text = (AxisKm[i] > 0 ? "+" : AxisKm[i] < 0 ? "−" : "") + System.Math.Abs(AxisKm[i]);
            }
            windText = Txt("Wind", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            north = Txt("North", AvTextRole.Micro, TextAlignmentOptions.Center, "ink-dim");
            north.text = "N";
            scale = Txt("Scale", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
            scale.text = "KM";
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
            AvText.Set(windText, MetOfficeText.WindStrip(m.WindFromDeg, m.WindMps));
            AvText.Set(label[MetChartLayout.Camera], m.HasCamera ? "CAM " + Mathf.RoundToInt(m.CamHeadingDeg).ToString("000") + "°" : "");
            AvText.Set(label[MetChartLayout.Theater], MetOfficeText.Theater(m.HalfXKm, m.HalfZKm));
            float offset = m.HasAnchor && m.HasCamera ? Mathf.Sqrt((m.AnchorX - m.CamX) * (m.AnchorX - m.CamX) + (m.AnchorZ - m.CamZ) * (m.AnchorZ - m.CamZ)) : 0f;
            AvText.Set(label[MetChartLayout.Anchor], m.HasAnchor ? MetOfficeText.AnchorLabel(offset) : "");
            for (int i = 0; i < MetOfficeModel.SetCount; i++)
                AvText.Set(label[MetChartLayout.FirstSet + i], m.Sets[i].Present && m.Sets[i].Placed ? MetCatalog.SetNames[i] + " " + MetOfficeText.Cover(m.Sets[i].Strength) : "");
            Arrange();
            Redraw();
        }

        protected override void Layout(float w, float h) => Arrange();

        private float Px(float km) => MetChartLayout.Px(km, Inset, Plot);
        private float Py(float km) => MetChartLayout.Py(km, Top + Inset, Plot);

        private void Arrange()
        {
            for (int i = 0; i < legendWord.Length; i++)
            {
                float x = 4f + (i % 4) * 74f, y = LegendTop + 6f + (i / 4) * 16f;
                AvLay.Place(legendWord[i], x + 27f, y, 46f, 15f);
            }
            for (int i = 0; i < axisLabel.Length; i++) AvLay.Place(axisLabel[i], Px(AxisKm[i]) - 16f, AxisTop + 3f, 32f, 15f);
            AvLay.Place(north, S - 34f, AxisTop + 3f, 16f, 15f);
            AvLay.Place(scale, 5f, AxisTop + 3f, 24f, 15f);
            AvLay.Place(windText, 6f, Inset, S - 44f, Top - 2f);
            if (model == null) return;
            // One deterministic placer for every label (MetChartLayout): clear of the frame, the fronts, the markers and each other; a label with no free spot keeps the least bad one, and its plate.
            for (int i = 0; i < label.Length; i++) widths[i] = label[i].text.Length > 0 ? Mathf.Ceil(AvText.Width(label[i])) + 2f : 0f;
            MetChartLayout.Place(model, Inset, Top + Inset, Plot, widths, boxes, scratch);
            for (int i = 0; i < label.Length; i++)
            {
                bool on = widths[i] > 0f && boxes[i].W > 0f;
                label[i].gameObject.SetActive(on);
                if (on) AvLay.Place(label[i], boxes[i].X, boxes[i].Y, boxes[i].W, boxes[i].H);
            }
        }

        private void Paint(EnvCanvas c)
        {
            Color sunken = EnvInk.Role("surface-sunken"), grid = EnvInk.Alpha(EnvInk.Role("hairline"), 0.55f), muted = EnvInk.Role("ink-muted");
            Color key = EnvInk.Role("key"), caution = EnvInk.Role("caution"), danger = EnvInk.Role("danger"), ink = EnvInk.Role("ink");
            float pt = Top + Inset;
            c.Quad(Inset, Inset, Plot, Top - Inset - 1f, sunken);                               // the wind strip
            c.Quad(Inset, pt, Plot, Plot, sunken);                                              // the plot
            for (int km = -100; km <= 100; km += 50)
            {
                c.Line(Px(km), pt, Px(km), pt + Plot, 0.6f, grid);
                c.Line(Inset, Py(km), S - Inset, Py(km), 0.6f, grid);
            }
            float cx = Px(0f), cy = Py(0f);
            float r50 = Plot / 6f;
            c.Arc(cx, cy, r50, r50, 0f, 360f, 0.7f, EnvInk.Alpha(muted, 0.5f), 2);
            c.Arc(cx, cy, 2f * r50, 2f * r50, 0f, 360f, 0.7f, EnvInk.Alpha(muted, 0.5f), 2);
            // The axis strip below the plot: a tick under every grid line, and the north arrow. Then the legend divider.
            c.Quad(Inset, AxisTop, Plot, MetOfficeLayout.AxisStrip - 1f, sunken);
            for (int i = 0; i < AxisKm.Length; i++) c.Quad(Px(AxisKm[i]) - 0.4f, AxisTop, 0.8f, 3f, muted);
            c.Quad(S - 11f, AxisTop + 8f, 1f, 7f, EnvInk.Role("ink-dim")); c.Tri(S - 15f, AxisTop + 9f, S - 7f, AxisTop + 9f, S - 10.5f, AxisTop + 3f, EnvInk.Role("ink-dim"));
            c.Quad(Inset, LegendTop, Plot, 1f, EnvInk.Role("hairline"));
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
                if (!MetChartMath.IsFinite(cell.X) || !MetChartMath.IsFinite(cell.Z) || !MetChartMath.IsFinite(cell.Radius)) continue;   // an unknown cell is not drawn at the chart centre
                float rr = Mathf.Max(2.5f, cell.Radius / (2f * MetChartMath.RangeKm) * Plot);
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
            // The labels last: a leader to each that stands off its marker, and a plate under each so a residual crossing still reads (the text is a later sibling, so it draws above).
            Color leader = EnvInk.Alpha(EnvInk.Role("ink-dim"), 0.8f), plate = EnvInk.Alpha(EnvInk.Role("ground"), 0.7f);
            for (int i = 0; i < label.Length; i++)
            {
                if (widths[i] <= 0f || boxes[i].W <= 0f) continue;
                if (MetChartLayout.Marker(model, i, Inset, pt, Plot, out float mx, out float my, out _))
                {
                    if (i == MetChartLayout.Theater) { mx += 6f; my -= 4f; }          // the theater's marker is the frame's north-west corner, not the label's anchor point
                    float gap = MetChartLayout.Gap(boxes[i], mx, my, out float nx, out float ny);
                    if (gap > MetChartLayout.LeaderAfter) c.Dashed(mx + (nx - mx) / gap * (i == MetChartLayout.Theater ? 0f : 7f), my + (ny - my) / gap * (i == MetChartLayout.Theater ? 0f : 7f), nx, ny, 0.7f, 3f, 2f, leader);
                }
                c.Quad(boxes[i].X, boxes[i].Y + 1f, boxes[i].W, boxes[i].H - 2f, plate);
            }
            DrawBarb(c, ink);
        }

        private void DrawBarb(EnvCanvas c, Color ink)
        {
            // The barb at the right end of the wind strip, a miniature of the weather-chart symbol (the readout beside it carries the numbers). The shaft leaves the station circle toward the heading the wind
            // comes FROM (MetChartMath.BarbAxis), so a wind from 090 points east; the feathers sit on its clockwise side and walk from the tip toward the station. It stays inside the 18 px strip at any heading.
            float cx = S - 20f, cy = Inset + (Top - Inset - 1f) * 0.5f;
            c.Ring(cx, cy, 2.2f, 1f, ink);
            MetChartMath.Barb(model.WindMps * 1.943844f, out int pennants, out int full, out bool half);
            if (pennants + full == 0 && !half) return;                       // calm: the circle alone
            MetChartMath.BarbAxis(model.WindFromDeg, out float ux, out float uy);
            float vx = -uy, vy = ux;
            const float shaft = 8f, feather = 5f, step = 2.6f;
            c.Line(cx + ux * 2.2f, cy + uy * 2.2f, cx + ux * shaft, cy + uy * shaft, 1f, ink);
            float at = shaft;
            for (int i = 0; i < pennants && at > 3f; i++)
            {
                float px = cx + ux * at, py = cy + uy * at;
                c.Tri(px, py, px - ux * 3.2f, py - uy * 3.2f, px + vx * feather, py + vy * feather, ink); at -= step + 1.4f;
            }
            for (int i = 0; i < full && at > 3f; i++)
            {
                float px = cx + ux * at, py = cy + uy * at;
                c.Line(px, py, px + ux * 1.5f + vx * feather, py + uy * 1.5f + vy * feather, 1f, ink); at -= step;
            }
            if (half && at > 2.4f)
            {
                float px = cx + ux * at, py = cy + uy * at;
                c.Line(px, py, px + ux * 0.8f + vx * feather * 0.5f, py + uy * 0.8f + vy * feather * 0.5f, 1f, ink);
            }
        }

        private void DrawLegend(EnvCanvas c)
        {
            MetFrontKind[] kinds = { MetFrontKind.Warm, MetFrontKind.Cold, MetFrontKind.Occluded, MetFrontKind.Squall };
            for (int i = 0; i < 8; i++)
            {
                float x = 4f + (i % 4) * 74f, y = LegendTop + 5f + (i / 4) * 16f;
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
