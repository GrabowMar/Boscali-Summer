using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>SERVER pages: faction tasking and live host controls, read-only for remote clients.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        private const int TaskRowPool = 12;
        private const float TaskRowHeight = 58f;
        private const float TaskingRefreshSeconds = 2f;
        private ISecondaryObjectivesView tasking;
        private AvControl taskRequest;
        private NoteLine taskNote;
        private StrNodeBoard taskList;
        private StrNote taskFill;
        private IReadOnlyList<SecondaryObjectiveView> taskCards;
        private float nextTaskingRefresh;

        private void BuildTaskingPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.ListDetails, "FACTION TASKING", "SECONDARY OBJECTIVES");
            // The board's status line and the refresh button share one line.
            taskNote = new NoteLine(flow.Content);
            var refresh = new AvButtons(flow.Content, new[]
            {
                new AvControl.Spec("REFRESH BOARD", () =>
                {
                    tasking?.Refresh();
                    nextTaskingRefresh = Time.unscaledTime + TaskingRefreshSeconds;
                }, AvButtonStyle.Default, AvIcon.Refresh),
            });
            taskRequest = refresh.Controls[0];
            flow.Row(taskNote, refresh);

            // Give the available height to named contracts, with paging when the board exceeds it.
            taskList = flow.Add(new StrNodeBoard(flow.Content, flow.Ticker, TaskRowPool, BindTaskRow, TaskRowHeight), 1f);
            // The growing bottom element: a counts summary under the board, or the empty-state card
            // when there is nothing to list. Without it the page ends in dead panel.
            taskFill = flow.Add(new StrNote(flow.Content, AvIcon.ListDetails), 1f);

            flow.Ticker.Add(page, AvTickRate.Slow, RefreshTasking);
            RefreshTasking();
        }

        private void BindTaskRow(int index, AvRow row)
        {
            if (taskCards == null || index >= taskCards.Count)
            {
                row.Set("", "", "", AvState.Inert);
                return;
            }

            SecondaryObjectiveView card = taskCards[index];
            bool active = card.IsActive;
            AvState state = card.IsComplete ? AvState.Ready : active ? AvState.Caution : AvState.Inert;
            string clock = card.IsOffered || active ? MfdSecondaryObjectives.ChipLabel(card) : "";
            string detail = card.Target + " \u00b7 " + card.Status +
                            (string.IsNullOrEmpty(clock) ? "" : " \u00b7 " + clock) +
                            "\n" + card.Reward;
            row.Set(card.Title, detail, AvNum.Percent(Mathf.Clamp01(card.Progress)), state);
            row.Help = string.IsNullOrWhiteSpace(card.Description)
                ? "Faction contract issued by the host."
                : MfdSecondaryObjectives.PlainObjective(card.Description);
        }

        private void RefreshTasking()
        {
            if (tasking == null) ModuleServices.TryGet(out tasking);
            // The board only paints from the last snapshot, so the TASKING page has to keep
            // asking even when no HUD or map layer is pulling snapshots on its own.
            if (tasking != null && Time.unscaledTime >= nextTaskingRefresh)
            {
                nextTaskingRefresh = Time.unscaledTime + TaskingRefreshSeconds;
                tasking.Refresh();
            }

            bool host = HostAuthority();
            if (taskRequest != null)
            {
                taskRequest.Interactable = host && tasking != null;
                taskRequest.Help = !host
                    ? "Host only. The host issues faction tasking."
                    : tasking == null
                        ? "Dynamic operations are not running on this host."
                        : "Ask the host for the current faction objective board. The board is " +
                          "issued by the host; this does not create work.";
            }

            if (tasking == null)
            {
                taskNote?.Set("Dynamic operations are not running on this host. Nothing is issuing faction tasking.");
                taskCards = null;
                taskList?.SetCount(0);
                taskList?.SetShown(false);
                taskFill?.Set("NO TASKING SOURCE", "Dynamic operations are not running on this host. Faction contracts appear here when the host runs them.");
                taskFill?.SetShown(true);
                return;
            }

            taskNote?.Set(tasking.Status ?? "");
            taskCards = tasking.Objectives;
            int count = taskCards?.Count ?? 0;
            taskList?.SetCount(count);
            taskList?.SetShown(count > 0);
            PaintTaskFill(count);
        }

        /// <summary>The board's bottom card: a counts summary under contracts, or the empty-state
        /// card when the host has issued nothing. Always shown, so the page never ends in dead panel.</summary>
        private void PaintTaskFill(int count)
        {
            if (taskFill == null) return;
            if (count <= 0)
            {
                taskFill.Set("NO CONTRACTS ON THE BOARD", "The host has not issued faction tasking. Press REFRESH BOARD to pull the latest.");
                taskFill.SetShown(true);
                return;
            }
            int active = 0, offered = 0, complete = 0;
            if (taskCards != null)
                for (int i = 0; i < taskCards.Count; i++)
                {
                    SecondaryObjectiveView card = taskCards[i];
                    if (card.IsComplete) complete++;
                    else if (card.IsActive) active++;
                    else if (card.IsOffered) offered++;
                }
            string headline = count + (count == 1 ? " CONTRACT" : " CONTRACTS") + " ON THE BOARD";
            if (active > 0) headline += " \u00b7 " + active + " IN PROGRESS";
            if (offered > 0) headline += " \u00b7 " + offered + " OFFERED";
            if (complete > 0) headline += " \u00b7 " + complete + " COMPLETE";
            taskFill.Set(headline, "Issued by the host; progress updates as objectives resolve.");
            taskFill.SetShown(true);
        }

        /// <summary>Which SERVER tab lists a host-settings section. Effects come from the section's own page.</summary>
        private static int ServerGroupOf(IHostSettingsView view)
        {
            if (view.Page == HostSettingsPage.Effects) return SEffects;
            switch ((view.Section ?? "").ToUpperInvariant())
            {
                case "HIGH COMMAND":
                case "SUPPORT CALL-INS":
                case "SQUAD AND ACES":
                case "PROGRESSION":
                case "COMMS":
                    return SForces;
                default:
                    return SWorld; // new sections default to the general rules tab
            }
        }

        // Reading order inside a tab; sections a module adds later follow in registration order.
        private static readonly string[] SectionOrder =
        {
            "DYNAMIC OPERATIONS", "WORLD EVENTS", "TRENCHES", "URBAN COMBAT",
            "HIGH COMMAND", "SUPPORT CALL-INS", "SQUAD AND ACES", "PROGRESSION", "COMMS",
            "FIRE AND DESTRUCTION", "WEATHER"
        };

        private static AvIcon SectionIcon(string section)
        {
            switch ((section ?? "").ToUpperInvariant())
            {
                case "DYNAMIC OPERATIONS": return AvIcon.Target;
                case "WORLD EVENTS": return AvIcon.Flag;
                case "TRENCHES": return AvIcon.Ruler2;
                case "URBAN COMBAT": return AvIcon.Shield;
                case "HIGH COMMAND": return AvIcon.Crown;
                case "SUPPORT CALL-INS": return AvIcon.Radio;
                case "SQUAD AND ACES": return AvIcon.UsersGroup;
                case "PROGRESSION": return AvIcon.Star;
                case "COMMS": return AvIcon.Antenna;
                case "FIRE AND DESTRUCTION": return AvIcon.Flame;
                case "WEATHER": return AvIcon.CloudRain;
                default: return AvIcon.Settings;
            }
        }

        private static int SectionRank(string section)
        {
            int i = Array.IndexOf(SectionOrder, (section ?? "").ToUpperInvariant());
            return i < 0 ? int.MaxValue : i;
        }

        private void BuildServerGroupPage(AvFlow flow, int page, int group)
        {
            var views = new List<IHostSettingsView>();
            if (hostSettings != null)
            {
                for (int i = 0; i < hostSettings.Views.Count; i++)
                    if (ServerGroupOf(hostSettings.Views[i]) == group) views.Add(hostSettings.Views[i]);
            }

            // Stable by registration order among equals.
            for (int i = 1; i < views.Count; i++)
            {
                IHostSettingsView v = views[i];
                int j = i - 1;
                while (j >= 0 && SectionRank(views[j].Section) > SectionRank(v.Section)) { views[j + 1] = views[j]; j--; }
                views[j + 1] = v;
            }

            if (views.Count == 0)
            {
                flow.Section(group == SEffects ? AvIcon.CloudRain : group == SForces ? AvIcon.Shield : AvIcon.Flag,
                    group == SEffects ? "WORLD EFFECTS" : group == SForces ? "FORCES & ECONOMY" : "WORLD RULES", "NOT INSTALLED");
                flow.Add(new NoteLine(flow.Content)).Set("No settings are published here. The features that own them are not running in this session.");
                return;
            }

            // One line up top says how many of this tab's switches are on: the tab's state at a glance.
            var summary = flow.Add(new AvHazardBar(flow.Content, "SWITCHES"));
            summary.Help = "SWITCHES: how many of this tab's on/off settings are on right now. The dials and rows below tune the rest.";
            void RefreshSummary()
            {
                int total = 0, live = 0;
                for (int i = 0; i < views.Count; i++)
                    for (int r = 0; r < views[i].Rows.Count; r++)
                    {
                        HostSettingView row = views[i].Rows[r];
                        if (row.Kind != HostSettingKind.Toggle) continue;
                        total++;
                        if (row.Value) live++;
                    }
                summary.Set(total > 0 ? live / (float)total : 0f, total > 0 ? live + " OF " + total + " ON" : "NO SWITCHES",
                    total > 0 ? AvState.Info : AvState.Inert);
            }
            flow.Ticker.Add(page, AvTickRate.Slow, () =>
            {
                for (int i = 0; i < views.Count; i++) views[i].Refresh();
                RefreshSummary();
            });

            for (int v = 0; v < views.Count; v++)
            {
                IHostSettingsView view = views[v];
                AvSection section = flow.Section(SectionIcon(view.Section), view.Section, null);
                string count = AvNum.Fixed(view.Rows.Count, 0) + " SET";
                void Caption() => section.SetCaption(count + (HostAuthority() ? " \u00b7 LIVE" : " \u00b7 LOCKED, HOST ONLY"));
                Caption();
                flow.Ticker.Add(page, AvTickRate.Slow, Caption);

                // Toggles become a two-up grid of icon cells (name + LED + ON/OFF, sentence on hover), levels
                // that print a percentage become dials, and every other stepper keeps a +/- row.
                AvCellGrid grid = null;
                SetRingRow dials = null;
                int dialCount = 0;
                for (int r = 0; r < view.Rows.Count; r++)
                {
                    HostSettingView row = view.Rows[r];
                    if (row.Kind == HostSettingKind.Toggle)
                    {
                        grid = grid ?? flow.Grid(2);
                        dials = null;
                        ServerToggleCell(flow, grid, page, view, row);
                    }
                    else if (TryPercent(row.ValueText, out _))
                    {
                        grid = null;
                        if (dials == null || dialCount >= 3) { dials = flow.Add(new SetRingRow(flow.Content)); dialCount = 0; }
                        dialCount++;
                        ServerLevelRing(flow, dials, page, view, row);
                    }
                    else
                    {
                        grid = null;
                        dials = null;
                        Stepper(flow, page, row.Label, () => row.ValueText,
                            d => view.Step(row.Id, d),
                            () => RowInteractive(row) && row.CanDecrease,
                            () => RowInteractive(row) && row.CanIncrease,
                            row.Help, () => RowInteractive(row), () => RowReason(row), readOnlyValue: true);
                    }
                }
            }
            RefreshSummary();
            AddChangeLog(flow, page);
        }

        /// <summary>
        /// The tab's growing element: what has been changed on this screen this session, newest first, with its
        /// age. It is the audit trail the one-line footer echo cannot be, and it takes whatever height is left.
        /// </summary>
        private void AddChangeLog(AvFlow flow, int page)
        {
            AvSection section = flow.Section(AvIcon.Clock, "SESSION CHANGES", "NEWEST FIRST");
            var board = flow.Add(new StrLogBoard(flow.Content, ChangeLogSize, null, 2), 1f);
            var note = flow.Add(new StrNote(flow.Content, AvIcon.ListDetails), 1f);
            note.Set("NO CHANGES YET", "Anything changed on this screen this session is listed here with its age.");
            void Refresh()
            {
                board.Begin();
                for (int i = changeCount - 1; i >= 0; i--)
                    board.Add(TheaterReadout.Age(Time.unscaledTime - changeTime[i]).ToUpperInvariant(), changeText[i],
                        AvState.Info, false, "Changed this session, " + TheaterReadout.Age(Time.unscaledTime - changeTime[i]) + " ago.");
                board.End();
                board.SetShown(changeCount > 0);
                note.SetShown(changeCount == 0);
                section.SetCaption(changeCount == 0 ? "NONE YET" : changeCount + (changeCount == 1 ? " CHANGE" : " CHANGES"));
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }

        private void ServerToggleCell(AvFlow flow, AvCellGrid grid, int page, IHostSettingsView view, HostSettingView row)
        {
            AvCell cell = null;
            cell = grid.Toggle(row.Label, "", () => row.Value, v =>
            {
                if (!RowInteractive(row)) return;
                view.Toggle(row.Id);
                Echo(row.Label + " \u2014 " + (row.Value ? "ON" : "OFF"));
                Changed();
            });
            void Refresh()
            {
                cell.Interactable = RowInteractive(row);
                cell.Help = RowReason(row);
                cell.Refresh();
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }

        /// <summary>A host level whose printed value is a percentage (e.g. "60%"), as a dial with - / +.</summary>
        private void ServerLevelRing(AvFlow flow, SetRingRow dials, int page, IHostSettingsView view, HostSettingView row)
        {
            Ring(flow, dials, page, row.Label, () => row.ValueText,
                () => TryPercent(row.ValueText, out float v) ? Mathf.Clamp01(v) : 0f,
                d => view.Step(row.Id, d),
                () => RowInteractive(row) && row.CanDecrease, () => RowInteractive(row) && row.CanIncrease,
                row.Help, () => RowInteractive(row), () => RowReason(row));
        }

        private static bool TryPercent(string text, out float v01)
        {
            v01 = 0f;
            if (string.IsNullOrEmpty(text) || !text.EndsWith("%", StringComparison.Ordinal)) return false;
            if (!float.TryParse(text.Substring(0, text.Length - 1).Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float pct)) return false;
            v01 = pct / 100f;
            return true;
        }

        private static bool RowInteractive(HostSettingView row) => HostAuthority() && row.Interactive;

        private static string RowReason(HostSettingView row) =>
            !HostAuthority()
                ? "LOCKED \u00b7 " + row.Help
                : row.Reason ?? row.Help;
    }
}
