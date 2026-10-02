using NOAvionics;
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
    /// <summary>BEHAVIOUR › RECORD, the TIMELINE half (spec bezel v2 §5, P3): each lane's steps as bars, PLAN (frozen at EXECUTE from
    /// distances, speeds and TIME ends; an open end runs to the edge with a "?") over REAL (when each step went out and was done), on
    /// one axis from EXECUTE to a little past now. Before EXECUTE it shows the plan as drawn. The bars are data, so their geometry is
    /// the v1 geometry; the frame, the axis and the lane labels are kit v2 (<see cref="TimelineView"/> is one flow part). Compact: only as
    /// tall as its lanes need, under a TIMELINE section; LOG follows (<see cref="BuildRecord"/>). Once a second while it shows.</summary>
    internal sealed partial class WmcPlan
    {
        private const float AxisH = 16f, BarRow = 13f, LaneLabel = 86f, CardPad = 6f;
        private const float BarAreaH = WingPlan.Lanes * 2f * BarRow + 8f;
        private const float TimelineCardH = AxisH + BarAreaH + CardPad * 2f;
        private const int MaxBars = WingPlan.Lanes * WingPlan.MaxSteps * 2;

        private TimelineView timeline;
        private readonly float[,] drawnStart = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[,] drawnEnd = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[] fromX = new float[WingPlan.Lanes], fromZ = new float[WingPlan.Lanes], fromSpeed = new float[WingPlan.Lanes];
        private float timelineNext;

        /// <summary>TIMELINE's section and card over LOG's chips and lines, one page (TIMELINE and LOG merged into RECORD).</summary>
        private void BuildRecord(AvFlow f, AvTicker t, int pageIndex)
        {
            f.Section(AvIcon.ChartLine, "TIMELINE", "PLAN OVER REAL");
            timeline = f.Add(new TimelineView(f.Content));
            BuildLog(f, t);
        }

        private void RefreshTimeline(WmcContext c)
        {
            if (Time.unscaledTime < timelineNext && !timeline.Moved) return;
            timeline.Moved = false;
            timelineNext = Time.unscaledTime + 1f;
            WingPlans plans = WingPlans.Instance;
            WingPlan plan = plans?.Plan;
            int steps = plan == null ? 0 : CountSteps(plan);
            bool none = steps == 0;
            timeline.ShowEmpty(none);
            if (none) return;
            PlanRunner r = plans.Runner;
            bool live = r != null && (r.Running || plans.Completed);
            // PLAN: frozen at EXECUTE; before it, the plan as drawn from where the elements are now.
            if (live) System.Array.Copy(plans.PlannedStart, drawnStart, drawnStart.Length);
            if (live) System.Array.Copy(plans.PlannedEnd, drawnEnd, drawnEnd.Length);
            else
            {
                WingService w = c.Client ? null : c.Wing;
                for (int l = 0; l < WingPlan.Lanes; l++)
                    if (!From(w, l, out fromX[l], out fromZ[l], out fromSpeed[l])) fromSpeed[l] = 150f;
                PlanTimeline.Planned(plan, fromX, fromZ, fromSpeed, drawnStart, drawnEnd);
            }
            // Review minor: a finished run's clock stops at its end ("DONE T+" kept counting).
            float now = !live || WingService.Instance == null ? 0f
                : (r.Running ? WingService.Instance.MissionTime : plans.FinishedAt) - r.ExecutedAt;
            float span = Mathf.Max(60f, now);
            for (int l = 0; l < WingPlan.Lanes; l++)
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    if (!float.IsNaN(drawnEnd[l, s])) span = Mathf.Max(span, drawnEnd[l, s]);
                    else if (!float.IsNaN(drawnStart[l, s])) span = Mathf.Max(span, drawnStart[l, s] + 60f);
                }
            span *= 1.1f;
            for (int i = 0; i < 5; i++) timeline.Axis(i, "T+" + Clock(span * i / 4f));
            timeline.Now(live ? (r.Running ? "NOW T+" : "DONE T+") + Clock(now) : "NOT RUN");

            int bar = 0, laneRow = 0;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                bool on = plan.Steps[l].Count > 0;
                timeline.Lane(l, on, laneRow, ElementRoster.Letter(l), WmcMapOverlay.ElementColor(l));
                if (!on) continue;
                float y = laneRow * 2f * BarRow + laneRow * 4f;
                Color planColor = WmcMapOverlay.ElementColor(l), realColor = planColor;
                planColor.a = 0.35f;
                realColor.a = 0.85f;
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    string kind = PlanWords.Kind(plan.Steps[l][s].Kind);
                    float ps = drawnStart[l, s], pe = drawnEnd[l, s];
                    if (!float.IsNaN(ps)) bar = Bar(bar, y, ps, float.IsNaN(pe) ? span : pe, span, planColor, float.IsNaN(pe) ? kind + " ?" : kind);
                    float rs = live ? r.StartedAt(l, s) : float.NaN, re = live ? r.EndedAt(l, s) : float.NaN;
                    if (!float.IsNaN(rs)) bar = Bar(bar, y + BarRow, rs, float.IsNaN(re) ? now : re, span, realColor, kind);
                }
                laneRow++;
            }
            timeline.HideBarsFrom(bar);
        }

        private int Bar(int i, float y, float from, float to, float span, Color color, string text)
        {
            if (i >= MaxBars) return i;
            float bw = timeline.BarWidth;
            float x0 = LaneLabel + Mathf.Clamp01(from / span) * bw, x1 = LaneLabel + Mathf.Clamp01(to / span) * bw;
            timeline.Bar(i, y, x0, Mathf.Max(3f, x1 - x0), color, text);
            return i + 1;
        }

        private static int CountSteps(WingPlan plan)
        {
            int n = 0;
            for (int l = 0; l < WingPlan.Lanes; l++) n += plan.Steps[l].Count;
            return n;
        }

        /// <summary>The timeline card as one flow part: a kit v2 frame, the axis and lane labels in the type roles, and the bars, which
        /// are data (element colours, as on the map).</summary>
        private sealed class TimelineView : AvPart
        {
            private readonly AvFrame frame;
            private readonly RectTransform barArea;
            private readonly TMP_Text[] axisLabels = new TMP_Text[5];
            private readonly TMP_Text nowLabel, empty;
            private readonly TMP_Text[] laneLabels = new TMP_Text[WingPlan.Lanes * 2];
            private readonly Color[] laneColors = new Color[WingPlan.Lanes];
            private readonly Image[] bars = new Image[MaxBars];
            private readonly TMP_Text[] barTexts = new TMP_Text[MaxBars];
            private float width;

            public TimelineView(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "Timeline");
                frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
                AvLay.Fill(frame.rectTransform);
                frame.Bracket = 8f;
                for (int i = 0; i < axisLabels.Length; i++)
                {
                    axisLabels[i] = AvText.Make(Rect, "Axis" + i, AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                    AvText.Fit(axisLabels[i], false);
                }
                nowLabel = AvText.Make(Rect, "Now", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(nowLabel, false);
                barArea = AvLay.Child(Rect, "Bars");
                for (int i = 0; i < laneLabels.Length; i++)
                {
                    laneLabels[i] = AvText.Make(barArea, "Lane" + i, i % 2 == 0 ? AvTextRole.Label : AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                    AvText.Fit(laneLabels[i], false);
                    laneLabels[i].gameObject.SetActive(false);
                }
                for (int i = 0; i < MaxBars; i++)
                {
                    bars[i] = AvLay.Solid(barArea, "Bar" + i, Color.white);
                    barTexts[i] = AvText.Make(barArea, "BarText" + i, AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                    AvText.Fit(barTexts[i], false);
                    bars[i].gameObject.SetActive(false);
                    barTexts[i].gameObject.SetActive(false);
                }
                empty = AvText.Make(Rect, "Empty", AvTextRole.ProseSmall, "No steps yet: draw the plan on PLAN.", TextAlignmentOptions.MidlineLeft);
                Restyle();
            }

            /// <summary>The card was placed at a new width: the bars are drawn again on the next refresh.</summary>
            public bool Moved { get; set; } = true;

            public float BarWidth => Mathf.Max(1f, width - CardPad * 2f - LaneLabel);

            public override float Measure(float w) => TimelineCardH;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                if (!Mathf.Approximately(width, s.W)) Moved = true;
                width = s.W;
                float innerW = s.W - CardPad * 2f, bw = BarWidth;
                for (int i = 0; i < axisLabels.Length; i++)
                    AvLay.Place(axisLabels[i].rectTransform, CardPad + LaneLabel + i * bw / 4f - 20f, CardPad, 56f, AxisH - 2f);
                AvLay.Place(nowLabel.rectTransform, CardPad + innerW - 110f, CardPad, 110f, AxisH - 2f);
                AvLay.Place(barArea, CardPad, CardPad + AxisH, innerW, BarAreaH);
                AvLay.Place(empty.rectTransform, CardPad, CardPad, innerW, 20f);
            }

            public override void Restyle()
            {
                AvStyle c = AvStyleHost.FuiStyle("card");
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                frame.BracketColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
                frame.SetVerticesDirty();
                Color dim = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                Color ink = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
                foreach (TMP_Text t in axisLabels) t.color = dim;
                nowLabel.color = ink;
                empty.color = dim;
                for (int i = 0; i < laneLabels.Length; i++) laneLabels[i].color = i % 2 == 0 ? laneColors[i / 2] : dim;
                foreach (TMP_Text t in barTexts) t.color = ink;
            }

            public void ShowEmpty(bool none)
            {
                if (empty.gameObject.activeSelf != none) empty.gameObject.SetActive(none);
                if (barArea.gameObject.activeSelf == none) barArea.gameObject.SetActive(!none);
                if (axisLabels[0].gameObject.activeSelf == none)
                    foreach (TMP_Text t in axisLabels) t.gameObject.SetActive(!none);
                if (nowLabel.gameObject.activeSelf == none) nowLabel.gameObject.SetActive(!none);
            }

            public void Axis(int i, string text) => Set(axisLabels[i], text);

            public void Now(string text) => Set(nowLabel, text);

            /// <summary>Lane <paramref name="l"/>'s two labels (PLAN over REAL) on row <paramref name="row"/>, in its element's colour.</summary>
            public void Lane(int l, bool on, int row, string letter, Color color)
            {
                TMP_Text plan = laneLabels[2 * l], real = laneLabels[2 * l + 1];
                if (plan.gameObject.activeSelf != on) plan.gameObject.SetActive(on);
                if (real.gameObject.activeSelf != on) real.gameObject.SetActive(on);
                if (!on) return;
                float y = row * 2f * BarRow + row * 4f;
                AvLay.Place(plan.rectTransform, 0f, y, LaneLabel - 4f, BarRow - 1f);
                AvLay.Place(real.rectTransform, 0f, y + BarRow, LaneLabel - 4f, BarRow - 1f);
                Set(plan, letter + "  PLAN");
                Set(real, "   REAL");
                laneColors[l] = color;
                plan.color = color;
            }

            public void Bar(int i, float y, float x, float w, Color color, string text)
            {
                Image b = bars[i];
                AvLay.Place(b.rectTransform, x, y + 1f, w, BarRow - 3f);
                if (b.color != color) b.color = color;
                if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
                TMP_Text t = barTexts[i];
                bool fits = w > 34f;
                if (t.gameObject.activeSelf != fits) t.gameObject.SetActive(fits);
                if (!fits) return;
                AvLay.Place(t.rectTransform, x + 2f, y, w - 3f, BarRow - 2f);
                Set(t, text);
            }

            public void HideBarsFrom(int first)
            {
                for (int i = first; i < MaxBars; i++)
                {
                    if (bars[i].gameObject.activeSelf) bars[i].gameObject.SetActive(false);
                    if (barTexts[i].gameObject.activeSelf) barTexts[i].gameObject.SetActive(false);
                }
            }

            private static void Set(TMP_Text t, string text)
            {
                if (t.text != text) t.text = text;
            }
        }
    }
}
