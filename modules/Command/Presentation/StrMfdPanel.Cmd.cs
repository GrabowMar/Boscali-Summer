using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>The cockpit's short war brief. The staff owns forces; the pilot chooses intent.</summary>
    internal sealed partial class StrMfdPanel
    {
        private const int ProposalSlots = 3;
        private const int FrontSlots = 4;
        private const int StaffLogRows = 8;

        private AvSection opSection;
        private StrOpCard operationCard;
        private StrNote operationNote;
        private AvSection proposalSection;
        private StrProposalDeck proposals;
        private StrNote proposalNote;
        private AvSection frontsSection;
        private StrFrontBoard frontBoard;
        private StrNote frontsNote;
        private AvSegmented postureControl;
        private AvSection staffLogSection;
        private StrLogBoard staffLog;
        private StrNote staffLogNote;
        private AvButtons replanRow;
        private AvControl replanButton;
        private TheaterWarPosture selectedPosture;
        private StrNote staffState, postureNote;
        private int paintedActiveId, paintedActiveRevision;

        private void ResetCmd()
        {
            opSection = null;
            operationCard = null;
            operationNote = null;
            proposalSection = null;
            proposals = null;
            proposalNote = null;
            frontsSection = null;
            frontBoard = null;
            frontsNote = null;
            postureControl = null;
            staffLogSection = null;
            staffLog = null;
            staffLogNote = null;
            replanRow = null;
            replanButton = null;
            staffState = postureNote = null;
            paintedActiveId = paintedActiveRevision = 0;
        }

        private void BuildCmdPage(AvFlow p)
        {
            staffState = p.Add(new StrNote(p.Content, AvIcon.Radio));
            opSection = p.Section(AvIcon.Flag, "LIVE OPERATION", "STAFF DIRECTED");
            operationCard = p.Add(new StrOpCard(p.Content));
            operationNote = p.Add(new StrNote(p.Content, AvIcon.Flag));
            replanRow = p.Buttons(
                new AvControl.Spec("REPLAN", CancelCmdOperation, AvButtonStyle.Danger, AvIcon.X));
            replanButton = replanRow.Controls[0];
            replanButton.Help = "Ask the host to call off the displayed operation and return its groups to staff tasking.";

            proposalSection = p.Section(AvIcon.ListDetails, "STAFF PROPOSALS", "PICK");
            proposals = p.Add(new StrProposalDeck(p.Content, ProposalSlots, PickCmdProposal));
            proposalNote = p.Add(new StrNote(p.Content, AvIcon.ListDetails));

            frontsSection = p.Section(AvIcon.MapPin, "FRONTS", "BY PRESSURE");
            frontBoard = p.Add(new StrFrontBoard(p.Content, FrontSlots, BindCmdFront));
            frontsNote = p.Add(new StrNote(p.Content, AvIcon.MapPin));

            postureControl = p.Add(new AvSegmented(p.Content, "STAFF POSTURE",
                new[] { "CAUTIOUS", "STEADY", "BOLD" },
                () => theaterWar?.Available == true && theaterWar.HasSnapshot ? (int)selectedPosture : -1,
                i => SetCmdPosture((TheaterWarPosture)i)));
            for (int i = 0; i < 3; i++) postureControl.Options[i].Help = PostureBrief((TheaterWarPosture)i);
            postureNote = p.Add(new StrNote(p.Content, AvIcon.Flag));

            // The staff's own log is the page's growing element: newest line first, as many as fit.
            staffLogSection = p.Section(AvIcon.ListDetails, "OPERATIONS LOG", "NEWEST FIRST");
            staffLog = p.Add(new StrLogBoard(p.Content, StaffLogRows, null, 2), 1f);
            staffLogNote = p.Add(new StrNote(p.Content, AvIcon.ListDetails));
        }

        private void BindCmdFront(int index, StrFrontBoard.Row row)
        {
            IReadOnlyList<TheaterFrontView> list = theaterWar != null && theaterWar.Available ? theaterWar.Fronts : null;
            FillFront(row, list != null && index < list.Count ? list[index] : null, index);
        }

        /// <summary>What a posture changes, in the staff's own terms (it biases which assaults the staff offers).</summary>
        internal static string PostureBrief(TheaterWarPosture posture)
        {
            switch (posture)
            {
                case TheaterWarPosture.Cautious: return "New assaults need at least 1.25 nearby friendly units per observed hostile unit. Active operation continues.";
                case TheaterWarPosture.Bold: return "New assaults need at least 0.75 nearby friendly units per observed hostile unit. Active operation continues.";
                default: return "New assaults need at least 1 nearby friendly unit per observed hostile unit. Active operation continues.";
            }
        }

        internal static void FillFront(StrFrontBoard.Row row, TheaterFrontView front, int index = -1)
        {
            if (front == null) { row.Set("—", "", 0f, false, AvState.Inert, null); return; }
            if (front.Observed)
            {
                float pressure = Mathf.Clamp01(front.Pressure);
                row.Set((index >= 0 ? "F" + (index + 1) + " · " : "") + front.Label, front.Status, pressure, true,
                    pressure > 0.5f ? AvState.Caution : AvState.Info,
                    front.Label + " — " + (front.Status ?? "").ToLowerInvariant() + ", " +
                    TheaterReadout.Percent(pressure) + " pressure.");
            }
            else
            {
                string age = front.AgeSeconds < 0f ? "UNCONFIRMED REPORT"
                    : "UNCONFIRMED · AGE " + Mathf.RoundToInt(front.AgeSeconds) + "S";
                row.Set((index >= 0 ? "F" + (index + 1) + " · " : "") + front.Label, age, 0f, false, AvState.Inert, front.Label + " — no confirmed contact.");
            }
        }

        /// <summary>Fill the proposal deck.</summary>
        internal static void FillProposals(StrProposalDeck deck, IReadOnlyList<TheaterProposalView> list,
            int slots, bool canCommand, bool current = true)
        {
            int count = list != null ? Mathf.Min(list.Count, slots) : 0;
            for (int i = 0; i < deck.Slots; i++)
            {
                if (i >= count) { deck.Hide(i); continue; }
                TheaterProposalView proposal = list[i];
                deck.Set(i, proposal.Id, proposal.Revision, proposal.Kind, proposal.Label, proposal.Brief, proposal.Forces, proposal.Risk,
                    current ? Mathf.CeilToInt(Mathf.Max(0f, proposal.SecondsRemaining)) : -1, canCommand,
                    canCommand ? "Choose this staff proposal; the host validates the current offer."
                        : "Commands are unavailable until the staff report is current and no command is pending.");
            }
        }

        private void RefreshCmd()
        {
            ITheaterWarView war = theaterWar;
            bool ready = war != null && war.Available && war.HasSnapshot;
            FillStaffState(staffState, war);
            // Without a report every block below would only repeat "awaiting the host"; the status note says it once.
            foreach (AvPart part in new AvPart[] { opSection, replanRow, proposalSection, frontsSection, postureControl, postureNote, staffLogSection })
                part.SetShown(ready);
            if (!ready)
            {
                foreach (AvPart part in new AvPart[] { operationCard, operationNote, proposals, proposalNote, frontBoard, frontsNote, staffLog, staffLogNote })
                    part.SetShown(false);
                paintedActiveId = paintedActiveRevision = 0;
                return;
            }
            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;

            opSection.SetCaption(!ready ? "NO CURRENT REPORT" : "ONE PRIMARY");
            operationCard.SetShown(active != null);
            operationNote.SetShown(active == null);
            if (active != null)
            {
                paintedActiveId = active.Id;
                paintedActiveRevision = active.Revision;
                operationCard.Set(active.Kind, active.Label, active.Phase, active.Summary,
                    active.GroundGroups, active.AirGroups, active.NavalGroups);
            }
            else
            {
                paintedActiveId = paintedActiveRevision = 0;
                operationNote.Set(ready ? "NO ACTIVE OPERATION" : "AWAITING STAFF", ready ? "Staff is assessing the fronts." : "Host report required.");
            }
            replanButton.Interactable = ready && war.CanCommand && active != null;

            IReadOnlyList<TheaterProposalView> propList = ready ? war.Proposals : null;
            int count = propList != null ? Mathf.Min(propList.Count, ProposalSlots) : 0;
            proposalSection.SetCaption(!ready ? "OFFLINE" : count == 0
                ? "ASSESSING" : AutoSelectionCaption(propList, war.SnapshotAgeSeconds <= 15f));
            proposals.SetShown(count > 0);
            proposalNote.SetShown(count == 0);
            if (count > 0) FillProposals(proposals, propList, ProposalSlots, war.CanCommand, war.SnapshotAgeSeconds <= 15f);
            else
                proposalNote.Set(ready ? "NO ELIGIBLE OPENINGS" : "NO PROPOSALS", ready ? "Staff continues its review. Nearby forces are not yet assigned." : "Awaiting the host's faction report.");

            IReadOnlyList<TheaterFrontView> frontList = ready ? war.Fronts : null;
            int frontCount = frontList?.Count ?? 0;
            frontsSection.SetCaption(!ready ? "NO REPORT" : frontCount + " TRACKED");
            frontBoard.SetCount(frontCount);
            frontBoard.SetShown(frontCount > 0);
            frontsNote.SetShown(frontCount == 0);
            if (frontCount == 0) frontsNote.Set(ready ? "NO FRONTS" : "FRONT REPORT UNAVAILABLE", ready ? "" : "Awaiting the host's faction report.");

            selectedPosture = ready ? war.Posture : TheaterWarPosture.Steady;
            postureControl.Refresh();
            foreach (AvControl option in postureControl.Options) option.Interactable = ready && war.CanCommand;
            postureNote.Set(ready ? "POSTURE · " + selectedPosture.ToString().ToUpperInvariant() : "POSTURE UNAVAILABLE",
                ready ? PostureBrief(selectedPosture) : "Awaiting the host's faction report.", ready ? AvState.Info : AvState.Inert);

            IReadOnlyList<string> log = ready ? war.StaffLog : null;
            int lines = log == null ? 0 : Mathf.Min(log.Count, staffLog.Capacity);
            staffLog.Begin();
            for (int i = 0; i < lines; i++)
                staffLog.Add((i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture), log[i],
                    AvState.Info, false, "Staff log entry " + (i + 1) + ", newest first.");
            staffLog.End();
            staffLog.SetShown(lines > 0);
            staffLogNote.SetShown(lines == 0);
            if (lines == 0) staffLogNote.Set(ready ? "NO STAFF TRAFFIC" : "STAFF LOG UNAVAILABLE", ready ? "" : "Awaiting the host's faction report.");
            staffLogSection.SetCaption(!ready ? "WAITING FOR REPORT" : lines == 0 ? "QUIET" : lines + (lines == 1 ? " ENTRY" : " ENTRIES"));
        }

        internal static string AutoSelectionCaption(IReadOnlyList<TheaterProposalView> list, bool current = true) =>
            list == null || list.Count == 0 ? "ASSESSING" : !current ? "DEADLINE UNCONFIRMED · O1 DEFAULT"
                : "STAFF SELECTS IN " + Mathf.CeilToInt(Mathf.Max(0f, list[0].SecondsRemaining)) + "S · O1 DEFAULT";

        internal static void FillStaffState(StrNote note, ITheaterWarView war)
        {
            if (war == null) { note.Set("THEATER OPS OFF", "Enable TheaterOps in F1 module settings and restart."); return; }
            if (!war.Available) { note.Set("STAFF DISABLED", "Living Front is unavailable for this faction."); return; }
            string status = war.CommandStatus ?? "";
            bool unconfirmed = status.StartsWith("UNCONFIRMED", StringComparison.OrdinalIgnoreCase);
            if (!war.HasSnapshot)
            {
                note.Set(unconfirmed ? "COMMAND UNCONFIRMED" : "WAITING FOR HOST",
                    "Requesting the faction staff report. Commands await a current report." + (status.Length > 0 ? "\n" + status : ""),
                    unconfirmed ? AvState.Caution : AvState.Info);
                return;
            }
            bool stale = war.SnapshotAgeSeconds > 15f;
            string age = "REPORT AGE " + Mathf.CeilToInt(Mathf.Max(0f, war.SnapshotAgeSeconds)) + "S";
            string title = stale ? "STALE STAFF REPORT" : war.CommandPending ? "COMMAND PENDING" : "STAFF CURRENT";
            AvState tone = stale || war.CommandPending ? AvState.Caution : AvState.Info;
            if (!stale && !war.CommandPending && unconfirmed) { title = "COMMAND UNCONFIRMED"; tone = AvState.Caution; }
            if (!stale && !war.CommandPending && status.StartsWith("REJECTED", StringComparison.OrdinalIgnoreCase)) { title = "COMMAND REJECTED"; tone = AvState.Danger; }
            if (!stale && !war.CommandPending && status.StartsWith("ACCEPTED", StringComparison.OrdinalIgnoreCase)) { title = "COMMAND ACCEPTED"; tone = AvState.Ready; }
            string detail = age + " · " + (stale ? "RECOVERING HOST REPORT · COMMANDS DISABLED" : war.CanCommand ? "HOST VALIDATES INTENT" : war.CommandPending ? "AWAITING HOST ACKNOWLEDGEMENT" : "OBSERVING STAFF");
            note.Set(title, detail + (status.Length > 0 ? "\n" + status : ""), tone);
        }

        private void PickCmdProposal(int id, int revision)
        {
            if (theaterWar == null || !theaterWar.CanCommand) return;
            theaterWar.RequestPick(id, revision);
            nextRefresh = 0f;
            RefreshCmd();
        }

        private void CancelCmdOperation()
        {
            if (theaterWar == null || !theaterWar.CanCommand || paintedActiveId <= 0) return;
            theaterWar.RequestCancel(paintedActiveId, paintedActiveRevision);
            nextRefresh = 0f;
            RefreshCmd();
        }

        private void SetCmdPosture(TheaterWarPosture posture)
        {
            if (theaterWar == null || !theaterWar.CanCommand) return;
            theaterWar.RequestPosture(posture);
            nextRefresh = 0f;
            RefreshCmd();
        }
    }
}
