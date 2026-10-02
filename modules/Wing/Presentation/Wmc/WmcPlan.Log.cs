using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

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
    /// <summary>BEHAVIOUR › RECORD, the LOG half (spec bezel v2 §5; the room's LOG drawer) on kit v2: the wing's events and radio
    /// lines, newest first, filtered by element or the selected aircraft, under TIMELINE's card (<see cref="WmcPlan.BuildRecord"/>),
    /// as a pooled <see cref="AvList"/> (every line has an id, so no pager). A line with an aircraft opens it on INSPECT and centres the map on it. STEPS and DEBRIEF swap
    /// what the list shows (the plan's steps planned against actual; the sortie's summary).</summary>
    internal sealed partial class WmcPlan
    {
        private const int LogPageRows = LogRows.MaxRows;
        private static readonly string[] LogChipLabels = { "ALL", "A", "B", "C", "D", "SELECTED" };

        /// <summary>What a list line shows and where a click goes (an aircraft id, or none).</summary>
        private struct LogLine
        {
            public string Name, Sub, Value;
            public AvState State;
            public uint Id;
        }

        private readonly List<LogRow> logRows = new List<LogRow>(LogRows.MaxRows);
        private readonly List<LogLine> logLines = new List<LogLine>(LogRows.MaxRows);
        private AvControl[] logChips;
        private AvControl debriefButton, stepsButton;
        private AvSection logSection;
        private AvList logList;
        private WmcLines logEmpty;
        private int logElement = -1;
        private bool logSelected;
        private long logStamp = long.MinValue;
        private bool logFilled, debrief, steps;
        private float stepsNext;

        /// <summary>LOG lines showing now (automation).</summary>
        public int LogRowsShown { get; private set; }

        private void BuildLog(AvFlow f, AvTicker t)
        {
            logSection = f.Section(AvIcon.Message2, "LOG", "");
            var chipSpecs = new AvControl.Spec[LogChipLabels.Length];
            for (int i = 0; i < chipSpecs.Length; i++)
            {
                int k = i;
                chipSpecs[i] = new AvControl.Spec(LogChipLabels[i], () => PickLogFilter(k));
            }
            logChips = f.Buttons(chipSpecs).Controls;
            for (int i = 0; i < logChips.Length; i++) ids.Add("plan.log.filter" + AvNum.Fixed(i, 0), logChips[i]);
            AvControl[] toggles = f.Buttons(
                new AvControl.Spec("STEPS", ToggleSteps, AvButtonStyle.Default, AvIcon.ListDetails),
                new AvControl.Spec("DEBRIEF", ToggleDebrief, AvButtonStyle.Default, AvIcon.Flag)).Controls;
            stepsButton = toggles[0];
            debriefButton = toggles[1];
            stepsButton.Help = "Every plan step: when it was planned, when it really went out, and how late or early.";
            debriefButton.Help = "This sortie in a few lines (kills, losses, tasks), then the last ones.";
            ids.Add("plan.log.steps", stepsButton);
            ids.Add("plan.log.debrief", debriefButton);
            logEmpty = f.Add(new WmcLines(f.Content, 1));
            logEmpty.Set(0, "Nothing logged yet.");
            logList = f.Add(new AvList(f.Content, t, LogPageRows, BindLog));
            logList.RowClicked = ClickLog;
        }

        private void BindLog(int item, AvRow row)
        {
            LogLine l = logLines[item];
            row.Set(l.Name, l.Sub, l.Value, l.State);
            ids.Add("plan.log.row" + AvNum.Fixed(item, 0), row);
        }

        private void PickLogFilter(int chip)
        {
            logSelected = chip == 5;
            logElement = chip >= 1 && chip <= 4 ? chip - 1 : -1;
            logFilled = false;
            if (last != null) RefreshLog(last);
            FlushRelayout();
        }

        private void ClickLog(int item)
        {
            if (item < 0 || item >= logLines.Count) return;
            // By aircraft, never by seat; a gone aircraft centres nothing (review P3 I5).
            uint id = logLines[item].Id;
            if (last == null || id == 0u || WmcContext.UnitOf(id) == null) return;
            WmcMap.Center(WmcContext.UnitOf(id));
            if (WingRows.IndexOf(last.Rows, last.Count, id) >= 0) WmcPanel.Instance?.Inspect(id);
        }

        private void ToggleSteps()
        {
            steps = !steps;
            if (steps) debrief = false;
            stepsButton.Latched = steps;
            debriefButton.Latched = debrief;
            stepsNext = 0f;
            logFilled = false;
            if (last != null) RefreshLog(last);
            FlushRelayout();
        }

        private void ToggleDebrief()
        {
            debrief = !debrief;
            if (debrief) steps = false;
            stepsButton.Latched = steps;
            debriefButton.Latched = debrief;
            logFilled = false;
            if (last != null) RefreshLog(last);
            FlushRelayout();
        }

        private void FlushRelayout()
        {
            if (!relayout) return;
            relayout = false;
            flow.RequestRelayout();
        }

        /// <summary>The lines are in <see cref="logLines"/>: the list and its caption follow.</summary>
        private void ShowLines(string note)
        {
            int n = logLines.Count;
            LogRowsShown = n;
            logList.SetCount(n);
            logEmpty.Set(0, n == 0 ? "Nothing logged yet." : "");
            logSection.SetCaption(note);
            relayout = true;
        }

        private void AddLine(string name, string sub, string value, AvState state, uint id)
        {
            logLines.Add(new LogLine { Name = name, Sub = sub, Value = value, State = state, Id = id });
        }

        /// <summary>STEPS (the old TIMELINE table, kept): STEP · PLANNED · ACTUAL · DIFF per plan step, once a second.</summary>
        private void RefreshSteps()
        {
            if (Time.unscaledTime < stepsNext) return;
            stepsNext = Time.unscaledTime + 1f;
            WingPlans plans = WingPlans.Instance;
            WingPlan plan = plans?.Plan;
            PlanRunner r = plans?.Runner;
            bool live = r != null && (r.Running || plans.Completed);
            logLines.Clear();
            for (int l = 0; plan != null && l < WingPlan.Lanes; l++)
                for (int s = 0; s < plan.Steps[l].Count && logLines.Count < LogRows.MaxRows; s++)
                {
                    float rs = live ? r.StartedAt(l, s) : float.NaN;
                    StepLine(l, s, PlanWords.Kind(plan.Steps[l][s].Kind), drawnStart[l, s], rs);
                }
            ShowLines("STEP · PLANNED · ACTUAL · DIFF");
        }

        /// <summary>"B2 ATTACK" over "PLANNED T+1:50 · ACTUAL T+2:40", LATE 0:50 on the right.</summary>
        private void StepLine(int l, int s, string kind, float planned, float actual)
        {
            string p = float.IsNaN(planned) ? "?" : "T+" + Clock(planned);
            string a = float.IsNaN(actual) ? "-" : "T+" + Clock(actual);
            string diff = "";
            if (!float.IsNaN(planned) && !float.IsNaN(actual))
            {
                float d = actual - planned;
                diff = Mathf.Abs(d) < 5f ? "ON TIME" : (d > 0f ? "LATE " : "EARLY ") + Clock(Mathf.Abs(d));
            }
            AddLine(PlanRules.Name(l, s) + " " + kind, "PLANNED " + p + " · ACTUAL " + a, diff, float.IsNaN(actual) ? AvState.Info : AvState.Ready, 0u);
        }

        /// <summary>DEBRIEF: this sortie's lines, then the first line of each kept one (spec WMC rebuild §PLAN DEBRIEF).</summary>
        private void RefreshDebrief()
        {
            DebriefService d = DebriefService.Instance;
            SortieLog s = d?.Sortie;
            long stamp = s == null ? 0 : ((long)(s.End - s.Start) / 10L) * 131L + s.Launched + s.Airborne * 3 + s.Landed * 7 + s.Lost * 11
                                         + s.Kills * 13 + s.TasksDone * 17 + s.TasksFailed * 19 + s.TargetsDown * 23 + s.Relocated * 29 + s.Gcas * 31;
            if (stamp == logStamp && logFilled) return;
            logStamp = stamp;
            logFilled = true;
            logLines.Clear();
            if (s != null)
                foreach (string line in s.Lines())
                    if (logLines.Count < LogRows.MaxRows) AddLine(logLines.Count == 0 ? "THIS SORTIE" : "SORTIE", line, null, AvState.Ready, 0u);
            DebriefStore store = d?.Store;
            for (int i = 0; store != null && i < store.Count && logLines.Count < LogRows.MaxRows; i++)
                AddLine("EARLIER", store.Lines(i)[0], null, AvState.Info, 0u);
            ShowLines("DEBRIEF");
        }

        private void RefreshLog(WmcContext c)
        {
            if (debrief)
            {
                RefreshDebrief();
                return;
            }
            if (steps)
            {
                RefreshSteps();
                return;
            }
            WingEventRing events = c.Client ? null : c.Wing?.Events;
            RadioLog radio = RadioDirector.Instance?.Log;
            // Fill allocates while it describes events: only when something was logged, or the filter or the selection changed.
            long stamp = LogRows.Stamp(events, radio) * 31L + (logElement + 2) * 7L + c.Selection.Single * 3L + (logSelected ? 1L : 0L);
            for (int k = 0; k < logChips.Length; k++)
                logChips[k].Latched = k == 5 ? logSelected : k == 0 ? !logSelected && logElement < 0 : !logSelected && logElement == k - 1;
            Enable(logChips[5], c.Selection.Single != 0u);
            if (stamp == logStamp && logFilled) return;
            logStamp = stamp;
            logFilled = true;
            var filter = new LogFilter { Element = logElement, ById = logSelected, Id = c.Selection.Single, Rows = c.Rows, Count = c.Count };
            int n = LogRows.Fill(events, radio, logRows, LogRows.MaxRows, filter);
            logLines.Clear();
            for (int i = 0; i < n; i++)
            {
                LogRow r = logRows[i];
                string when = Clock(r.Time);
                // A radio line or a wing-level event has no one to centre on: no false click target (review P1 m3).
                AddLine(r.Radio ? when + " · RADIO" : when + " · " + LogRows.Who(r.Member), r.Text, null,
                    r.Radio ? AvState.Info : r.Member < 0 ? AvState.Caution : AvState.Ready, r.Id);
            }
            ShowLines(AvNum.Fixed(n, 0) + " LINES");
        }
    }
}
