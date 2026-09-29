using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>SERVER pages: faction tasking and live host controls, read-only for remote clients.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        private const int TaskRowCount = 3;
        private const float TaskingRefreshSeconds = 2f;
        private ISecondaryObjectivesView tasking;
        private AvControl taskRequest;
        private NoteLine taskNote;
        private AvList taskList;
        private IReadOnlyList<SecondaryObjectiveView> taskCards;
        private float nextTaskingRefresh;

        private void BuildTaskingPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.ListDetails, "FACTION TASKING", "SECONDARY OBJECTIVES");
            taskRequest = flow.Buttons(new AvControl.Spec("REFRESH BOARD", () =>
            {
                tasking?.Refresh();
                nextTaskingRefresh = Time.unscaledTime + TaskingRefreshSeconds;
            })).Controls[0];
            taskNote = flow.Add(new NoteLine(flow.Content));
            taskList = flow.Add(new AvList(flow.Content, flow.Ticker, TaskRowCount, BindTaskRow));

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
        }

        private void RefreshTasking()
        {
            if (tasking == null) ModServices.TryGet(out tasking);
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
                return;
            }

            taskNote?.Set(tasking.Status ?? "");
            taskCards = tasking.Objectives;
            taskList?.SetCount(taskCards?.Count ?? 0);
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

            flow.Ticker.Add(page, AvTickRate.Slow, () =>
            {
                for (int i = 0; i < views.Count; i++) views[i].Refresh();
            });

            for (int v = 0; v < views.Count; v++)
            {
                IHostSettingsView view = views[v];
                AvSection section = flow.Section(SectionIcon(view.Section), view.Section, null);
                string count = AvNum.Fixed(view.Rows.Count, 0) + (view.Rows.Count == 1 ? " SETTING" : " SETTINGS");
                void Caption() => section.SetCaption(count + (HostAuthority() ? " \u00b7 LIVE" : " \u00b7 LOCKED, HOST ONLY"));
                Caption();
                flow.Ticker.Add(page, AvTickRate.Slow, Caption);

                for (int r = 0; r < view.Rows.Count; r++)
                {
                    HostSettingView row = view.Rows[r];
                    if (row.Kind == HostSettingKind.Toggle)
                        Toggle(flow, page, row.Label, row.Help,
                            () => row.Value, _ => view.Toggle(row.Id),
                            () => RowInteractive(row), () => RowReason(row));
                    else
                        Stepper(flow, page, row.Label, () => row.ValueText,
                            d => view.Step(row.Id, d),
                            () => RowInteractive(row) && row.CanDecrease,
                            () => RowInteractive(row) && row.CanIncrease,
                            row.Help, () => RowInteractive(row), () => RowReason(row), readOnlyValue: true);
                }
            }
        }

        private static bool RowInteractive(HostSettingView row) => HostAuthority() && row.Interactive;

        private static string RowReason(HostSettingView row) =>
            !HostAuthority()
                ? "LOCKED \u00b7 " + row.Help
                : row.Reason ?? row.Help;
    }
}
