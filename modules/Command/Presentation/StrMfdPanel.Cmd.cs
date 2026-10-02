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
        private AvControl openRoomButton, replanButton;
        private StrPlanningWindow cmdPlanningWindow;
        private TheaterWarPosture selectedPosture;

        private void ResetCmd()
        {
            if (cmdPlanningWindow != null) Destroy(cmdPlanningWindow.gameObject);
            cmdPlanningWindow = null;
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
            openRoomButton = replanButton = null;
        }

        private void BuildCmdPage(AvFlow p)
        {
            opSection = p.Section(AvIcon.Flag, "LIVE OPERATION", "STAFF DIRECTED");
            operationCard = p.Add(new StrOpCard(p.Content));
            operationNote = p.Add(new StrNote(p.Content, AvIcon.Flag));
            AvButtons ops = p.Buttons(
                new AvControl.Spec("ROOM", OpenCmdPlanning, AvButtonStyle.Default, AvIcon.Maximize),
                new AvControl.Spec("REPLAN", CancelCmdOperation, AvButtonStyle.Danger, AvIcon.X));
            openRoomButton = ops.Controls[0];
            replanButton = ops.Controls[1];
            openRoomButton.Help = "Open the operations room: the live theater map beside the operation, the staff's offers and the fronts.";
            replanButton.Help = "Call off this operation and ask staff for new choices. Forces already committed return to the pool.";

            proposalSection = p.Section(AvIcon.ListDetails, "STAFF PROPOSALS", "PICK");
            proposals = p.Add(new StrProposalDeck(p.Content, ProposalSlots, PickCmdProposal));
            proposalNote = p.Add(new StrNote(p.Content, AvIcon.ListDetails));

            frontsSection = p.Section(AvIcon.MapPin, "FRONTS", "BY PRESSURE");
            frontBoard = p.Add(new StrFrontBoard(p.Content, FrontSlots, BindCmdFront));
            frontsNote = p.Add(new StrNote(p.Content, AvIcon.MapPin));

            postureControl = p.Add(new AvSegmented(p.Content, "STAFF POSTURE",
                new[] { "CAUTIOUS", "STEADY", "BOLD" }, () => (int)selectedPosture, i => SetCmdPosture((TheaterWarPosture)i)));
            for (int i = 0; i < 3; i++) postureControl.Options[i].Help = PostureBrief((TheaterWarPosture)i);

            // The staff's own log is the page's growing element: newest line first, as many as fit.
            staffLogSection = p.Section(AvIcon.ListDetails, "STAFF LOG", "NEWEST FIRST");
            staffLog = p.Add(new StrLogBoard(p.Content, StaffLogRows, null, 2), 1f);
            staffLogNote = p.Add(new StrNote(p.Content, AvIcon.ListDetails), 1f);
        }

        private void BindCmdFront(int index, StrFrontBoard.Row row)
        {
            IReadOnlyList<TheaterFrontView> list = theaterWar != null && theaterWar.Available ? theaterWar.Fronts : null;
            FillFront(row, list != null && index < list.Count ? list[index] : null);
        }

        /// <summary>What a posture changes, in the staff's own terms (it biases which assaults the staff offers).</summary>
        internal static string PostureBrief(TheaterWarPosture posture)
        {
            switch (posture)
            {
                case TheaterWarPosture.Cautious: return "CAUTIOUS: staff defends first and offers assaults only at clear odds.";
                case TheaterWarPosture.Bold: return "BOLD: staff offers assaults sooner and accepts thinner odds.";
                default: return "STEADY: staff weighs assault and defence evenly.";
            }
        }

        internal static void FillFront(StrFrontBoard.Row row, TheaterFrontView front)
        {
            if (front == null) { row.Set("—", "", 0f, false, AvState.Inert, null); return; }
            if (front.Observed)
            {
                float pressure = Mathf.Clamp01(front.Pressure);
                row.Set(front.Label, front.Status, pressure, true,
                    pressure > 0.5f ? AvState.Caution : AvState.Info,
                    front.Label + " — " + (front.Status ?? "").ToLowerInvariant() + ", " +
                    TheaterReadout.Percent(pressure) + " pressure.");
            }
            else
            {
                string age = front.AgeSeconds < 0f ? "UNCONFIRMED REPORT"
                    : "UNCONFIRMED · AGE " + Mathf.RoundToInt(front.AgeSeconds) + "S";
                row.Set(front.Label, age, 0f, false, AvState.Inert, front.Label + " — no confirmed contact.");
            }
        }

        /// <summary>Fill the proposal deck; shared by the console page and the operations room.</summary>
        internal static void FillProposals(StrProposalDeck deck, IReadOnlyList<TheaterProposalView> list,
            int slots, bool canCommand)
        {
            int count = list != null ? Mathf.Min(list.Count, slots) : 0;
            for (int i = 0; i < deck.Slots; i++)
            {
                if (i >= count) { deck.Hide(i); continue; }
                TheaterProposalView proposal = list[i];
                deck.Set(i, proposal.Kind, proposal.Label, proposal.Brief, proposal.Forces, proposal.Risk,
                    Mathf.CeilToInt(Mathf.Max(0f, proposal.SecondsRemaining)), canCommand,
                    canCommand ? "Choose this staff proposal; the host validates the current offer."
                        : "Staff proposal. Only the host can choose.");
            }
        }

        private void RefreshCmd()
        {
            ITheaterWarView war = theaterWar;
            bool ready = war != null && war.Available;
            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;

            opSection.SetCaption(!ready ? "STAFF OFFLINE" : war.CanCommand ? "STAFF AUTO" : "OBSERVING");
            operationCard.SetShown(active != null);
            operationNote.SetShown(active == null);
            if (active != null)
                operationCard.Set(active.Kind, active.Label, active.Phase, active.Summary,
                    active.GroundGroups, active.AirGroups, active.NavalGroups);
            else
                operationNote.Set(ready ? "NO OPERATION" : "STAFF OFFLINE", "");
            replanButton.Interactable = ready && war.CanCommand && active != null;

            IReadOnlyList<TheaterProposalView> propList = ready ? war.Proposals : null;
            int count = propList != null ? Mathf.Min(propList.Count, ProposalSlots) : 0;
            proposalSection.SetCaption(!ready ? "OFFLINE" : count == 0
                ? "AUTO" : count + " OPENING" + (count == 1 ? "" : "S"));
            proposals.SetShown(count > 0);
            proposalNote.SetShown(count == 0);
            if (count > 0) FillProposals(proposals, propList, ProposalSlots, war.CanCommand);
            else
                proposalNote.Set(ready ? "NO OPENINGS" : "NO PROPOSALS", "");

            IReadOnlyList<TheaterFrontView> frontList = ready ? war.Fronts : null;
            int frontCount = frontList?.Count ?? 0;
            frontsSection.SetCaption(!ready ? "NO REPORT" : frontCount + " TRACKED");
            frontBoard.SetCount(frontCount);
            frontBoard.SetShown(frontCount > 0);
            frontsNote.SetShown(frontCount == 0);
            if (frontCount == 0) frontsNote.Set("NO FRONTS", "");

            selectedPosture = ready ? war.Posture : TheaterWarPosture.Steady;
            postureControl.Refresh();

            IReadOnlyList<string> log = ready ? war.StaffLog : null;
            int lines = log == null ? 0 : Mathf.Min(log.Count, staffLog.Capacity);
            staffLog.Begin();
            for (int i = 0; i < lines; i++)
                staffLog.Add((i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture), log[i],
                    AvState.Info, false, "Staff log entry " + (i + 1) + ", newest first.");
            staffLog.End();
            staffLog.SetShown(lines > 0);
            staffLogNote.SetShown(lines == 0);
            if (lines == 0) staffLogNote.Set(ready ? "NO STAFF TRAFFIC" : "STAFF OFFLINE", "");
            staffLogSection.SetCaption(lines == 0 ? "QUIET" : lines + (lines == 1 ? " ENTRY" : " ENTRIES"));
        }

        private void PickCmdProposal(int slot)
        {
            IReadOnlyList<TheaterProposalView> proposalList = theaterWar?.Proposals;
            if (proposalList == null || slot < 0 || slot >= proposalList.Count) return;
            TheaterProposalView proposal = proposalList[slot];
            if (theaterWar.RequestPick(proposal.Id, proposal.Revision)) nextRefresh = 0f;
        }

        private void CancelCmdOperation()
        {
            TheaterLiveOperationView active = theaterWar?.ActiveOperation;
            if (active != null && theaterWar.RequestCancel(active.Id, active.Revision)) nextRefresh = 0f;
        }

        private void SetCmdPosture(TheaterWarPosture posture)
        {
            if (theaterWar != null && theaterWar.RequestPosture(posture)) nextRefresh = 0f;
        }

        private void OpenCmdPlanning()
        {
            if (cmdPlanningWindow == null)
                cmdPlanningWindow = StrPlanningWindow.Create(theaterWar, overlay);
            cmdPlanningWindow.Show();
        }
    }
}
