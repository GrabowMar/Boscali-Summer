using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>One mark of the SORTIE timeline: a solid bar, a bar with a dashed edge (the plan), or a diamond (an event). One mesh each;
    /// the kit has no dashed outline, so the dashes are drawn here. Made with an explicit CanvasRenderer (kit rule for raw graphics).</summary>
    internal sealed class SortieMark : MaskableGraphic
    {
        public enum Shape { Fill, Dashed, Diamond }

        private const float Dash = 3f, Gap = 2f;
        private Shape shape;
        private Color edge = Color.white;

        public static SortieMark Make(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            SortieMark m = go.AddComponent<SortieMark>();
            m.raycastTarget = false;
            return m;
        }

        /// <summary>Sets what the mark looks like; nothing is rebuilt when nothing changed.</summary>
        public void Look(Shape s, Color fill, Color edgeColor)
        {
            if (s == shape && fill == color && edgeColor == edge) return;
            shape = s;
            color = fill;
            edge = edgeColor;
            SetVerticesDirty();
        }

        public void Put(float x, float y, float w, float h)
        {
            var rt = rectTransform;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (r.width <= 0f || r.height <= 0f) return;
            if (shape == Shape.Diamond)
            {
                float cx = r.center.x, cy = r.center.y;
                Vert(vh, cx, r.yMax, color);
                Vert(vh, r.xMax, cy, color);
                Vert(vh, cx, r.yMin, color);
                Vert(vh, r.xMin, cy, color);
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(2, 3, 0);
                return;
            }
            Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, color);
            if (shape != Shape.Dashed) return;
            for (float x = r.xMin; x < r.xMax; x += Dash + Gap)
            {
                float x1 = Mathf.Min(x + Dash, r.xMax);
                Quad(vh, x, r.yMax - 1f, x1, r.yMax, edge);
                Quad(vh, x, r.yMin, x1, r.yMin + 1f, edge);
            }
            for (float y = r.yMin; y < r.yMax; y += Dash + Gap)
            {
                float y1 = Mathf.Min(y + Dash, r.yMax);
                Quad(vh, r.xMin, y, r.xMin + 1f, y1, edge);
                Quad(vh, r.xMax - 1f, y, r.xMax, y1, edge);
            }
        }

        private static void Vert(VertexHelper vh, float x, float y, Color c) => vh.AddVert(new Vector3(x, y), c, Vector2.zero);

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color c)
        {
            int i = vh.currentVertCount;
            Vert(vh, x0, y0, c);
            Vert(vh, x0, y1, c);
            Vert(vh, x1, y1, c);
            Vert(vh, x1, y0, c);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    /// <summary>BEHAVIOUR › SORTIE's timeline (spec 2026-10-04 §4.4; mockup behaviour.html #a): per element a lane with its stance band,
    /// the PLAN as dashed bars over the REAL ones as solid bars, event diamonds along the foot and the NOW line, on one axis. A bar is
    /// a button: it picks its step. The page hands in fractions of the axis (0..1) and the timeline places them at its width; the frame,
    /// axis and labels are kit v2, the marks are data (element colours, as on the map).</summary>
    internal sealed class SortieTimeline : AvPart
    {
        public const int MaxLanes = WingPlan.Lanes, MaxBars = WingPlan.Lanes * WingPlan.MaxSteps, MaxEvents = 24;
        private const float PadX = 6f, PadTop = 4f, AxisH = 14f, LabelW = 18f, BandH = 9f, PlanH = 15f, RealH = 12f, LaneGap = 6f, EventH = 14f, PadBottom = 6f;
        private const float LaneH = BandH + 1f + PlanH + 1f + RealH, Stride = LaneH + LaneGap;

        private readonly AvFrame frame;
        private readonly TMP_Text[] axis = new TMP_Text[5];
        private readonly TMP_Text nowLabel, empty;
        private readonly TMP_Text[] letters = new TMP_Text[MaxLanes], bandText = new TMP_Text[MaxLanes];
        private readonly SortieMark[] bands = new SortieMark[MaxLanes], laneHits = new SortieMark[MaxLanes], grid = new SortieMark[5];
        private readonly SortieMark[] planMarks = new SortieMark[MaxBars], realMarks = new SortieMark[MaxBars], diamonds = new SortieMark[MaxEvents];
        private readonly TMP_Text[] planText = new TMP_Text[MaxBars];
        private readonly SortieMark nowLine;
        private readonly Color[] laneColor = new Color[MaxLanes];
        private readonly bool[] laneOn = new bool[MaxLanes];
        private readonly int[] planRow = new int[MaxBars], realRow = new int[MaxBars], planLane = new int[MaxBars], planStep = new int[MaxBars];
        private readonly float[] planF0 = new float[MaxBars], planF1 = new float[MaxBars], realF0 = new float[MaxBars], realF1 = new float[MaxBars];
        private readonly float[] eventF = new float[MaxEvents];
        private readonly string[] letterShown = new string[MaxLanes];
        private int lanes, plans, reals, events;
        private float nowF = -1f, width;
        private bool eventRow;

        /// <summary>A bar was pressed: its lane and step (step -1: the lane's letter).</summary>
        public Action<int, int> Picked;

        public SortieTimeline(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Sortie");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = 8f;
            for (int i = 0; i < axis.Length; i++)
            {
                grid[i] = SortieMark.Make(Rect, "Grid" + i);
                axis[i] = AvText.Make(Rect, "Axis" + i, AvTextRole.Micro, "", i == 0 ? TextAlignmentOptions.MidlineLeft : i == 4 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Midline);
                AvText.Fit(axis[i], false);
            }
            for (int l = 0; l < MaxLanes; l++)
            {
                int lane = l;
                bands[l] = SortieMark.Make(Rect, "Band" + l);
                bandText[l] = AvText.Make(Rect, "BandText" + l, AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                AvText.Fit(bandText[l], false);
                laneHits[l] = SortieMark.Make(Rect, "Lane" + l);
                letters[l] = AvText.Make(Rect, "Letter" + l, AvTextRole.Label, "", TextAlignmentOptions.Midline);
                AvText.Fit(letters[l], false);
                AvHit.On(laneHits[l]).Click = e => Picked?.Invoke(lane, -1);
            }
            for (int i = 0; i < MaxBars; i++)
            {
                int at = i;
                planMarks[i] = SortieMark.Make(Rect, "Plan" + i);
                planText[i] = AvText.Make(Rect, "PlanText" + i, AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                AvText.Fit(planText[i], false);
                realMarks[i] = SortieMark.Make(Rect, "Real" + i);
                AvHit.On(planMarks[i]).Click = e => Picked?.Invoke(planLane[at], planStep[at]);
            }
            for (int i = 0; i < MaxEvents; i++) diamonds[i] = SortieMark.Make(Rect, "Event" + i);
            nowLine = SortieMark.Make(Rect, "Now");
            nowLabel = AvText.Make(Rect, "NowText", AvTextRole.Micro, "NOW", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(nowLabel, false);
            empty = AvText.Make(Rect, "Empty", AvTextRole.ProseSmall, "No plan yet: queue orders on ORDERS (SHIFT adds a step), then EXECUTE here.", TextAlignmentOptions.TopLeft, true);
            Hide();
            Restyle();
        }

        /// <summary>The card was placed at a new width: the bars are drawn again on the next refresh.</summary>
        public bool Moved { get; set; } = true;

        private float PlotX => PadX + LabelW + 4f;
        private float PlotW => Mathf.Max(1f, width - PlotX - PadX);
        private float LaneTop(int row) => PadTop + AxisH + row * Stride;

        public override float Measure(float w)
        {
            if (lanes == 0) return PadTop + AvText.Height(empty, w - 2f * PadX) + PadBottom + 4f;
            return PadTop + AxisH + lanes * Stride - LaneGap + (eventRow ? EventH + 4f : 0f) + PadBottom;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            if (!Mathf.Approximately(width, s.W)) Moved = true;
            width = s.W;
            AvLay.Place(empty.rectTransform, PadX, PadTop, s.W - 2f * PadX, s.H - PadTop);
            Layout();
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            frame.BracketColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
            frame.SetVerticesDirty();
            Color dim = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            Color ink = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            foreach (TMP_Text t in axis) t.color = dim;
            empty.color = dim;
            nowLabel.color = ink;
            foreach (TMP_Text t in planText) t.color = ink;
            for (int l = 0; l < MaxLanes; l++)
            {
                letters[l].color = laneColor[l];
                bandText[l].color = AvTheme.Accent;
            }
            for (int i = 0; i < grid.Length; i++) grid[i].Look(SortieMark.Shape.Fill, AvTheme.Hairline.WithAlpha(0.45f), Color.clear);
            nowLine.Look(SortieMark.Shape.Fill, ink, Color.clear);
        }

        // ---------------------------------------------------------------- what the page hands in

        public void ShowLanes(int count, bool withEvents)
        {
            if (count == lanes && withEvents == eventRow) return;
            lanes = count;
            eventRow = withEvents;
            Changed();
        }

        public void Begin()
        {
            plans = reals = events = 0;
            for (int l = 0; l < MaxLanes; l++) laneOn[l] = false;
            nowF = -1f;
        }

        public void Axis(int i, string text)
        {
            if (axis[i].text != text) axis[i].text = text;
        }

        public void Now(float fraction, string text)
        {
            nowF = fraction;
            if (nowLabel.text != text) nowLabel.text = text;
        }

        /// <summary>Lane <paramref name="row"/>: its element letter and colour, and the stance it flies (the band).</summary>
        public void Lane(int row, string letter, Color color, string stance)
        {
            if (row >= MaxLanes) return;
            laneOn[row] = true;
            laneColor[row] = color;
            if (letters[row].text != letter) letters[row].text = letter;
            letters[row].color = color;
            if (bandText[row].text != stance) bandText[row].text = stance;
            bandText[row].color = AvTheme.Accent;
            bands[row].Look(SortieMark.Shape.Fill, AvTheme.Accent.WithAlpha(0.14f), Color.clear);
            letterShown[row] = letter;
        }

        public void Plan(int row, int lane, int step, float f0, float f1, string text, Color color, bool selected)
        {
            if (plans >= MaxBars) return;
            int i = plans++;
            planRow[i] = row;
            planLane[i] = lane;
            planStep[i] = step;
            planF0[i] = f0;
            planF1[i] = f1;
            if (planText[i].text != text) planText[i].text = text;
            planMarks[i].Look(SortieMark.Shape.Dashed, color.WithAlpha(0.16f), selected ? AvTheme.Warning : color.WithAlpha(0.9f));
        }

        public void Real(int row, float f0, float f1, Color color)
        {
            if (reals >= MaxBars) return;
            int i = reals++;
            realRow[i] = row;
            realF0[i] = f0;
            realF1[i] = f1;
            realMarks[i].Look(SortieMark.Shape.Fill, color.WithAlpha(0.85f), Color.clear);
        }

        public void Event(float fraction, Color color)
        {
            if (events >= MaxEvents) return;
            eventF[events] = fraction;
            diamonds[events].Look(SortieMark.Shape.Diamond, color, Color.clear);
            events++;
        }

        /// <summary>The page handed everything in: place it at the current width.</summary>
        public void End()
        {
            Moved = false;
            Layout();
        }

        private void Hide()
        {
            foreach (SortieMark m in grid) m.gameObject.SetActive(false);
            foreach (SortieMark m in bands) m.gameObject.SetActive(false);
            foreach (SortieMark m in laneHits) m.gameObject.SetActive(false);
            foreach (SortieMark m in planMarks) m.gameObject.SetActive(false);
            foreach (SortieMark m in realMarks) m.gameObject.SetActive(false);
            foreach (SortieMark m in diamonds) m.gameObject.SetActive(false);
            foreach (TMP_Text t in planText) t.gameObject.SetActive(false);
            foreach (TMP_Text t in bandText) t.gameObject.SetActive(false);
            foreach (TMP_Text t in letters) t.gameObject.SetActive(false);
            foreach (TMP_Text t in axis) t.gameObject.SetActive(false);
            nowLine.gameObject.SetActive(false);
            nowLabel.gameObject.SetActive(false);
        }

        private static void On(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        private void Layout()
        {
            bool any = lanes > 0 && width > 1f;
            On(empty, lanes == 0);
            if (!any)
            {
                Hide();
                return;
            }
            float px = PlotX, pw = PlotW, bottom = PadTop + AxisH + lanes * Stride - LaneGap;
            for (int i = 0; i < axis.Length; i++)
            {
                float x = px + pw * i / 4f;
                On(axis[i], true);
                On(grid[i], true);
                grid[i].Put(Mathf.Min(x, px + pw - 1f), PadTop + AxisH - 2f, 1f, bottom - PadTop - AxisH + 4f);
                float lw = 52f;
                float lx = i == 0 ? x : i == 4 ? x - lw : x - lw * 0.5f;
                AvLay.Place(axis[i].rectTransform, lx, PadTop, lw, AxisH - 2f);
            }
            for (int l = 0; l < MaxLanes; l++)
            {
                bool on = laneOn[l] && l < lanes;
                On(letters[l], on);
                On(laneHits[l], on);
                On(bands[l], on);
                On(bandText[l], on && bandText[l].text.Length > 0);
                if (!on) continue;
                float y = LaneTop(l);
                AvLay.Place(letters[l].rectTransform, PadX, y, LabelW, LaneH);
                laneHits[l].Put(PadX, y, LabelW, LaneH);
                laneHits[l].Look(SortieMark.Shape.Fill, new Color(0f, 0f, 0f, 0.003f), Color.clear);
                AvHit.On(laneHits[l]);
                bands[l].Put(px, y, pw, BandH);
                AvLay.Place(bandText[l].rectTransform, px + 3f, y, pw - 6f, BandH);
            }
            for (int i = 0; i < MaxBars; i++)
            {
                bool p = i < plans, r = i < reals;
                On(planMarks[i], p);
                On(realMarks[i], r);
                if (p)
                {
                    float x0 = px + Mathf.Clamp01(planF0[i]) * pw, x1 = px + Mathf.Clamp01(planF1[i]) * pw, w = Mathf.Max(3f, x1 - x0 - 1f);
                    float y = LaneTop(planRow[i]) + BandH + 1f;
                    planMarks[i].Put(x0, y, w, PlanH);
                    bool fits = w > 34f;
                    On(planText[i], fits);
                    if (fits) AvLay.Place(planText[i].rectTransform, x0 + 3f, y, w - 4f, PlanH);
                }
                else On(planText[i], false);
                if (!r) continue;
                float rx0 = px + Mathf.Clamp01(realF0[i]) * pw, rx1 = px + Mathf.Clamp01(realF1[i]) * pw;
                realMarks[i].Put(rx0, LaneTop(realRow[i]) + BandH + PlanH + 2f, Mathf.Max(3f, rx1 - rx0 - 1f), RealH);
            }
            for (int i = 0; i < MaxEvents; i++)
            {
                bool on = i < events && eventRow;
                On(diamonds[i], on);
                if (on) diamonds[i].Put(px + Mathf.Clamp01(eventF[i]) * pw - 4f, bottom + 4f + (i % 2) * 0f, 8f, 8f);
            }
            bool now = nowF >= 0f;
            On(nowLine, now);
            On(nowLabel, now);
            if (now)
            {
                float x = px + Mathf.Clamp01(nowF) * pw;
                nowLine.Put(x, PadTop + AxisH - 2f, 1.5f, bottom - PadTop - AxisH + 2f);
                bool left = x > px + pw - 34f;
                nowLabel.alignment = left ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
                AvLay.Place(nowLabel.rectTransform, left ? x - 36f : x + 3f, bottom - 10f, 34f, 10f);
            }
        }
    }
}
