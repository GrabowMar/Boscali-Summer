using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
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
    /// <summary>BEHAVIOUR › SORTIE, the timeline half (spec 2026-10-04 §4.4): each lane's steps as bars, PLAN (frozen at EXECUTE from
    /// distances, speeds and TIME ends; an open end runs to the edge with a "?") over REAL (when each step went out and was done), the
    /// element's stance as a band, the events as diamonds and NOW, on one axis from EXECUTE to a little past now. Before EXECUTE it shows
    /// the plan as drawn. The bars are data; the card is <see cref="SortieTimeline"/>. The whole page is rebuilt when something it shows
    /// changed, and once a second while a plan runs.</summary>
    internal sealed partial class WmcPlan
    {
        private SortieTimeline timeline;
        private readonly float[,] drawnStart = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[,] drawnEnd = new float[WingPlan.Lanes, WingPlan.MaxSteps];
        private readonly float[] fromX = new float[WingPlan.Lanes], fromZ = new float[WingPlan.Lanes], fromSpeed = new float[WingPlan.Lanes];
        private long sortieKey = long.MinValue;
        /// <summary>The stance names a lane's band shows when there is no host service to ask (offline renders).</summary>
        private string[] bandSeed = null;

        private void RefreshSortie(WmcContext c)
        {
            WingPlans plans = Plans;
            WingPlan plan = plans?.Plan;
            WingService w = c.Client ? null : c.Wing;
            PlanRunner r = plans?.Runner;
            bool running = plans != null && plans.Running, live = r != null && (running || plans.Completed);
            bool editable = plan != null && !running && w != null;
            if (plan != null && (selLane < 0 || selLane >= WingPlan.Lanes)) selLane = 0;
            if (plan != null && selStep >= plan.Steps[selLane].Count) selStep = -1;
            // Review minor: a finished run's clock stops at its end ("DONE T+" kept counting).
            float now = !live ? 0f : (running ? c.MissionTime : plans.FinishedAt) - r.ExecutedAt;

            // The key: plan edits, runner states, selection, the events and acks, the filter, the second, where the elements are (to the
            // 100 m, before a run) and the confirmations.
            WingEventRing events = c.Client ? null : c.Wing?.Events;
            RadioLog radio = RadioDirector.Instance?.Log;
            long key = version * 7919L + (running ? 1 : 0) + (plans != null && plans.Completed ? 2 : 0) + selLane * 13L + selStep * 131L
                       + c.Count * 17L + c.Selection.Count * 257L + (long)(live ? now : 0f) * 3L + (debrief ? 5L : 0L)
                       + LogRows.Stamp(events, radio) * 31L + WingAcks.Feed.Count * 37L + (logElement + 2) * 7L + c.Selection.Single * 3L + (logSelected ? 1L : 0L);
            for (int e = 0; e < WingPlan.Lanes && plan != null; e++)
            {
                key = key * 31L + plan.Steps[e].Count + (w != null && w.Roster.InUse(e) ? 1 : 0);
                if (r != null)
                    for (int s = 0; s < plan.Steps[e].Count; s++) key = key * 7L + (int)r.State(e, s);
                if (!live && From(w, e, out float fx, out float fz, out _)) key = key * 31L + (long)(fx / 100f) * 3L + (long)(fz / 100f);
            }
            key = key * 7L + (planGate.IsArmed("new", Time.unscaledTime) ? 1 : 0) + (planGate.IsArmed("abort", Time.unscaledTime) ? 2 : 0)
                  + (selStep >= 0 && planGate.IsArmed("del" + selLane + "." + selStep, Time.unscaledTime) ? 4 : 0);
            if (key == sortieKey) return;
            sortieKey = key;
            relayout = true;

            if (plan == null)
            {
                RefreshPlanParts(c, null, plans, w, false, false, 0);
                timeline.Begin();
                timeline.ShowLanes(0, false);
                timeline.End();
                RebuildEvents(c, null, null, false, w, 0f);
                RefreshEventList();
                return;
            }
            PlanTimes(c, plan, plans, r, w, live);
            int shown = 0;
            for (int l = 0; l < WingPlan.Lanes; l++)
                if (plan.Steps[l].Count > 0) shown++;
            lanesShown = shown;
            RebuildEvents(c, plan, r, live, w, now);
            DrawTimeline(plan, r, w, live, now);
            RefreshPlanParts(c, plan, plans, w, running, editable, shown);
            RefreshStepRow(plan, r, live);
            RefreshEventList();
        }

        /// <summary>Each step's planned start and end: frozen at EXECUTE; before it, the plan as drawn from where the elements are now.</summary>
        private void PlanTimes(WmcContext c, WingPlan plan, WingPlans plans, PlanRunner r, WingService w, bool live)
        {
            if (live)
            {
                System.Array.Copy(plans.PlannedStart, drawnStart, drawnStart.Length);
                System.Array.Copy(plans.PlannedEnd, drawnEnd, drawnEnd.Length);
                return;
            }
            for (int l = 0; l < WingPlan.Lanes; l++)
                if (!From(w, l, out fromX[l], out fromZ[l], out fromSpeed[l])) fromSpeed[l] = 150f;
            PlanTimeline.Planned(plan, fromX, fromZ, fromSpeed, drawnStart, drawnEnd);
        }

        private void DrawTimeline(WingPlan plan, PlanRunner r, WingService w, bool live, float now)
        {
            float span = Mathf.Max(60f, now);
            for (int l = 0; l < WingPlan.Lanes; l++)
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    if (!float.IsNaN(drawnEnd[l, s])) span = Mathf.Max(span, drawnEnd[l, s]);
                    else if (!float.IsNaN(drawnStart[l, s])) span = Mathf.Max(span, drawnStart[l, s] + 60f);
                }
            span *= 1.1f;
            timeline.Begin();
            for (int i = 0; i < 5; i++) timeline.Axis(i, "T+" + Clock(span * i / 4f));
            int row = 0;
            for (int l = 0; l < WingPlan.Lanes; l++)
            {
                if (plan.Steps[l].Count == 0) continue;
                Color color = WmcMapOverlay.ElementColor(l);
                timeline.Lane(row, ElementRoster.Letter(l), color, Band(w, l));
                for (int s = 0; s < plan.Steps[l].Count; s++)
                {
                    string kind = PlanWords.Kind(plan.Steps[l][s].Kind);
                    float ps = drawnStart[l, s], pe = drawnEnd[l, s];
                    if (!float.IsNaN(ps))
                        timeline.Plan(row, l, s, ps / span, float.IsNaN(pe) ? 1f : pe / span, float.IsNaN(pe) ? kind + " ?" : kind, color, l == selLane && s == selStep);
                    float rs = live ? r.StartedAt(l, s) : float.NaN, re = live ? r.EndedAt(l, s) : float.NaN;
                    if (!float.IsNaN(rs)) timeline.Real(row, rs / span, float.IsNaN(re) ? now / span : re / span, color);
                }
                row++;
            }
            if (live)
                foreach (SortieEvent e in sortieEvents)
                {
                    float t = e.Time - r.ExecutedAt;
                    if (t >= 0f && t <= span) timeline.Event(t / span, e.Tone == 2 ? AvTheme.Alert : e.Tone == 1 ? AvTheme.Warning : AvTheme.Accent);
                }
            timeline.Now(live ? now / span : -1f, "NOW");
            timeline.ShowLanes(row, live);
            timeline.End();
        }

        /// <summary>The lane's stance (the band): the book's stance the element flies, with a * when it has been tuned since.</summary>
        private string Band(WingService w, int lane)
        {
            if (w == null) return bandSeed != null && lane < bandSeed.Length ? bandSeed[lane] ?? "" : "";
            if (!w.Roster.InUse(lane)) return "";
            byte[] axes = StanceDoctrine.Axes(w.DoctrineOf(lane));
            Stance s = StanceWords.Closest(WmcStanceActions.Book.All, axes, out int mask);
            return s == null ? "CUSTOM" : s.Name + (mask != 0 ? " *" : "");
        }

        /// <summary>The selected step planned against actual: "B2 CAP", "PLANNED T+1:50 · ACTUAL T+2:40", LATE 0:50 on the right.</summary>
        private void RefreshStepRow(WingPlan plan, PlanRunner r, bool live)
        {
            if (!(selStep >= 0 && selStep < plan.Steps[selLane].Count)) return;
            PlanStep p = plan.Steps[selLane][selStep];
            float planned = drawnStart[selLane, selStep];
            float actual = live ? r.StartedAt(selLane, selStep) : float.NaN;
            string state = PlanWords.State(live ? r : null, selLane, selStep);
            string why = live && r.State(selLane, selStep) == StepState.Blocked ? " · " + (r.Why(selLane) ?? "refused") : "";
            string diff = SortieWords.Diff(planned, actual);
            stepRow.Set(PlanRules.Name(selLane, selStep) + " " + PlanWords.Kind(p.Kind),
                "PLANNED " + (float.IsNaN(planned) ? "?" : "T+" + Clock(planned)) + " · ACTUAL " + (float.IsNaN(actual) ? "-" : "T+" + Clock(actual)) + " · " + state + why,
                diff.Length > 0 ? diff : state,
                diff.StartsWith("LATE", System.StringComparison.Ordinal) || state == "HELD" || state == "BLOCKED" ? AvState.Caution : state == "RUN" ? AvState.Ready : AvState.Info);
            stepRow.Help = "The selected step. Its START and END are below; tap the bar again to let go.";
        }
    }
}
