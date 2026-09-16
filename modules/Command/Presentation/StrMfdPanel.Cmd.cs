using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// CMD — the theater operations board. The player does not command units here; the
    /// board names the faction's main effort, funds the mission's own convoy groups from the
    /// shared faction pool, and reports what the vanilla rearm network is doing.
    ///
    /// <para>The AI follows the effort because the TheaterOps module biases the two query
    /// seams it already uses: where units with no better order head, and which depot or
    /// airbase delivers reinforcements. A remote client sees the host's effort and cannot
    /// set it; readiness is a local read on every peer.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        private const int CmdPriorityRows = 6;
        private const int CmdReinforceRows = 6;

        /// <summary>Section header, effort card, two notes, and the readiness rows.</summary>
        private const float CmdHeader = 22f;
        private const float CmdCard = 72f;
        private const float CmdNote = 20f;
        private const float CmdReadinessRows = 3f * 17f;

        private readonly ListRow[] cmdRows = new ListRow[CmdPriorityRows];
        private readonly ListRow[] cmdReinforceRows = new ListRow[CmdReinforceRows];
        private RectTransform cmdRoot;
        private TMP_Text cmdEffortLabel, cmdEffortDetail, cmdPriorityNote, cmdReinforceNote;
        private TMP_Text cmdAwaitingValue, cmdRearmValue, cmdDepletedValue, cmdReadinessNote;
        private Image cmdEffortRail;
        private AvButton cmdClear;

        private void ResetCmd()
        {
            cmdRoot = null;
            cmdEffortLabel = cmdEffortDetail = cmdPriorityNote = cmdReinforceNote = null;
            cmdAwaitingValue = cmdRearmValue = cmdDepletedValue = cmdReadinessNote = null;
            cmdEffortRail = null;
            cmdClear = null;
            Array.Clear(cmdRows, 0, cmdRows.Length);
            Array.Clear(cmdReinforceRows, 0, cmdReinforceRows.Length);
        }

        private void BuildCmdPage(GameObject page)
        {
            Rect view = shell.Body;
            float width = view.width - AvScreen.SpineInset;

            // Four bands exceed the reduced MFD body at lower canvas heights, so the page
            // scrolls only when it has to.
            float contentHeight =
                CmdHeader + CmdCard +
                CmdHeader + CmdNote + CmdPriorityRows * ListRow.Pitch +
                CmdHeader + CmdNote + CmdReinforceRows * ListRow.Pitch +
                CmdHeader + CmdReadinessRows + CmdNote + 12f;
            Rect body;
            cmdRoot = AvScreen.Scroll((RectTransform)page.transform, view, contentHeight, out body);

            float x = body.x + AvScreen.SpineInset;
            float y = body.y;
            AvStyled.Spine(cmdRoot, new Rect(body.x, body.y, 3f, body.height));

            // ---- MAIN EFFORT ---------------------------------------------------------------
            y = SectionHeader(cmdRoot, x, y, width, "MAIN EFFORT", "HOST AUTHORITY", band: false);

            var card = new Rect(x - 4f, y + 2f, width + 8f, 64f);
            (_, cmdEffortRail) = AvKit.TacticalCard(cmdRoot, card, AvTheme.RailInert);

            const float actionWidth = 110f;
            float textWidth = width - actionWidth - 14f;
            cmdEffortLabel = AvStyled.Label(
                cmdRoot, new Rect(x + 4f, y - 2f, textWidth, 18f), "NO MAIN EFFORT SET", "row-name");
            cmdEffortDetail = AvStyled.Label(
                cmdRoot, new Rect(x + 4f, y - 22f, textWidth, 38f), "", "row-sub");
            cmdClear = AvStyled.Button(
                cmdRoot, new Rect(card.x + card.width - actionWidth - 8f, y - 32f, actionWidth, 26f),
                "CLEAR EFFORT", "btn", ClearCmdPriority);
            y -= CmdCard;

            // ---- PRIORITY TARGETS ----------------------------------------------------------
            y = SectionHeader(cmdRoot, x, y, width, "PRIORITY TARGETS", "ACTIVE OBJECTIVES", band: true);
            cmdPriorityNote = AvStyled.Label(cmdRoot, new Rect(x, y, width, 16f), "", "row-sub");
            y -= CmdNote;
            for (int i = 0; i < cmdRows.Length; i++)
                cmdRows[i] = new ListRow(cmdRoot, x, y - i * ListRow.Pitch, width);
            y -= CmdPriorityRows * ListRow.Pitch;

            // ---- REINFORCE -----------------------------------------------------------------
            y = SectionHeader(cmdRoot, x, y, width, "REINFORCE", "FACTION POOL", band: true);
            cmdReinforceNote = AvStyled.Label(cmdRoot, new Rect(x, y, width, 16f), "", "row-sub");
            y -= CmdNote;
            for (int i = 0; i < cmdReinforceRows.Length; i++)
                cmdReinforceRows[i] = new ListRow(cmdRoot, x, y - i * ListRow.Pitch, width);
            y -= CmdReinforceRows * ListRow.Pitch;

            // ---- READINESS -----------------------------------------------------------------
            y = SectionHeader(cmdRoot, x, y, width, "READINESS", "REARM NETWORK", band: false);
            cmdAwaitingValue = KeyValue(cmdRoot, x, y, width, "UNITS AWAITING REARM");
            y -= 17f;
            cmdRearmValue = KeyValue(cmdRoot, x, y, width, "REARM ASSETS  READY / TRACKED");
            y -= 17f;
            cmdDepletedValue = KeyValue(cmdRoot, x, y, width, "DEPLETED REARM ASSETS");
            y -= 20f;
            cmdReadinessNote = AvStyled.Label(cmdRoot, new Rect(x, y, width, 16f), "", "row-sub");
        }

        private void RefreshCmd()
        {
            if (cmdEffortLabel == null) return;
            RefreshCmdEffort();
            RefreshCmdPriority();
            RefreshCmdReinforce();
            RefreshCmdReadiness();
        }

        private void RefreshCmdEffort()
        {
            ITheaterPriorityView view = theaterPriority;
            if (view == null || !view.Available)
            {
                cmdEffortLabel.text = "THEATER OPERATIONS NOT RUNNING";
                cmdEffortLabel.color = AvTheme.Dim;
                cmdEffortDetail.text =
                    "The theater operations module is disabled or failed to start. " +
                    "No directive is applied.";
                cmdEffortRail.color = AvTheme.RailInert;
                cmdClear.SetEnabled(false);
                return;
            }

            bool canCommand = view.CanCommand;
            bool has = view.HasPriority;

            cmdEffortLabel.text = has
                ? (view.PriorityLabel ?? "MAIN EFFORT").ToUpperInvariant()
                : "NO MAIN EFFORT SET";
            cmdEffortLabel.color = has ? AvTheme.Accent : AvTheme.Dim;
            cmdEffortDetail.text = has
                ? canCommand
                    ? "AI ground pushes and reinforcement delivery follow this objective until cleared."
                    : "The host's main effort. It directs friendly AI, not your aircraft."
                : canCommand
                    ? "Choose an active objective below. The faction pushes there and its reinforcements arrive there."
                    : "The host has not set a main effort.";
            cmdEffortRail.color = has ? AvTheme.RailReady : AvTheme.RailInert;
            cmdClear.SetEnabled(canCommand && has);
            cmdClear.WithTooltip(has
                ? "Clear the main effort. Units with no better order return to the nearest objective."
                : canCommand ? "No main effort is set." : "The host sets the main effort.");
        }

        private void RefreshCmdPriority()
        {
            ITheaterPriorityView view = theaterPriority;
            if (view == null || !view.Available)
            {
                cmdPriorityNote.text = "THEATER OPERATIONS NOT RUNNING";
                cmdPriorityNote.color = AvTheme.Dim;
                for (int i = 0; i < cmdRows.Length; i++) cmdRows[i].Hide();
                return;
            }

            bool canCommand = view.CanCommand;
            IReadOnlyList<TheaterPriorityOption> options = view.Options;
            int shown = 0;
            for (int i = 0; i < options.Count && shown < cmdRows.Length; i++)
            {
                TheaterPriorityOption option = options[i];
                if (option == null) continue;

                cmdRows[shown].Bind(
                    option.Selected ? "ready" : "info",
                    option.Label,
                    option.Detail,
                    option.Selected ? "PRIORITY" : "SET",
                    option.Selected ? 1f : 0f,
                    option.Selected ? AvTheme.Accent : AvTheme.Dim,
                    option.Selected ? AvTheme.Accent : AvTheme.RailInert,
                    canCommand ? (Action)(() => SetCmdPriority(option.Key)) : null,
                    option.Selected
                        ? canCommand
                            ? "The faction's current main effort."
                            : "The host's current main effort."
                        : canCommand
                            ? "Set the faction's main effort to this objective."
                            : "Read-only: the host sets the main effort.");
                shown++;
            }
            for (int i = shown; i < cmdRows.Length; i++) cmdRows[i].Hide();

            cmdPriorityNote.text = shown == 0
                ? "NO ACTIVE OBJECTIVES WITH A POSITION"
                : canCommand ? shown + " OBJECTIVES · CLICK TO SET" : "HOST-SET · READ ONLY";
            cmdPriorityNote.color = shown == 0 ? AvTheme.Dim : AvTheme.RailCaution;
        }

        private void RefreshCmdReinforce()
        {
            ITheaterLogisticsView view = theaterLogistics;
            if (view == null || !view.Available)
            {
                cmdReinforceNote.text = "THEATER OPERATIONS NOT RUNNING";
                cmdReinforceNote.color = AvTheme.Dim;
                for (int i = 0; i < cmdReinforceRows.Length; i++) cmdReinforceRows[i].Hide();
                return;
            }

            if (!view.CanCommand)
            {
                cmdReinforceNote.text = "HOST FUNDS REINFORCEMENTS";
                cmdReinforceNote.color = AvTheme.Dim;
                for (int i = 0; i < cmdReinforceRows.Length; i++) cmdReinforceRows[i].Hide();
                return;
            }

            float funds = view.FactionFunds;
            cmdReinforceNote.text = "POOL " + (float.IsNaN(funds) ? "—" : UnitConverter.ValueReading(funds)) +
                                    " · DELIVERED FROM THE DEPOT NEAREST THE EFFORT";
            cmdReinforceNote.color = AvTheme.Dim;

            IReadOnlyList<ReinforcementOption> options = view.Reinforcements;
            int shown = 0;
            for (int i = 0; i < options.Count && shown < cmdReinforceRows.Length; i++)
            {
                ReinforcementOption option = options[i];
                if (option == null) continue;

                string sub = option.Detail;
                if (!option.Affordable) sub += "  ·  INSUFFICIENT FUNDS";
                else if (!option.Ready) sub += "  ·  READY IN " + Mathf.CeilToInt(option.CooldownSeconds) + "s";

                cmdReinforceRows[shown].Bind(
                    !option.Affordable ? "locked" : option.Ready ? "ready" : "cooling",
                    option.Label,
                    sub,
                    UnitConverter.ValueReading(option.Cost),
                    0f,
                    option.Affordable ? AvTheme.TextPrimary : AvTheme.Disabled,
                    AvTheme.RailInert,
                    option.Ready ? (Action)(() => FundCmdReinforcement(option.Key)) : null,
                    option.Ready
                        ? "Fund " + option.Label + " from the faction pool."
                        : option.Affordable
                            ? "On cooldown until the previous delivery clears."
                            : "The faction pool cannot cover this call right now.");
                shown++;
            }
            for (int i = shown; i < cmdReinforceRows.Length; i++) cmdReinforceRows[i].Hide();

            if (shown == 0)
                cmdReinforceNote.text += "  ·  NO CONVOY GROUPS IN THIS MISSION";
        }

        private void RefreshCmdReadiness()
        {
            bool available = theaterLogistics != null && theaterLogistics.Available;
            ReadinessSummary readiness = available ? theaterLogistics.Readiness : default;
            bool observed = available && readiness.Observed;

            cmdAwaitingValue.text = observed ? readiness.UnitsAwaitingRearm.ToString() : "—";
            cmdRearmValue.text = observed ? readiness.RearmAvailable + " / " + readiness.RearmAssets : "—";
            cmdDepletedValue.text = observed ? readiness.RearmDepleted.ToString() : "—";
            cmdDepletedValue.color = observed && readiness.RearmDepleted > 0
                ? AvTheme.RailCaution
                : AvTheme.TextPrimary;

            cmdReadinessNote.text = !available
                ? "THEATER OPERATIONS NOT RUNNING"
                : observed ? "LOCAL READ OF THE VANILLA REARM NETWORK" : "REARM NETWORK UNAVAILABLE ON THIS PEER";
            cmdReadinessNote.color = observed ? AvTheme.Dim : AvTheme.RailCaution;
        }

        private void SetCmdPriority(string key)
        {
            if (theaterPriority == null || !theaterPriority.RequestPriority(key)) return;
            nextRefresh = 0f;
        }

        private void ClearCmdPriority()
        {
            if (theaterPriority == null || !theaterPriority.RequestClear()) return;
            nextRefresh = 0f;
        }

        private void FundCmdReinforcement(string key)
        {
            if (theaterLogistics == null || !theaterLogistics.RequestReinforcement(key)) return;
            nextRefresh = 0f;
        }
    }
}
