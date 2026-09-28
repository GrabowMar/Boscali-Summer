using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>Faction tasking and live host controls, read-only for remote clients.</summary>
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
            string detail = card.Target + " · " + card.Status +
                            (string.IsNullOrEmpty(clock) ? "" : " · " + clock) +
                            "\n" + card.Reward;
            row.Set(card.Title, detail, AvNum.Percent(Mathf.Clamp01(card.Progress)), state);
        }

        private void RefreshTasking()
        {
            if (tasking == null) ModServices.TryGet(out tasking);
            // The board only paints from the last snapshot, so the SERVER page has to keep
            // asking even when no HUD or map layer is pulling snapshots on its own.
            if (tasking != null && Time.unscaledTime >= nextTaskingRefresh)
            {
                nextTaskingRefresh = Time.unscaledTime + TaskingRefreshSeconds;
                tasking.Refresh();
            }

            bool host = HostAuthority();
            if (taskRequest != null) taskRequest.Interactable = host && tasking != null;

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

        private void BuildHostSettingsPage(AvFlow flow, int page, HostSettingsPage kind, string title)
        {
            var views = new List<IHostSettingsView>();
            if (hostSettings != null)
            {
                for (int i = 0; i < hostSettings.Views.Count; i++)
                    if (hostSettings.Views[i].Page == kind) views.Add(hostSettings.Views[i]);
            }

            if (views.Count == 0)
            {
                flow.Section(kind == HostSettingsPage.Effects ? AvIcon.CloudRain : AvIcon.Settings, title, "NO MODULE CONTROLS");
                flow.Add(new NoteLine(flow.Content)).Set("No host settings are available.");
                return;
            }

            flow.Ticker.Add(page, AvTickRate.Slow, () =>
            {
                for (int i = 0; i < views.Count; i++) views[i].Refresh();
            });

            for (int v = 0; v < views.Count; v++)
            {
                IHostSettingsView view = views[v];
                flow.Section(kind == HostSettingsPage.Effects ? AvIcon.CloudRain : AvIcon.Settings, view.Section, null);

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
                ? "Host only. This is the host's value; only the host can change how the mission plays."
                : row.Reason ?? row.Help;
    }
}
