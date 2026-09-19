using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
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
    ///
    /// <para>The objective and reinforcement boards divide whatever height the rest of the
    /// page leaves. Neither is a fixed six rows: a short bay keeps what it can and scrolls,
    /// and a tall one widens the pitch so the last row meets the status line.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        private const int CmdPriorityRows = 6;
        private const int CmdReinforceRows = 6;

        /// <summary>The fewest rows a list keeps before the page would rather scroll.</summary>
        private const int CmdRowMinimum = 4;

        /// <summary>A taller bay spreads the two lists to this pitch at most.</summary>
        private const float CmdPitchMax = 52f;

        /// <summary>
        /// Everything the page holds around its two lists, in the order the build lays it
        /// out: the effort section, the two list headers with their notes, and the
        /// readiness block. Written as the sum of the same steps the layout uses.
        /// </summary>
        private const float CmdFixedHeight =
            24f + 68f +                // MAIN EFFORT: header, card
            24f + 16f +                // PRIORITY TARGETS: header, note
            24f + 16f +                // REINFORCE: header, note
            24f + 3f * KvPitch + 4f + 16f; // READINESS: header, three figures, note

        private readonly ListRow[] cmdRows = new ListRow[CmdPriorityRows];
        private readonly ListRow[] cmdReinforceRows = new ListRow[CmdReinforceRows];
        private RectTransform cmdRoot;
        private TMP_Text cmdEffortLabel, cmdEffortDetail, cmdPriorityNote, cmdReinforceNote;
        private TMP_Text cmdAwaitingValue, cmdRearmValue, cmdDepletedValue, cmdReadinessNote;
        private Image cmdEffortRail;
        private AvButton cmdClear;
        private int cmdBuiltPriority = CmdPriorityRows;
        private int cmdBuiltReinforce = CmdReinforceRows;

        private void ResetCmd()
        {
            cmdRoot = null;
            cmdEffortLabel = cmdEffortDetail = cmdPriorityNote = cmdReinforceNote = null;
            cmdAwaitingValue = cmdRearmValue = cmdDepletedValue = cmdReadinessNote = null;
            cmdEffortRail = null;
            cmdClear = null;
            Array.Clear(cmdRows, 0, cmdRows.Length);
            Array.Clear(cmdReinforceRows, 0, cmdReinforceRows.Length);
            cmdBuiltPriority = CmdPriorityRows;
            cmdBuiltReinforce = CmdReinforceRows;
        }

        private void BuildCmdPage(GameObject page)
        {
            Rect view = shell.Body;

            // The two boards divide the height the fixed blocks leave. The priority board
            // takes the odd row: it is the page's primary reading, and the note says when
            // either list had to drop rows rather than dropping them silently.
            float space = view.height - CmdFixedHeight;
            bool fits = space >= CmdRowMinimum * ListPitch;
            int rows = fits
                ? Mathf.Min(CmdPriorityRows + CmdReinforceRows,
                            Mathf.FloorToInt(space / ListPitch))
                : CmdPriorityRows + CmdReinforceRows;
            float pitch = fits
                ? Mathf.Clamp(space / rows, ListPitch, CmdPitchMax)
                : ListPitch;

            cmdBuiltPriority = Mathf.Clamp((rows + 1) / 2, 2, CmdPriorityRows);
            cmdBuiltReinforce = Mathf.Clamp(rows - cmdBuiltPriority, 2, CmdReinforceRows);

            float contentHeight = Mathf.Max(
                view.height, CmdFixedHeight + (cmdBuiltPriority + cmdBuiltReinforce) * pitch);
            Rect body;
            cmdRoot = AvScreen.Scroll((RectTransform)page.transform, view, contentHeight, out body);

            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;
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
            // Clearing is the one destructive action on the board, so it wears the danger
            // style: it recedes at rest and turns alert under the pointer. Painting it with
            // a solid theme plate would make the chrome the loudest thing on the page.
            cmdClear = AvStyled.Button(
                cmdRoot, new Rect(card.x + card.width - actionWidth - 8f, y - 32f, actionWidth, 26f),
                "CLEAR EFFORT", "btn", ClearCmdPriority, AvButtonStyle.Danger);
            y -= 68f;

            // ---- PRIORITY TARGETS ----------------------------------------------------------
            y = SectionHeader(cmdRoot, x, y, width, "PRIORITY TARGETS", "ACTIVE OBJECTIVES", band: true);
            cmdPriorityNote = AvStyled.Label(cmdRoot, new Rect(x, y, width, 14f), "", "row-sub");
            y -= 16f;
            for (int i = 0; i < cmdBuiltPriority; i++)
                cmdRows[i] = new ListRow(cmdRoot, x, y - i * pitch, width, pitch);
            y -= cmdBuiltPriority * pitch;

            // ---- REINFORCE -----------------------------------------------------------------
            y = SectionHeader(cmdRoot, x, y, width, "REINFORCE", "FACTION POOL", band: true);
            cmdReinforceNote = AvStyled.Label(cmdRoot, new Rect(x, y, width, 14f), "", "row-sub");
            y -= 16f;
            for (int i = 0; i < cmdBuiltReinforce; i++)
                cmdReinforceRows[i] = new ListRow(cmdRoot, x, y - i * pitch, width, pitch);
            y -= cmdBuiltReinforce * pitch;

            // ---- READINESS -----------------------------------------------------------------
            y = SectionHeader(cmdRoot, x, y, width, "READINESS", "REARM NETWORK", band: false);
            cmdAwaitingValue = KeyValue(cmdRoot, x, y, width, "UNITS AWAITING REARM");
            y -= KvPitch;
            cmdRearmValue = KeyValue(cmdRoot, x, y, width, "REARM ASSETS  READY / TRACKED");
            y -= KvPitch;
            cmdDepletedValue = KeyValue(cmdRoot, x, y, width, "DEPLETED REARM ASSETS");
            y -= KvPitch + 4f;
            cmdReadinessNote = AvStyled.Label(cmdRoot, new Rect(x, y, width, 14f), "", "row-sub");
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
                for (int i = 0; i < cmdBuiltPriority; i++) cmdRows[i].Hide();
                return;
            }

            bool canCommand = view.CanCommand;
            IReadOnlyList<TheaterPriorityOption> options = view.Options;
            int total = 0;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null) total++;

            int shown = 0;
            for (int i = 0; i < options.Count && shown < cmdBuiltPriority; i++)
            {
                TheaterPriorityOption option = options[i];
                if (option == null) continue;

                cmdRows[shown].Bind(
                    option.Selected ? "ready" : "info",
                    option.Label,
                    option.Detail,
                    option.Selected ? "PRIORITY" : "SET",
                    option.Selected,
                    option.Selected ? 1f : 0f,
                    option.Selected ? AvTheme.Accent : AvTheme.Dim,
                    option.Selected ? AvTheme.Accent : AvTheme.RailInert,
                    canCommand ? (Action)(() => SetCmdPriority(option.Key)) : null,
                    option.Selected
                        ? canCommand
                            ? "The faction's current main effort. Clear it to release the order."
                            : "The host's current main effort."
                        : canCommand
                            ? "Set the faction's main effort to this objective."
                            : "Read-only: the host sets the main effort.");
                shown++;
            }
            for (int i = shown; i < cmdBuiltPriority; i++) cmdRows[i].Hide();

            cmdPriorityNote.text = shown == 0
                ? "NO ACTIVE OBJECTIVES WITH A POSITION"
                : (total > shown ? "SHOWING " + shown + " OF " + total + " · " : shown + " OBJECTIVES · ") +
                  (canCommand ? "CLICK TO SET" : "READ ONLY");
            cmdPriorityNote.color = shown == 0 ? AvTheme.Dim : AvTheme.RailCaution;
        }

        private void RefreshCmdReinforce()
        {
            ITheaterLogisticsView view = theaterLogistics;
            if (view == null || !view.Available)
            {
                cmdReinforceNote.text = "THEATER OPERATIONS NOT RUNNING";
                cmdReinforceNote.color = AvTheme.Dim;
                for (int i = 0; i < cmdBuiltReinforce; i++) cmdReinforceRows[i].Hide();
                return;
            }

            if (!view.CanCommand)
            {
                cmdReinforceNote.text = "HOST FUNDS REINFORCEMENTS";
                cmdReinforceNote.color = AvTheme.Dim;
                for (int i = 0; i < cmdBuiltReinforce; i++) cmdReinforceRows[i].Hide();
                return;
            }

            float funds = view.FactionFunds;
            cmdReinforceNote.text = "POOL " + (float.IsNaN(funds) ? "—" : UnitConverter.ValueReading(funds)) +
                                    " · DELIVERED FROM THE DEPOT NEAREST THE EFFORT";
            cmdReinforceNote.color = AvTheme.Dim;

            IReadOnlyList<ReinforcementOption> options = view.Reinforcements;
            int total = 0;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null) total++;

            int shown = 0;
            for (int i = 0; i < options.Count && shown < cmdBuiltReinforce; i++)
            {
                ReinforcementOption option = options[i];
                if (option == null) continue;

                // The state leads the detail line: the row is one line tall, and a clipped
                // tail must never be the state that the rail's colour is carrying.
                string state = !option.Affordable ? "INSUFFICIENT FUNDS"
                             : !option.Ready ? "READY IN " + Mathf.CeilToInt(option.CooldownSeconds) + "s"
                             : null;
                string sub = string.IsNullOrEmpty(state)
                    ? option.Detail
                    : string.IsNullOrEmpty(option.Detail) ? state : state + " · " + option.Detail;

                // No track: a cost is a figure, not a share of anything, and a bar under it
                // would invite the reader to compare prices as if they were progress.
                cmdReinforceRows[shown].Bind(
                    !option.Affordable ? "locked" : option.Ready ? "ready" : "cooling",
                    option.Label,
                    sub,
                    UnitConverter.ValueReading(option.Cost),
                    false,
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
            for (int i = shown; i < cmdBuiltReinforce; i++) cmdReinforceRows[i].Hide();

            if (shown == 0)
                cmdReinforceNote.text += "  ·  NO CONVOY GROUPS IN THIS MISSION";
            else if (total > shown)
                cmdReinforceNote.text += "  ·  SHOWING " + shown + " OF " + total;
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
