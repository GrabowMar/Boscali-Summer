using NOAvionics;
using System.Collections.Generic;
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
    /// <summary>BEHAVIOUR › SORTIE, the events half (spec 2026-10-04 §4.4): the wing's events, its radio lines, your order results
    /// (<see cref="WingAcks.Feed"/>) and the steps that ran LATE or EARLY, merged by time, newest first, filtered by element or the
    /// selected aircraft; and the sortie's AFTER-ACTION REPORT in their place. A line with an aircraft opens it on INSPECT and centres
    /// the map on it. A pooled <see cref="AvList"/> that pages by <see cref="PageRows"/>.</summary>
    internal sealed partial class WmcPlan
    {
        private const int PageRows = 8, MaxEvents = LogRows.MaxRows + AckFeed.Capacity + WingPlan.Lanes * WingPlan.MaxSteps;
        private static readonly string[] LogChipLabels = { "ALL", "A", "B", "C", "D", "SELECTED" };
        private static readonly System.Comparison<SortieEvent> Newest = (a, b) => b.Time.CompareTo(a.Time);

        /// <summary>One line of the merged event list; <see cref="Tone"/> is <see cref="SortieWords.Tone"/> (2 danger, 1 caution).</summary>
        private struct SortieEvent
        {
            public float Time;
            public string Who, Text;
            public int Tone, Lane;
            public uint Id;
            public bool Radio;
        }

        /// <summary>What a list line shows and where a click goes (an aircraft id, or none).</summary>
        private struct LogLine
        {
            public string Name, Sub, Value;
            public AvState State;
            public uint Id;
        }

        private readonly List<LogRow> logRows = new List<LogRow>(LogRows.MaxRows);
        private readonly List<LogLine> logLines = new List<LogLine>(MaxEvents);
        private readonly List<SortieEvent> sortieEvents = new List<SortieEvent>(MaxEvents);
        private AvControl[] logChips;
        private AvControl debriefButton;
        private AvSection logSection;
        private AvList logList;
        private WmcLines logEmpty;
        private int logElement = -1;
        private bool logSelected;
        private bool debrief;

        /// <summary>LOG lines showing now (automation).</summary>
        public int LogRowsShown { get; private set; }

        private void BuildLog(AvFlow f, AvTicker t)
        {
            logSection = f.Section(AvIcon.Message2, "EVENTS", "");
            var chipSpecs = new AvControl.Spec[LogChipLabels.Length];
            for (int i = 0; i < chipSpecs.Length; i++)
            {
                int k = i;
                chipSpecs[i] = new AvControl.Spec(LogChipLabels[i], () => PickLogFilter(k));
            }
            logChips = f.Buttons(chipSpecs).Controls;
            string[] chipTips = { "Every element and aircraft.", "Element A's lines.", "Element B's lines.", "Element C's lines.", "Element D's lines.", "The selected aircraft's lines." };
            for (int i = 0; i < logChips.Length; i++)
            {
                logChips[i].Help = chipTips[i];
                ids.Add("plan.log.filter" + AvNum.Fixed(i, 0), logChips[i]);
            }
            logEmpty = f.Add(new WmcLines(f.Content, 1));
            logEmpty.Set(0, "Nothing logged yet.");
            logList = f.Add(new AvList(f.Content, t, PageRows, BindLog));
            logList.RowClicked = ClickLog;
            debriefButton = f.Buttons(new AvControl.Spec("AFTER-ACTION REPORT", ToggleDebrief, AvButtonStyle.Default, AvIcon.Flag)).Controls[0];
            debriefButton.Help = "This sortie in a few lines (kills, losses, tasks), then the last ones; press again for the events.";
            ids.Add("plan.log.debrief", debriefButton);
        }

        private void BindLog(int item, AvRow row)
        {
            if (item >= logLines.Count) return;
            LogLine l = logLines[item];
            row.Set(l.Name, l.Sub, l.Value, l.State);
            ids.Add("plan.log.row" + AvNum.Fixed(item, 0), row);
        }

        private void PickLogFilter(int chip)
        {
            logSelected = chip == 5;
            logElement = chip >= 1 && chip <= 4 ? chip - 1 : -1;
            debrief = false;
            debriefButton.Latched = false;
            Refreshed();
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

        private void ToggleDebrief()
        {
            debrief = !debrief;
            debriefButton.Latched = debrief;
            Refreshed();
        }

        /// <summary>Merges the wing's events and radio lines, your order results and the steps that ran late or early into
        /// <see cref="sortieEvents"/>, newest first, as the filter says.</summary>
        private void RebuildEvents(WmcContext c, WingPlan plan, PlanRunner r, bool live, WingService w, float now)
        {
            sortieEvents.Clear();
            WingEventRing events = c.Client ? null : c.Wing?.Events;
            RadioLog radio = RadioDirector.Instance?.Log;
            var filter = new LogFilter { Element = logElement, ById = logSelected, Id = c.Selection.Single, Rows = c.Rows, Count = c.Count };
            int n = LogRows.Fill(events, radio, logRows, LogRows.MaxRows, filter);
            for (int i = 0; i < n; i++)
            {
                LogRow row = logRows[i];
                sortieEvents.Add(new SortieEvent
                {
                    Time = row.Time, Who = row.Radio ? "RADIO" : LogRows.Who(row.Member), Text = row.Text, Id = row.Id, Radio = row.Radio,
                    Tone = SortieWords.Tone(row.Text), Lane = -1,
                });
            }
            // Your orders: the ack's clock is real time, the events' is the mission's.
            if (!logSelected)
                for (int i = 0; i < WingAcks.Feed.Count; i++)
                {
                    AckLine a = WingAcks.Feed.Newest(i);
                    if (logElement >= 0 && a.Who != ElementRoster.Letter(logElement)) continue;
                    float t = c.MissionTime - (Time.unscaledTime - a.Time);
                    sortieEvents.Add(new SortieEvent
                    {
                        Time = t, Who = a.Who, Text = a.Accepted ? "WILCO · " + (a.What ?? "") : "UNABLE · " + (string.IsNullOrEmpty(a.Reason) ? "REFUSED" : a.Reason.ToUpperInvariant()),
                        Tone = a.Accepted ? 0 : 2, Lane = -1,
                    });
                }
            // Steps that went out late or early: the STEPS table, as events.
            if (live && !logSelected)
                for (int l = 0; l < WingPlan.Lanes; l++)
                {
                    if (logElement >= 0 && l != logElement) continue;
                    for (int s = 0; s < plan.Steps[l].Count && sortieEvents.Count < MaxEvents; s++)
                    {
                        float actual = r.StartedAt(l, s);
                        string diff = SortieWords.Diff(drawnStart[l, s], actual);
                        if (diff.Length == 0 || diff == "ON TIME") continue;
                        sortieEvents.Add(new SortieEvent
                        {
                            Time = r.ExecutedAt + actual, Who = PlanRules.Name(l, s), Text = PlanWords.Kind(plan.Steps[l][s].Kind) + " " + diff,
                            Tone = diff.StartsWith("LATE", System.StringComparison.Ordinal) ? 1 : 0, Lane = l,
                        });
                    }
                }
            if (live && sortieEvents.Count < MaxEvents) AddExecute(r, plan);
            sortieEvents.Sort(Newest);
            logLines.Clear();
            for (int i = 0; i < sortieEvents.Count; i++)
            {
                SortieEvent e = sortieEvents[i];
                logLines.Add(new LogLine
                {
                    Name = e.Who + " · " + e.Text, Value = SortieWords.Stamp(e.Time, live, live ? r.ExecutedAt : 0f),
                    State = e.Radio ? AvState.Info : e.Tone == 2 ? AvState.Danger : e.Tone == 1 ? AvState.Caution : AvState.Ready, Id = e.Id,
                });
            }
        }

        /// <summary>The run's first line, at the foot of the list (it is the oldest).</summary>
        private void AddExecute(PlanRunner r, WingPlan plan)
        {
            int lanes = 0;
            for (int l = 0; l < WingPlan.Lanes; l++)
                if (plan.Steps[l].Count > 0) lanes++;
            if (logElement >= 0 || logSelected) return;
            sortieEvents.Add(new SortieEvent { Time = r.ExecutedAt, Who = "EXECUTE", Text = AvNum.Fixed(lanes, 0) + (lanes == 1 ? " LANE" : " LANES"), Lane = -1 });
        }

        /// <summary>Puts <see cref="logLines"/> on the list (the events, or the after-action report), with its caption and chips.</summary>
        private void RefreshEventList()
        {
            for (int k = 0; k < logChips.Length; k++)
                logChips[k].Latched = k == 5 ? logSelected : k == 0 ? !logSelected && logElement < 0 : !logSelected && logElement == k - 1;
            Enable(logChips[5], last != null && last.Selection.Single != 0u);
            if (debrief) RefreshDebrief();
            else ShowLines(AvNum.Fixed(logLines.Count, 0) + (logLines.Count == 1 ? " LINE" : " LINES"));
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

        /// <summary>AFTER-ACTION REPORT: this sortie's lines, then the first line of each kept one (spec WMC rebuild §PLAN DEBRIEF).</summary>
        private void RefreshDebrief()
        {
            DebriefService d = DebriefService.Instance;
            SortieLog s = d?.Sortie;
            logLines.Clear();
            if (s != null)
                foreach (string line in s.Lines())
                    if (logLines.Count < LogRows.MaxRows) AddLine(logLines.Count == 0 ? "THIS SORTIE" : "SORTIE", line, null, AvState.Ready, 0u);
            DebriefStore store = d?.Store;
            for (int i = 0; store != null && i < store.Count && logLines.Count < LogRows.MaxRows; i++)
                AddLine("EARLIER", store.Lines(i)[0], null, AvState.Info, 0u);
            ShowLines("AFTER-ACTION REPORT");
        }
    }
}
