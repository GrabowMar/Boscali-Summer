using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// The SET SERVER page: the host's side of the mod, moved here from the deleted ADM
    /// bezel. One scrolling page — the faction tasking board plus every host-authoritative
    /// setting published by an installed feature. A remote client sees the same page
    /// read-only, because the values that steer the mission should at least be legible to
    /// the people flying it.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        private const int TaskRowCount = 3;

        private ISecondaryObjectivesView tasking;
        private AvButton taskRequest;
        private TMP_Text taskNote;
        private readonly ListRow[] taskRows = new ListRow[TaskRowCount];

        private void BuildServerPage(RectTransform parent, Rect body)
        {
            int settingRows = 0;
            int sections = 1;
            if (hostSettings != null)
            {
                for (int i = 0; i < hostSettings.Views.Count; i++)
                {
                    sections++;
                    settingRows += hostSettings.Views[i].Rows.Count;
                }
            }

            parent = Page(ServerDisplay, parent, body,
                TaskRowCount + settingRows + 3, sections, out Rect area);

            // Providers re-read their config before the row widgets paint: values, bounds and
            // availability all come from the owning module, never from a copy kept here.
            if (hostSettings != null)
            {
                refreshers.Add(() =>
                {
                    for (int i = 0; i < hostSettings.Views.Count; i++) hostSettings.Views[i].Refresh();
                });
            }
            refreshers.Add(RefreshTasking);

            float x = area.x + AvScreen.SpineInset;
            float width = area.width - AvScreen.SpineInset;

            Heading(parent, ref area, "01", "FACTION TASKING", "SECONDARY OBJECTIVES");

            taskRequest = AvStyled.Button(parent, new Rect(x, area.y - 4f, 110f, 24f), "REQUEST BOARD", "btn",
                () => { tasking?.Refresh(); nextTick = 0f; });
            area.y -= 32f;

            taskNote = AvStyled.Label(parent, new Rect(x, area.y, width, 30f), "", "row-sub");
            area.y -= 36f;

            for (int i = 0; i < taskRows.Length; i++)
                taskRows[i] = new ListRow(parent, x, area.y - i * ListRow.Pitch, width);
            area.y -= TaskRowCount * ListRow.Pitch + 10f;

            if (hostSettings == null) return;

            int section = 2;
            for (int v = 0; v < hostSettings.Views.Count; v++)
            {
                IHostSettingsView view = hostSettings.Views[v];
                Heading(parent, ref area, section.ToString("00", CultureInfo.InvariantCulture), view.Section, null);

                for (int r = 0; r < view.Rows.Count; r++)
                {
                    HostSettingView row = view.Rows[r];
                    Rect rect = TakeRow(ref area);
                    if (row.Kind == HostSettingKind.Toggle)
                    {
                        Toggle(parent, rect, row.Label, row.Help,
                            () => row.Value, _ => view.Toggle(row.Id),
                            () => RowInteractive(row), () => RowReason(row));
                    }
                    else
                    {
                        Stepper(parent, rect, row.Label, () => row.ValueText,
                            d => view.Step(row.Id, d),
                            () => RowInteractive(row) && row.CanDecrease,
                            () => RowInteractive(row) && row.CanIncrease,
                            row.Help,
                            () => RowInteractive(row), () => RowReason(row), readOnlyValue: true);
                    }
                }
                section++;
            }
        }

        private static bool RowInteractive(HostSettingView row) => HostAuthority() && row.Interactive;

        private static string RowReason(HostSettingView row) =>
            !HostAuthority()
                ? "Host only. Only the server host can change how the mission plays."
                : row.Reason ?? row.Help;

        private void RefreshTasking()
        {
            if (tasking == null) ModServices.TryGet(out tasking);
            if (taskRequest != null)
            {
                bool host = HostAuthority();
                taskRequest.SetEnabled(host && tasking != null);
                taskRequest.WithTooltip(!host
                    ? "Host only. The host issues faction tasking."
                    : tasking == null
                        ? "Dynamic operations are not running on this host."
                        : "Ask the host for the current faction objective board. The board is " +
                          "issued by the host; this does not create work.");
            }
            if (taskNote == null) return;

            if (tasking == null)
            {
                taskNote.text = "Dynamic operations are not running on this host. " +
                                "Nothing is issuing faction tasking.";
                taskNote.color = AvTheme.Dim;
                for (int i = 0; i < taskRows.Length; i++) taskRows[i]?.Hide();
                return;
            }

            taskNote.text = tasking.Status ?? "";
            taskNote.color = AvTheme.Dim;

            IReadOnlyList<SecondaryObjectiveView> cards = tasking.Objectives;
            int count = cards == null ? 0 : cards.Count;

            for (int i = 0; i < taskRows.Length; i++)
            {
                if (i >= count)
                {
                    taskRows[i]?.Hide();
                    continue;
                }

                SecondaryObjectiveView card = cards[i];
                bool active = card.IsActive;

                string rail = card.IsComplete ? "ready" : active ? "armed" : "locked";
                Color tint = card.IsComplete ? AvTheme.RailReady
                           : active ? AvTheme.RailCaution
                           : AvTheme.Disabled;

                string detail = card.Target + " · " + card.Status +
                                (active ? " · " + Countdown(card.SecondsRemaining) : "") +
                                "\n" + card.Reward;

                taskRows[i].Bind(
                    rail,
                    card.Title,
                    detail,
                    Percent(card.Progress),
                    card.Progress,
                    tint,
                    tint);
            }
        }

        private static string Percent(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return "—";
            float clamped = ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
            return ((int)Math.Round(clamped * 100f, MidpointRounding.AwayFromZero))
                   .ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string Countdown(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "—";
            if (seconds <= 0f) return "EXPIRED";

            int total = (int)Math.Ceiling(seconds);
            if (total < 60) return "T-" + total + "s";

            int minutes = total / 60;
            int rest = total % 60;
            return "T-" + minutes + ":" + rest.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>One objective card: rail, title, detail, progress figure and track.</summary>
        private sealed class ListRow
        {
            public const float Pitch = 50f;

            private readonly GameObject root;
            private readonly Image rail;
            private readonly TMP_Text name;
            private readonly TMP_Text detail;
            private readonly TMP_Text value;
            private readonly Image bar;

            public ListRow(RectTransform parent, float x, float y, float width)
            {
                root = new GameObject("ListRow", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, Pitch - 4f));

                const float trail = 96f;
                float textWidth = width - trail - 20f;

                rail = AvStyled.Rail(rect, new Rect(0f, 0f, 3f, Pitch - 8f), "locked");
                name = AvStyled.Label(rect, new Rect(12f, 0f, textWidth, 15f), "", "row-name");
                detail = AvStyled.Label(rect, new Rect(12f, -16f, textWidth, 28f), "", "row-sub");
                value = AvStyled.Label(rect, new Rect(width - trail, 0f, trail, 15f), "",
                                       "row-value", align: TextAlignmentOptions.MidlineRight);
                bar = AvKit.ProgressBar(rect, new Rect(width - trail, -22f, trail, 6f), 0f,
                                        AvTheme.RailReady);

                AvKit.Rule(rect, new Rect(0f, -(Pitch - 8f), width, 1f),
                           AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.13f)));
                root.SetActive(false);
            }

            public void Bind(string railState, string title, string sub, string figure,
                             float fraction, Color figureColor, Color barColor)
            {
                rail.color = AvStyleHost.Resolve(
                    AvStyleHost.Style("rail " + railState).Background, AvTheme.RailInert);

                name.text = title ?? "";
                detail.text = sub ?? "";
                value.text = figure ?? "";
                value.color = figureColor;

                bar.color = barColor;
                bar.fillAmount = Mathf.Clamp01(fraction);

                if (!root.activeSelf) root.SetActive(true);
            }

            public void Hide()
            {
                if (root.activeSelf) root.SetActive(false);
            }
        }
    }
}
