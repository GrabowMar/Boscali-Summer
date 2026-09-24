using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// OPERATIONS — the theater war room. The director fights the faction's war: it opens
    /// offensives, names their objectives, funds their waves, holds the main effort and
    /// stands defenses. Players conduct through standing orders — stance, hold, chest and
    /// axes — and read the war back in the posture, the pushes, the main effort and the
    /// staff log. The board also funds the mission's own convoy groups by hand and reports
    /// the vanilla rearm network.
    ///
    /// <para>The board is drawn once and re-laid out, not rebuilt: the fixed blocks sit at the
    /// top, the two lists take the height that is left, and the lists re-flow only when the
    /// number of rows they can actually show changes. A reserved-but-empty bay was the old
    /// panel's largest defect — half a screen of nothing between two lists.</para>
    /// </summary>
    internal sealed partial class StrMfdPanel
    {
        private const int CmdOperationSlots = 2;

        private const int CmdAxisMaximum = OperationsBoardFit.TargetMaximum;
        private const int CmdReinforceMaximum = OperationsBoardFit.ReinforceMaximum;

        /// <summary>A taller bay spreads the rows to this pitch at most.</summary>
        private const float CmdPitchMax = 56f;

        private const float CmdCardHeight = 70f;
        private const float CmdCardGap = 6f;

        /// <summary>One standing-order row: key, value and its stepper.</summary>
        private const float CmdOrderRow = 24f;

        /// <summary>Staff log lines on show; the ring holds more than the page can spare.</summary>
        private const int CmdLogLines = 3;
        private const float CmdLogLine = 14f;

        /// <summary>The two card rows plus their gap and the offset the first one sits at.</summary>
        private const float CmdCardBlock = 2f + CmdOperationSlots * CmdCardHeight +
                                          (CmdOperationSlots - 1) * CmdCardGap;

        /// <summary>
        /// Everything the page holds around its two lists, in the order the layout pass walks:
        /// the director block with its effort and guard lines, the standing orders with their
        /// two rows, the operations section with its two card slots, the axes header, the
        /// staff log, the reinforcement header, and the readiness block.
        /// </summary>
        private const float CmdChromeHeight =
            HeaderStep + 2f * KvPitch +
            HeaderStep + 2f * CmdOrderRow +
            HeaderStep + CmdCardBlock +
            HeaderStep +
            HeaderStep + CmdLogLines * CmdLogLine + 4f +
            HeaderStep +
            HeaderStep + 2f * KvPitch + 16f;

        private readonly OperationCard[] cmdCards = new OperationCard[CmdOperationSlots];
        private readonly ListRow[] cmdAxisRows = new ListRow[CmdAxisMaximum];
        private readonly ListRow[] cmdReinforceRows = new ListRow[CmdReinforceMaximum];
        private readonly TMP_Text[] cmdLogLines = new TMP_Text[CmdLogLines];

        private RectTransform cmdRoot;
        private RectTransform cmdScroll;
        private Rect cmdBody;
        private Image cmdSpine;
        private Image cmdDecisionSurface;
        private Image cmdDecisionRail;
        private CmdHeader cmdDirectorHeader, cmdOrdersHeader, cmdOperationsHeader, cmdAxesHeader,
            cmdLogHeader, cmdReinforceHeader, cmdReadinessHeader;
        private CmdLine cmdEffortLine, cmdGuardLine;
        private CmdLine cmdAwaitingLine, cmdRearmLine;
        private TMP_Text cmdReadinessNote;

        private TMP_Text cmdStanceKey, cmdStanceValue;
        private AvButton cmdStanceDown, cmdStanceUp, cmdHold;
        private TMP_Text cmdEscrowKey, cmdEscrowValue, cmdReserveKey, cmdReserveValue;
        private AvButton cmdEscrowDown, cmdEscrowUp, cmdReserveDown, cmdReserveUp;

        private bool cmdFits;
        private float cmdSpace;
        private int cmdAxisCapacity;
        private int cmdReinforceCapacity;
        private int cmdShownAxes = -1;
        private int cmdShownReinforce = -1;
        private int cmdShownCards = -1;
        private StrPlanningWindow cmdPlanningWindow;

        private void ResetCmd()
        {
            if (cmdPlanningWindow != null) Destroy(cmdPlanningWindow.gameObject);
            cmdPlanningWindow = null;
            cmdRoot = null;
            cmdScroll = null;
            cmdBody = default;
            cmdSpine = null;
            cmdDecisionSurface = cmdDecisionRail = null;
            cmdDirectorHeader = cmdOrdersHeader = cmdOperationsHeader = cmdAxesHeader = null;
            cmdLogHeader = cmdReinforceHeader = cmdReadinessHeader = null;
            cmdEffortLine = cmdGuardLine = null;
            cmdAwaitingLine = cmdRearmLine = null;
            cmdReadinessNote = null;
            cmdStanceKey = cmdStanceValue = null;
            cmdStanceDown = cmdStanceUp = cmdHold = null;
            cmdEscrowKey = cmdEscrowValue = cmdReserveKey = cmdReserveValue = null;
            cmdEscrowDown = cmdEscrowUp = cmdReserveDown = cmdReserveUp = null;
            cmdFits = false;
            cmdSpace = 0f;
            cmdAxisCapacity = OperationsBoardFit.TargetMinimum;
            cmdReinforceCapacity = OperationsBoardFit.ReinforceMinimum;
            cmdShownAxes = cmdShownReinforce = -1;
            cmdShownCards = -1;
            Array.Clear(cmdCards, 0, cmdCards.Length);
            Array.Clear(cmdAxisRows, 0, cmdAxisRows.Length);
            Array.Clear(cmdReinforceRows, 0, cmdReinforceRows.Length);
            Array.Clear(cmdLogLines, 0, cmdLogLines.Length);
        }

        private void BuildCmdPage(GameObject page)
        {
            Rect view = shell.Body;
            ResolveCmdRows(view);

            // The build has no data yet, so it reserves the largest page this bay can ever
            // hold. The lists are laid out again on the first refresh, once the board knows
            // how many objectives and convoy groups it actually has.
            cmdShownAxes = cmdAxisCapacity;
            cmdShownReinforce = cmdReinforceCapacity;
            cmdShownCards = CmdOperationSlots;
            float contentHeight = CmdChromeHeight +
                                  (cmdShownAxes + cmdShownReinforce) * ListPitch;

            cmdRoot = AvScreen.Scroll((RectTransform)page.transform, view, contentHeight, out cmdBody);
            if (cmdRoot != (RectTransform)page.transform) cmdScroll = cmdRoot;

            float width = cmdBody.width - AvScreen.SpineInset;

            cmdSpine = AvStyled.Spine(cmdRoot, new Rect(cmdBody.x, cmdBody.y, 3f, cmdBody.height));

            cmdDirectorHeader = new CmdHeader(cmdRoot, "DIRECTOR");
            cmdDecisionSurface = AvKit.Panel(cmdRoot, new Rect(0f, 0f, width, 44f), AvTheme.Surface);
            cmdDecisionRail = AvKit.Rule(cmdRoot, new Rect(0f, 0f, 3f, 44f), AvTheme.RailInfo);
            cmdEffortLine = new CmdLine(cmdRoot, "MAIN EFFORT");
            cmdGuardLine = new CmdLine(cmdRoot, "GUARDING", () => OpenCmdPlanning(-1));

            cmdOrdersHeader = new CmdHeader(cmdRoot, "STANDING ORDERS");
            cmdStanceKey = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, 16f), "STANCE", "kv-key");
            cmdStanceValue = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, 16f), "—", "kv-value",
                                            align: TextAlignmentOptions.MidlineRight);
            cmdStanceDown = AvStyled.Button(cmdRoot, new Rect(0f, 0f, 28f, 20f), "−", "btn",
                                            () => StepCmdStance(-0.1f), AvButtonStyle.Default);
            cmdStanceUp = AvStyled.Button(cmdRoot, new Rect(0f, 0f, 28f, 20f), "+", "btn",
                                          () => StepCmdStance(0.1f), AvButtonStyle.Default);
            cmdHold = AvStyled.Button(cmdRoot, new Rect(0f, 0f, 0f, 20f), "HOLD", "btn",
                                      ToggleCmdHold, AvButtonStyle.Danger);
            cmdEscrowKey = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, 16f), "ESC ≤", "kv-key");
            cmdEscrowValue = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, 16f), "—", "kv-value",
                                            align: TextAlignmentOptions.MidlineRight);
            cmdEscrowDown = AvStyled.Button(cmdRoot, new Rect(0f, 0f, 26f, 20f), "−", "btn",
                                            () => StepCmdEscrow(-1f), AvButtonStyle.Default);
            cmdEscrowUp = AvStyled.Button(cmdRoot, new Rect(0f, 0f, 26f, 20f), "+", "btn",
                                          () => StepCmdEscrow(1f), AvButtonStyle.Default);
            cmdReserveKey = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, 16f), "RES ≥", "kv-key");
            cmdReserveValue = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, 16f), "—", "kv-value",
                                             align: TextAlignmentOptions.MidlineRight);
            cmdReserveDown = AvStyled.Button(cmdRoot, new Rect(0f, 0f, 26f, 20f), "−", "btn",
                                             () => StepCmdReserve(-1f), AvButtonStyle.Default);
            cmdReserveUp = AvStyled.Button(cmdRoot, new Rect(0f, 0f, 26f, 20f), "+", "btn",
                                           () => StepCmdReserve(1f), AvButtonStyle.Default);

            cmdOperationsHeader = new CmdHeader(cmdRoot, "OPERATIONS", () => OpenCmdPlanning(0));
            for (int i = 0; i < cmdCards.Length; i++)
            {
                int slot = i;
                cmdCards[i] = new OperationCard(cmdRoot, width, () => OpenCmdPlanning(slot));
            }

            cmdAxesHeader = new CmdHeader(cmdRoot, "AXES");
            for (int i = 0; i < cmdAxisRows.Length; i++)
                cmdAxisRows[i] = new ListRow(cmdRoot, 0f, 0f, width, ListPitch);
            for (int i = 0; i < cmdReinforceRows.Length; i++)
                cmdReinforceRows[i] = new ListRow(cmdRoot, 0f, 0f, width, ListPitch);

            cmdLogHeader = new CmdHeader(cmdRoot, "STAFF LOG");
            for (int i = 0; i < cmdLogLines.Length; i++)
                cmdLogLines[i] = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, CmdLogLine), "", "row-sub");

            cmdReinforceHeader = new CmdHeader(cmdRoot, "REINFORCE");
            cmdReadinessHeader = new CmdHeader(cmdRoot, "READINESS");
            cmdAwaitingLine = new CmdLine(cmdRoot, "UNITS AWAITING REARM");
            cmdRearmLine = new CmdLine(cmdRoot, "REARM ASSETS  READY / TRACKED");
            cmdReadinessNote = AvStyled.Label(cmdRoot, new Rect(0f, 0f, 0f, 14f), "", "row-sub");

            ReflowCmd(cmdShownAxes, cmdShownReinforce, cmdShownCards);
        }

        /// <summary>Solves how many list rows this bay holds and how they divide between the lists.</summary>
        private void ResolveCmdRows(Rect view)
        {
            cmdSpace = Mathf.Max(0f, view.height - CmdChromeHeight);
            cmdFits = OperationsBoardFit.Fits(cmdSpace, ListPitch);

            int rows = OperationsBoardFit.Rows(cmdSpace, ListPitch, cmdFits);
            cmdAxisCapacity = OperationsBoardFit.Targets(rows);
            cmdReinforceCapacity = OperationsBoardFit.Reinforce(rows, cmdAxisCapacity);
        }

        /// <summary>
        /// Lays the page out top to bottom for the number of rows that are actually on the
        /// board. Called at build and whenever that number changes; a refresh that changes
        /// nothing walks no rects.
        /// </summary>
        private void ReflowCmd(int axes, int reinforce, int cards)
        {
            if (cmdRoot == null) return;

            int total = Mathf.Max(1, axes + reinforce);
            float pitch = cmdFits
                ? OperationsBoardFit.Pitch(cmdSpace, total, ListPitch, CmdPitchMax)
                : ListPitch;

            float x = cmdBody.x + AvScreen.SpineInset;
            float width = cmdBody.width - AvScreen.SpineInset;
            float y = cmdBody.y;

            y = cmdDirectorHeader.Place(x, y, width);
            AvKit.Place(cmdDecisionSurface.rectTransform, new Rect(x, y + 2f, width, 44f));
            AvKit.Place(cmdDecisionRail.rectTransform, new Rect(x, y + 2f, 3f, 44f));
            cmdEffortLine.Place(x + 10f, y, width - 20f);
            y -= KvPitch;
            cmdGuardLine.Place(x + 10f, y, width - 20f);
            y -= KvPitch;

            y = cmdOrdersHeader.Place(x, y, width);
            y = PlaceCmdStanceRow(x, y, width);
            y = PlaceCmdChestRow(x, y, width);

            y = cmdOperationsHeader.Place(x, y, width);
            float cardsTop = y - 2f;
            for (int i = 0; i < cmdCards.Length; i++)
                cmdCards[i].Place(x, cardsTop - i * (CmdCardHeight + CmdCardGap), width);
            y = cardsTop - (cards * CmdCardHeight + (cards - 1) * CmdCardGap);

            y = cmdAxesHeader.Place(x, y, width);
            for (int i = 0; i < cmdAxisRows.Length; i++)
            {
                if (i < axes) cmdAxisRows[i].Place(x, y - i * pitch, pitch);
                else cmdAxisRows[i].Hide();
            }
            y -= axes * pitch;

            y = cmdLogHeader.Place(x, y, width);
            for (int i = 0; i < cmdLogLines.Length; i++)
            {
                AvKit.Place(cmdLogLines[i].rectTransform,
                            new Rect(x, y - i * CmdLogLine - 2f, width, CmdLogLine));
            }
            y -= CmdLogLines * CmdLogLine + 4f;

            y = cmdReinforceHeader.Place(x, y, width);
            for (int i = 0; i < cmdReinforceRows.Length; i++)
            {
                if (i < reinforce) cmdReinforceRows[i].Place(x, y - i * pitch, pitch);
                else cmdReinforceRows[i].Hide();
            }
            y -= reinforce * pitch;

            y = cmdReadinessHeader.Place(x, y, width);
            cmdAwaitingLine.Place(x, y, width);
            y -= KvPitch;
            cmdRearmLine.Place(x, y, width);
            y -= KvPitch;
            AvKit.Place(cmdReadinessNote.rectTransform, new Rect(x, y - 2f, width, 14f));
            y -= 16f;

            float used = cmdBody.y - y + 6f;
            float contentHeight = Mathf.Max(cmdBody.height, used);
            AvKit.Place(cmdSpine.rectTransform, new Rect(cmdBody.x, cmdBody.y, 3f, contentHeight));
            if (cmdScroll != null)
                cmdScroll.sizeDelta = new Vector2(cmdScroll.sizeDelta.x, contentHeight);

            cmdShownAxes = axes;
            cmdShownReinforce = reinforce;
            cmdShownCards = cards;
        }

        private float PlaceCmdStanceRow(float x, float y, float width)
        {
            const float step = 28f;
            const float gap = 4f;
            float holdWidth = Mathf.Max(64f, width * 0.22f);
            holdWidth = Mathf.Min(holdWidth, width * 0.3f);
            float valueWidth = width * 0.24f;
            float keyWidth = width - valueWidth - step * 2f - gap * 2f - holdWidth - gap;
            float cursor = x;
            AvKit.Place(cmdStanceKey.rectTransform, new Rect(cursor, y, keyWidth, 16f));
            cursor += keyWidth;
            AvKit.Place(cmdStanceValue.rectTransform, new Rect(cursor, y, valueWidth, 16f));
            cursor += valueWidth + gap;
            AvKit.Place(cmdStanceDown.transform as RectTransform, new Rect(cursor, y - 2f, step, 20f));
            cursor += step + gap;
            AvKit.Place(cmdStanceUp.transform as RectTransform, new Rect(cursor, y - 2f, step, 20f));
            cursor += step + gap;
            AvKit.Place(cmdHold.transform as RectTransform, new Rect(cursor, y - 2f, width - (cursor - x), 20f));
            return y - CmdOrderRow;
        }

        private float PlaceCmdChestRow(float x, float y, float width)
        {
            const float step = 26f;
            const float gap = 4f;
            float half = (width - gap) / 2f;
            float keyWidth = 52f;
            float valueWidth = half - keyWidth - step * 2f - gap * 2f;
            float cursor = x;
            AvKit.Place(cmdEscrowKey.rectTransform, new Rect(cursor, y, keyWidth, 16f));
            cursor += keyWidth;
            AvKit.Place(cmdEscrowValue.rectTransform, new Rect(cursor, y, valueWidth, 16f));
            cursor += valueWidth + gap;
            AvKit.Place(cmdEscrowDown.transform as RectTransform, new Rect(cursor, y - 2f, step, 20f));
            cursor += step + gap;
            AvKit.Place(cmdEscrowUp.transform as RectTransform, new Rect(cursor, y - 2f, step, 20f));
            cursor = x + half + gap;
            AvKit.Place(cmdReserveKey.rectTransform, new Rect(cursor, y, keyWidth, 16f));
            cursor += keyWidth;
            AvKit.Place(cmdReserveValue.rectTransform, new Rect(cursor, y, valueWidth, 16f));
            cursor += valueWidth + gap;
            AvKit.Place(cmdReserveDown.transform as RectTransform, new Rect(cursor, y - 2f, step, 20f));
            cursor += step + gap;
            AvKit.Place(cmdReserveUp.transform as RectTransform, new Rect(cursor, y - 2f, step, 20f));
            return y - CmdOrderRow;
        }

        private void RefreshCmd()
        {
            if (cmdRoot == null) return;
            RefreshCmdDirector();
            RefreshCmdOrders();
            int cards = RefreshCmdOperations();
            int axes = RefreshCmdAxes();
            RefreshCmdLog();
            int reinforce = RefreshCmdReinforce();
            RefreshCmdReadiness();

            // One re-flow per refresh at most, and only when the lists changed length.
            if (axes != cmdShownAxes || reinforce != cmdShownReinforce || cards != cmdShownCards)
                ReflowCmd(axes, reinforce, cards);
        }

        private void OpenCmdPlanning(int selection)
        {
            if (cmdPlanningWindow == null)
                cmdPlanningWindow = StrPlanningWindow.Create(
                    theaterOperations, theaterPriority, theaterLogistics, highCommand, activeEvents,
                    strikePicture);
            cmdPlanningWindow.Show(selection);
        }

        // ---- Director -------------------------------------------------------------------------

        private static string CmdPostureWord(TheaterDirectorPosture posture)
        {
            switch (posture)
            {
                case TheaterDirectorPosture.Attacking: return "ATTACKING";
                case TheaterDirectorPosture.Defending: return "DEFENDING";
                case TheaterDirectorPosture.Holding: return "HOLDING";
                default: return "IDLE";
            }
        }

        private void RefreshCmdDirector()
        {
            ITheaterOperationsView ops = theaterOperations;
            ITheaterPriorityView pri = theaterPriority;
            bool available = ops != null && ops.Available;
            TheaterDirectionView direction = available ? ops.Direction : null;

            cmdDirectorHeader.Note.text = !available
                ? "THEATER DIRECTOR OFF"
                : direction == null ? "STAFF QUIET"
                : CmdPostureWord(direction.Posture) +
                  (direction.ActivePlans > 0 ? " · " + direction.ActivePlans +
                   (direction.ActivePlans == 1 ? " PUSH" : " PUSHES") : "");
            cmdDirectorHeader.Note.color = available ? AvTheme.Dim : AvTheme.RailCaution;

            bool hasEffort = available && pri != null && pri.Available && pri.HasPriority;
            string effort = !hasEffort ? "— (STAFF IDLE)"
                : (pri.PriorityLabel ?? "MAIN EFFORT").ToUpperInvariant() +
                  (direction == null ? ""
                   : direction.EffortIsDefense ? " · DEFENSE" : " · OFFENSE");
            cmdEffortLine.Set(effort, hasEffort ? AvTheme.Accent : AvTheme.Disabled);

            string guard = direction != null && !string.IsNullOrEmpty(direction.DefenseLabel)
                ? direction.DefenseLabel.ToUpperInvariant() : "—";
            cmdGuardLine.Set(guard == "—" ? guard : guard + "  >",
                guard == "—" ? AvTheme.Disabled : AvTheme.RailCaution);
            cmdGuardLine.SetEnabled(guard != "—");
        }

        // ---- Standing orders ------------------------------------------------------------------

        private void RefreshCmdOrders()
        {
            ITheaterOperationsView view = theaterOperations;
            bool available = view != null && view.Available;
            bool canCommand = available && view.CanCommand;
            TheaterInfluenceView influence = available ? view.Influence : null;

            cmdOrdersHeader.Note.text = influence == null || string.IsNullOrEmpty(influence.Setter)
                ? "STAFF DEFAULT" : "SET BY " + influence.Setter.ToUpperInvariant();
            cmdOrdersHeader.Note.color = AvTheme.Dim;

            cmdStanceValue.text = influence != null
                ? (int)Math.Round(influence.Stance * 100f) + "% ATTACK" : "—";
            cmdStanceValue.color = canCommand ? AvTheme.TextPrimary : AvTheme.Disabled;
            bool held = influence != null && influence.HoldOffense;
            cmdHold.SetText(held ? "HELD" : "HOLD");
            cmdHold.SetLatched(held);

            cmdEscrowValue.text = influence != null
                ? UnitConverter.ValueReading(influence.MaxEscrowPerPlan) : "—";
            cmdEscrowValue.color = canCommand ? AvTheme.TextPrimary : AvTheme.Disabled;
            cmdReserveValue.text = influence != null
                ? UnitConverter.ValueReading(influence.ReserveFloor) : "—";
            cmdReserveValue.color = canCommand ? AvTheme.TextPrimary : AvTheme.Disabled;

            string off = "The theater director is not running.";
            cmdStanceDown.SetEnabled(canCommand);
            cmdStanceDown.WithTooltip(canCommand
                ? "Lean the staff toward defense. The director still scores every objective."
                : off);
            cmdStanceUp.SetEnabled(canCommand);
            cmdStanceUp.WithTooltip(canCommand
                ? "Lean the staff toward attack. The director still guards held ground."
                : off);
            cmdHold.SetEnabled(canCommand);
            cmdHold.WithTooltip(canCommand
                ? held ? "Release the hold. The staff opens offensives again."
                       : "Hold new offensives. Defenses still run; unlaunched plans stand down."
                : off);
            string escrowTip = canCommand ? "Cap what one offensive may escrow, in millions." : off;
            string reserveTip = canCommand ? "Floor the faction pool will not be spent below." : off;
            cmdEscrowDown.SetEnabled(canCommand);
            cmdEscrowUp.SetEnabled(canCommand);
            cmdEscrowDown.WithTooltip(escrowTip);
            cmdEscrowUp.WithTooltip(escrowTip);
            cmdReserveDown.SetEnabled(canCommand);
            cmdReserveUp.SetEnabled(canCommand);
            cmdReserveDown.WithTooltip(reserveTip);
            cmdReserveUp.WithTooltip(reserveTip);
        }

        private void StepCmdStance(float delta)
        {
            if (theaterOperations == null || theaterOperations.Influence == null) return;
            float stance = theaterOperations.Influence.Stance + delta;
            if (theaterOperations.RequestStance(stance)) nextRefresh = 0f;
        }

        private void ToggleCmdHold()
        {
            if (theaterOperations == null || theaterOperations.Influence == null) return;
            if (theaterOperations.RequestHold(!theaterOperations.Influence.HoldOffense)) nextRefresh = 0f;
        }

        private void StepCmdEscrow(float waves)
        {
            if (theaterOperations == null || theaterOperations.Influence == null) return;
            float step = waves * theaterOperations.WaveBudget;
            float escrow = theaterOperations.Influence.MaxEscrowPerPlan + step;
            if (theaterOperations.RequestChest(escrow, theaterOperations.Influence.ReserveFloor))
                nextRefresh = 0f;
        }

        private void StepCmdReserve(float waves)
        {
            if (theaterOperations == null || theaterOperations.Influence == null) return;
            float step = waves * theaterOperations.WaveBudget;
            float reserve = theaterOperations.Influence.ReserveFloor + step;
            if (theaterOperations.RequestChest(theaterOperations.Influence.MaxEscrowPerPlan, reserve))
                nextRefresh = 0f;
        }

        // ---- Operations ---------------------------------------------------------------------

        private int RefreshCmdOperations()
        {
            ITheaterOperationsView view = theaterOperations;
            bool available = view != null && view.Available;

            if (!available)
            {
                cmdOperationsHeader.Note.text = "OFFENSIVE PLANNER OFF";
                cmdOperationsHeader.Note.color = AvTheme.RailCaution;
                cmdCards[0].ShowCard();
                cmdCards[0].Show("locked", "OFFENSIVE PLANNER NOT RUNNING", "",
                                 "The theater operations module is disabled or failed to start.");
                cmdCards[0].SetTimeline(TheaterOperationPhase.Mustering, AvTheme.RailInert, true);
                cmdCards[0].SetSide("NO PUSHES CAN RUN ON THIS PEER", true);
                cmdCards[0].SetPrimary(null, false, null, null);
                cmdCards[0].SetSecondary(null, false, null, null);
                for (int i = 1; i < cmdCards.Length; i++) cmdCards[i].Hide();
                return 1;
            }

            IReadOnlyList<TheaterOperationView> operations = view.Operations;
            int count = 0;
            for (int i = 0; i < operations.Count && count < CmdOperationSlots; i++)
            {
                if (operations[i] == null) continue;
                BindCmdOperation(operations[i], count);
                count++;
            }

            if (count < CmdOperationSlots)
                BindCmdIdle(cmdCards[count++]);
            for (int i = count; i < cmdCards.Length; i++) cmdCards[i].Hide();

            cmdOperationsHeader.Note.text = count == 1 && operations.Count == 0 ? "OPEN BRIEFING >"
                : operations.Count + (operations.Count == 1 ? " PUSH" : " PUSHES") + "  ·  OPEN >";
            cmdOperationsHeader.Note.color = AvTheme.Dim;
            return count;
        }

        private void BindCmdIdle(OperationCard card)
        {
            card.ShowCard();
            card.Show("info", "NO PUSH UNDER WAY", "IDLE", "THE STAFF REVIEWS THE THEATER");
            card.SetTimeline(TheaterOperationPhase.Mustering, AvTheme.RailInert, true);
            card.SetSide("STANCE, HOLD, CHEST AND AXES STEER WHAT OPENS NEXT", true);
            card.SetPrimary(null, false, null, null);
            card.SetSecondary(null, false, null, null);
        }

        private void BindCmdOperation(TheaterOperationView operation, int slot)
        {
            if (slot < 0 || slot >= cmdCards.Length) return;
            OperationCard card = cmdCards[slot];
            if (card == null) return;

            bool concluded = operation.Phase == TheaterOperationPhase.Concluded;
            card.ShowCard();
            card.Show(
                TheaterReadout.OffensiveRail(operation.Phase, operation.Outcome),
                "OPERATION " + operation.Name,
                TheaterReadout.OffensivePhaseWord(operation.Phase, operation.Outcome),
                CmdOperationDetail(operation),
                operation.IsHeld);
            card.SetTimeline(
                operation.Phase,
                CmdOperationBarColour(operation.Phase, operation.Outcome),
                concluded);
            card.SetSide(concluded ? CmdOperationReport(operation) : null, concluded);

            // The board reports the push; the staff owns it. No control ever lands here.
            card.SetPrimary(null, false, null, null);
            card.SetSecondary(null, false, null, null);
        }

        private static Color CmdOperationBarColour(
            TheaterOperationPhase phase, TheaterOperationOutcome outcome)
        {
            AvStyle style = AvStyleHost.Style(
                "rail " + TheaterReadout.OffensiveRail(phase, outcome));
            return AvStyleHost.Resolve(style.Background, AvTheme.RailInert);
        }

        private string CmdOperationDetail(TheaterOperationView operation)
        {
            string target = string.IsNullOrEmpty(operation.TargetLabel)
                ? "—"
                : Terse(operation.TargetLabel.ToUpperInvariant(), 24);
            string waves = operation.WavesPlanned + (operation.WavesPlanned == 1 ? " WAVE" : " WAVES");

            switch (operation.Phase)
            {
                case TheaterOperationPhase.Mustering:
                    return "GATHERING FORCES · " + waves + " FUNDED · " +
                           UnitConverter.ValueReading(operation.Budget) + " ESCROWED";
                case TheaterOperationPhase.Planning:
                    return "STAFF PLANNING THE PUSH · " + waves + " FUNDED · " +
                           UnitConverter.ValueReading(operation.Budget) + " ESCROWED";
                case TheaterOperationPhase.AwaitingTarget:
                    return "PLAN READY · " + waves + " FUNDED · STAFF NAMING THE TARGET";
                case TheaterOperationPhase.Launching:
                    return operation.IsHeld
                        ? "H-HOUR HELD — " + Terse(operation.Holder.ToUpperInvariant(), 18) +
                          " HAS THE MAIN EFFORT"
                        : "H-HOUR IN " + Mathf.Max(0, Mathf.CeilToInt(operation.Countdown)) +
                          "s · TARGET " + target;
                case TheaterOperationPhase.Assault:
                    return "WAVE " + operation.WavesLaunched + " OF " + operation.WavesPlanned +
                           " ON THE ROAD · " + target + " · " +
                           TheaterReadout.Clock(operation.ElapsedSeconds);
                case TheaterOperationPhase.Holding:
                    return "ALL " + operation.WavesPlanned + " WAVES ON THE ROAD · HOLDING " +
                           (operation.HoldRemaining < 0f ? "—" : TheaterReadout.Clock(operation.HoldRemaining)) +
                           " FOR REINFORCEMENT";
                default:
                    return "TARGET " + target;
            }
        }

        private static string CmdOperationReport(TheaterOperationView operation)
        {
            string report = TheaterReadout.OffensiveOutcomeWord(operation.Outcome) + " · " +
                            operation.WavesLaunched + " OF " + operation.WavesPlanned + " WAVES · " +
                            UnitConverter.ValueReading(operation.Spent) + " SPENT";
            if (operation.ElapsedSeconds > 0f)
                report += " · " + TheaterReadout.Clock(operation.ElapsedSeconds);
            if (operation.Returned > 0f)
                report += " · " + UnitConverter.ValueReading(operation.Returned) + " RETURNED";
            return report;
        }

        private static string Terse(string value, int limit) =>
            string.IsNullOrEmpty(value) || value.Length <= limit
                ? value
                : value.Substring(0, limit - 1) + "…";

        // ---- Axes -------------------------------------------------------------------------------

        /// <summary>Binds the axes list from the live objectives; returns the visible row count.</summary>
        private int RefreshCmdAxes()
        {
            ITheaterPriorityView view = theaterPriority;
            if (view == null || !view.Available)
            {
                cmdAxesHeader.Note.text = "THEATER OPERATIONS NOT RUNNING";
                cmdAxesHeader.Note.color = AvTheme.Dim;
                for (int i = 0; i < cmdAxisRows.Length; i++) cmdAxisRows[i].Hide();
                return 0;
            }

            ITheaterOperationsView ops = theaterOperations;
            bool canCommand = ops != null && ops.Available && ops.CanCommand;
            TheaterInfluenceView influence = canCommand ? ops.Influence : null;

            IReadOnlyList<TheaterPriorityOption> options = view.Options;
            int total = 0;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null) total++;

            int leans = influence != null && influence.Axes != null ? influence.Axes.Count : 0;
            int shown = 0;
            // Leaned objectives read first: the list holds four rows and a lean below the
            // fold would report leans the board never shows.
            for (int pass = 0; pass < 2 && shown < cmdAxisCapacity; pass++)
                for (int i = 0; i < options.Count && shown < cmdAxisCapacity; i++)
                {
                    TheaterPriorityOption option = options[i];
                    if (option == null) continue;

                    float weight = CmdAxisWeight(influence, option.Key);
                    if ((Math.Abs(weight) >= 0.001f) != (pass == 0)) continue;
                string figure = Math.Abs(weight) < 0.001f ? "—"
                    : (weight > 0f ? "FAVOR +" : "AVOID ") + weight.ToString("F1");
                string rail = weight > 0.001f ? "ready" : weight < -0.001f ? "cooling" : "info";
                cmdAxisRows[shown].Bind(
                    rail,
                    option.Label,
                    option.Detail,
                    figure,
                    false,
                    0f,
                    Math.Abs(weight) < 0.001f ? AvTheme.Dim : AvTheme.Accent,
                    AvTheme.RailInert,
                    canCommand ? (Action)(() => CycleCmdAxis(option.Key, weight)) : null,
                    canCommand
                        ? "Lean on this objective: favor it, avoid it, or leave it to the staff. " +
                          "A lean, never an order."
                        : "Read-only: the theater director is not running.");
                shown++;
            }
            for (int i = shown; i < cmdAxisRows.Length; i++) cmdAxisRows[i].Hide();

            // 4 is the director snapshot's own four axis pairs; the domain ceiling moves with it.
            cmdAxesHeader.Note.text = shown == 0
                ? "NO ACTIVE OBJECTIVES"
                : (total > shown ? "SHOWING " + shown + " OF " + total + "  ·  " : "") +
                  leans + " OF 4 LEANS";
            cmdAxesHeader.Note.color = shown == 0 ? AvTheme.Dim : AvTheme.RailCaution;
            return shown;
        }

        private static float CmdAxisWeight(TheaterInfluenceView influence, string key)
        {
            if (influence == null || influence.Axes == null || string.IsNullOrEmpty(key)) return 0f;
            for (int i = 0; i < influence.Axes.Count; i++)
            {
                TheaterAxisView axis = influence.Axes[i];
                if (axis != null && string.Equals(axis.Key, key, StringComparison.Ordinal))
                    return axis.Weight;
            }
            return 0f;
        }

        private void CycleCmdAxis(string key, float weight)
        {
            if (theaterOperations == null) return;
            // None favors, favor avoids, avoid releases: one click walks the whole lean.
            float next = Math.Abs(weight) < 0.001f ? 1f : weight > 0f ? -1f : 0f;
            if (theaterOperations.RequestAxis(key, next)) nextRefresh = 0f;
        }

        // ---- Staff log ----------------------------------------------------------------------------

        private void RefreshCmdLog()
        {
            ITheaterOperationsView view = theaterOperations;
            bool available = view != null && view.Available;
            IReadOnlyList<string> log = available ? view.StaffLog : null;

            cmdLogHeader.Note.text = available
                ? (log != null && log.Count > cmdLogLines.Length
                    ? "LATEST " + cmdLogLines.Length + " OF " + log.Count : "")
                : "THEATER OPERATIONS NOT RUNNING";
            cmdLogHeader.Note.color = AvTheme.Dim;

            for (int i = 0; i < cmdLogLines.Length; i++)
            {
                string line = log != null && i < log.Count ? log[i] : null;
                cmdLogLines[i].text = string.IsNullOrEmpty(line) ? "—" : line;
                cmdLogLines[i].color = string.IsNullOrEmpty(line) ? AvTheme.Disabled : AvTheme.Dim;
            }
        }

        // ---- Reinforcement and readiness -----------------------------------------------------

        /// <summary>Binds the reinforcement list; returns the visible row count.</summary>
        private int RefreshCmdReinforce()
        {
            ITheaterLogisticsView view = theaterLogistics;
            if (view == null || !view.Available)
            {
                cmdReinforceHeader.Note.text = "THEATER OPERATIONS NOT RUNNING";
                cmdReinforceHeader.Note.color = AvTheme.Dim;
                for (int i = 0; i < cmdReinforceRows.Length; i++) cmdReinforceRows[i].Hide();
                return 0;
            }

            if (!view.CanCommand)
            {
                cmdReinforceHeader.Note.text = "HOST FUNDS REINFORCEMENTS";
                cmdReinforceHeader.Note.color = AvTheme.Dim;
                for (int i = 0; i < cmdReinforceRows.Length; i++) cmdReinforceRows[i].Hide();
                return 0;
            }

            float funds = view.FactionFunds;
            cmdReinforceHeader.Note.text = "POOL " +
                (float.IsNaN(funds) ? "—" : UnitConverter.ValueReading(funds));
            cmdReinforceHeader.Note.color = AvTheme.Dim;

            IReadOnlyList<ReinforcementOption> options = view.Reinforcements;
            int total = 0;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null) total++;

            int shown = 0;
            for (int i = 0; i < options.Count && shown < cmdReinforceCapacity; i++)
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
                        ? "Fund " + option.Label + " from the faction pool. It enters the same " +
                          "supply queue an offensive's waves use."
                        : option.Affordable
                            ? "On cooldown until the previous delivery clears."
                            : "The faction pool cannot cover this call right now.");
                shown++;
            }
            for (int i = shown; i < cmdReinforceRows.Length; i++) cmdReinforceRows[i].Hide();

            if (total > shown)
                cmdReinforceHeader.Note.text += "  ·  SHOWING " + shown + " OF " + total;
            else if (shown == 0)
                cmdReinforceHeader.Note.text += "  ·  NO CONVOY GROUPS";
            return shown;
        }

        private void RefreshCmdReadiness()
        {
            bool available = theaterLogistics != null && theaterLogistics.Available;
            ReadinessSummary readiness = available ? theaterLogistics.Readiness : default;
            bool observed = available && readiness.Observed;

            cmdAwaitingLine.Set(
                observed ? readiness.UnitsAwaitingRearm.ToString() : "—",
                AvTheme.TextPrimary);
            cmdRearmLine.Set(
                observed
                    ? readiness.RearmAvailable + " / " + readiness.RearmAssets +
                      (readiness.RearmDepleted > 0 ? "  ·  " + readiness.RearmDepleted + " DEPLETED" : "")
                    : "—",
                observed && readiness.RearmDepleted > 0 ? AvTheme.RailCaution : AvTheme.TextPrimary);

            cmdReadinessNote.text = !available
                ? "THEATER OPERATIONS NOT RUNNING"
                : observed ? "LOCAL READ OF THE REARM NETWORK" : "REARM NETWORK UNAVAILABLE HERE";
            cmdReadinessNote.color = observed ? AvTheme.Dim : AvTheme.RailCaution;
        }

        private void FundCmdReinforcement(string key)
        {
            if (theaterLogistics == null || !theaterLogistics.RequestReinforcement(key)) return;
            nextRefresh = 0f;
        }

        // ---- Page furniture ------------------------------------------------------------------

        /// <summary>
        /// A section head the layout pass can move: the spine tick, the title, the live note
        /// on the right of the same line, and the hairline rule under both. The CMD page
        /// re-flows its sections when the lists change length, so unlike the fixed pages its
        /// headers cannot be drawn once and forgotten.
        /// </summary>
        private sealed class CmdHeader
        {
            private readonly Image tick;
            private readonly TMP_Text title;
            private readonly MfdGlyph icon;
            private readonly Image rule;
            private readonly AvButton hit;

            public TMP_Text Note { get; }

            public CmdHeader(RectTransform parent, string titleText, Action onClick = null)
            {
                tick = AvStyled.Box(parent, new Rect(0f, 0f, 5f, 1f), "spine-tick");
                var iconObject = new GameObject("SectionIcon", typeof(RectTransform), typeof(MfdGlyph));
                icon = iconObject.GetComponent<MfdGlyph>();
                iconObject.transform.SetParent(parent, false);
                icon.raycastTarget = false;
                icon.SetKind(SectionGlyph(titleText), AvTheme.RailInfo);
                title = AvStyled.Label(parent, new Rect(0f, 0f, 0f, 14f), titleText, "section-title");
                Note = AvStyled.Label(parent, new Rect(0f, 0f, 0f, 14f), "", "section-title-note",
                                      align: TextAlignmentOptions.MidlineRight);
                rule = AvKit.Rule(parent, new Rect(0f, 0f, 0f, 1f),
                                  AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.35f)));
                if (onClick != null)
                {
                    hit = AvKit.HitButton(parent, new Rect(0f, 0f, 1f, 14f), onClick);
                    hit.WithTooltip("Open the theater planning briefing.");
                }
            }

            public float Place(float x, float y, float width)
            {
                AvKit.Place(tick.rectTransform, new Rect(x - AvScreen.SpineInset + 3f, y - 7f, 5f, 1f));
                float titleWidth = width * 0.56f;
                AvKit.Place(icon.rectTransform, new Rect(x, y - 1f, 13f, 13f));
                AvKit.Place(title.rectTransform, new Rect(x + 19f, y, titleWidth - 19f, 14f));
                AvKit.Place(Note.rectTransform, new Rect(x + titleWidth, y, width - titleWidth, 14f));
                if (hit != null)
                    AvKit.Place((RectTransform)hit.transform, new Rect(x + titleWidth, y, width - titleWidth, 16f));
                AvKit.Place(rule.rectTransform, new Rect(x, y - 17f, width, 1f));
                return y - HeaderStep;
            }
        }

        /// <summary>One label/figure pair the layout pass can move.</summary>
        private sealed class CmdLine
        {
            private readonly TMP_Text key;
            private readonly TMP_Text value;
            private readonly AvButton hit;

            public CmdLine(RectTransform parent, string keyText, Action onClick = null)
            {
                key = AvStyled.Label(parent, new Rect(0f, 0f, 0f, 16f), keyText, "kv-key");
                value = AvStyled.Label(parent, new Rect(0f, 0f, 0f, 16f), "—", "kv-value",
                                       align: TextAlignmentOptions.MidlineRight);
                if (onClick != null)
                {
                    hit = AvKit.HitButton(parent, new Rect(0f, 0f, 1f, 18f), onClick);
                    hit.WithTooltip("Preview the current defense in the theater briefing.");
                }
            }

            public void Place(float x, float y, float width)
            {
                AvKit.Place(key.rectTransform, new Rect(x, y, width * 0.6f, 16f));
                AvKit.Place(value.rectTransform, new Rect(x + width * 0.6f, y, width * 0.4f, 16f));
                if (hit != null) AvKit.Place((RectTransform)hit.transform, new Rect(x, y, width, 18f));
            }

            public void Set(string text, Color colour)
            {
                value.text = text ?? "";
                value.color = colour;
            }

            public void SetEnabled(bool enabled) => hit?.SetEnabled(enabled);
        }

        // ---- The operation card --------------------------------------------------------------

        /// <summary>
        /// One operation card: a rail, the operation and its state, the live detail line, the
        /// staff's timeline, and its battle report once concluded. The board reports the push;
        /// the staff owns it, so unlike the old card this one carries no control.
        ///
        /// <para>The timeline is the point of the card. An offensive runs itself, so a bar that
        /// fills while the text says something else is a lie about what is happening; the five
        /// named stages say where the plan is and what it is waiting for. A concluded card
        /// swaps the timeline for its battle report.</para>
        ///
        /// <para>The card is a fixed frame; every refresh writes into it rather than rebuilding
        /// it, and the layout pass only moves the whole frame.</para>
        /// </summary>
        private sealed class OperationCard
        {
            private const float StateWidth = 138f;
            private const float TimelineTop = -44f;

            private readonly GameObject root;
            private readonly Image rail;
            private readonly TMP_Text name;
            private readonly TMP_Text state;
            private readonly TMP_Text detail;
            private readonly TMP_Text[] stages = new TMP_Text[TheaterReadout.OffensiveStages.Length];
            private readonly GameObject timelineRoot;
            private readonly Image timelineFill;
            private readonly TMP_Text report;
            private readonly AvButton hit;

            public OperationCard(RectTransform parent, float width, Action open)
            {
                root = new GameObject("OperationCard", typeof(RectTransform));
                var rect = (RectTransform)root.transform;
                rect.SetParent(parent, false);

                AvKit.Panel(rect, new Rect(0f, 0f, width, CmdCardHeight), AvTheme.Surface, AvSprites.Card);
                AvKit.Outline(rect, new Rect(0f, 0f, width, CmdCardHeight), AvTheme.Hairline);
                rail = AvKit.Rule(rect, new Rect(0f, 0f, 3f, CmdCardHeight), AvTheme.RailInert);

                name = AvStyled.Label(
                    rect, new Rect(8f, -5f, width - StateWidth - 16f, 16f), "", "row-name");
                state = AvStyled.Label(
                    rect, new Rect(width - StateWidth - 8f, -5f, StateWidth, 16f), "",
                    "row-value", align: TextAlignmentOptions.MidlineRight);
                detail = AvStyled.Label(rect, new Rect(8f, -23f, width - 16f, 14f), "", "row-sub");

                timelineRoot = new GameObject("Timeline", typeof(RectTransform));
                var timeline = (RectTransform)timelineRoot.transform;
                timeline.SetParent(rect, false);
                AvKit.Place(timeline, new Rect(8f, TimelineTop, width - 16f, 14f));

                float step = (width - 16f) / stages.Length;
                for (int i = 0; i < stages.Length; i++)
                {
                    stages[i] = AvStyled.Label(
                        timeline, new Rect(i * step, 0f, step - 2f, 11f),
                        TheaterReadout.OffensiveStages[i], "kv-key");
                    stages[i].characterSpacing = 0f;
                }

                AvKit.Panel(timeline, new Rect(0f, -12f, width - 16f, 2f), AvTheme.SurfaceInert);
                timelineFill = AvKit.Panel(timeline, new Rect(0f, -12f, width - 16f, 2f),
                                           AvTheme.RailInert);
                timelineFill.sprite = AvSprites.White;
                timelineFill.type = Image.Type.Filled;
                timelineFill.fillMethod = Image.FillMethod.Horizontal;
                timelineFill.fillOrigin = 0;
                timelineFill.fillAmount = 0f;

                report = AvStyled.Label(rect, new Rect(8f, TimelineTop - 1f, width - 16f, 14f), "",
                                        "row-sub");
                hit = AvKit.HitButton(rect, new Rect(0f, 0f, width, CmdCardHeight), open);
                hit.WithTooltip("Open this offensive in the theater planning briefing.");
            }

            public void Place(float x, float y, float width)
            {
                AvKit.Place((RectTransform)root.transform, new Rect(x, y, width, CmdCardHeight));
            }

            public void ShowCard()
            {
                if (!root.activeSelf) root.SetActive(true);
            }

            public void Hide()
            {
                if (root.activeSelf) root.SetActive(false);
            }

            public void Show(string railState, string title, string stateWord, string sub, bool warn = false)
            {
                rail.color = AvStyleHost.Resolve(
                    AvStyleHost.Style("rail " + railState).Background, AvTheme.RailInert);
                name.text = title ?? "";
                state.text = stateWord ?? "";
                state.color = AvTheme.TextPrimary;
                detail.text = sub ?? "";
                detail.color = warn ? AvTheme.RailCaution : AvTheme.Dim;
            }

            /// <summary>
            /// Marks the staff's timeline up to <paramref name="phase"/>. A concluded card, and
            /// any card with no timeline to show, hides the strip and uses the report line the
            /// caller writes with <see cref="SetSide"/>.
            /// </summary>
            public void SetTimeline(TheaterOperationPhase phase, Color colour, bool useReport)
            {
                if (timelineRoot.activeSelf == useReport) timelineRoot.SetActive(!useReport);
                if (useReport) return;

                int reached = TheaterReadout.OffensiveStage(phase);
                for (int i = 0; i < stages.Length; i++)
                {
                    stages[i].color = i < reached ? AvTheme.Dim
                                    : i == reached ? AvTheme.TextPrimary
                                    : AvTheme.Disabled;
                }

                timelineFill.color = colour;
                timelineFill.fillAmount = reached / (float)(stages.Length - 1);
            }

            /// <summary>Writes the report line and says whether it is on show.</summary>
            public void SetSide(string text, bool visible)
            {
                report.text = text ?? "";
                report.color = AvTheme.Dim;
                if (report.gameObject.activeSelf != visible) report.gameObject.SetActive(visible);
            }

            /// <summary>Cards carry no control; the overloads stay so callers read unchanged.</summary>
            public void SetPrimary(string label, bool enabled, Action action, string tooltip)
            {
            }

            public void SetSecondary(string label, bool enabled, Action action, string tooltip)
            {
            }
        }
    }
}
