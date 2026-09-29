using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>The cockpit's short war brief. The staff owns forces; the pilot chooses intent.</summary>
    internal sealed partial class StrMfdPanel
    {
        private const int ProposalSlots = 3;
        private const int FrontSlots = 4;

        private StrOpCard operationCard;
        private StrNote operationNote;
        private AvSection proposalSection;
        private StrProposalDeck proposals;
        private StrNote proposalNote;
        private AvSection frontsSection;
        private StrFrontBoard fronts;
        private StrNote frontsNote;
        private AvSegmented postureControl;
        private ProseText postureText;
        private ProseText warStatusText;
        private AvControl openRoomButton, replanButton;
        private StrPlanningWindow cmdPlanningWindow;
        private TheaterWarPosture selectedPosture;

        private void ResetCmd()
        {
            if (cmdPlanningWindow != null) Destroy(cmdPlanningWindow.gameObject);
            cmdPlanningWindow = null;
            operationCard = null;
            operationNote = null;
            proposalSection = null;
            proposals = null;
            proposalNote = null;
            frontsSection = null;
            fronts = null;
            frontsNote = null;
            postureControl = null;
            postureText = null;
            warStatusText = null;
            openRoomButton = replanButton = null;
        }

        private void BuildCmdPage(AvFlow p)
        {
            p.Section(AvIcon.Flag, "LIVE OPERATION", "STAFF DIRECTED");
            operationCard = p.Add(new StrOpCard(p.Content));
            operationNote = p.Add(new StrNote(p.Content, AvIcon.Flag));
            AvButtons ops = p.Buttons(
                new AvControl.Spec("OPEN ROOM", OpenCmdPlanning, AvButtonStyle.Default, AvIcon.Maximize),
                new AvControl.Spec("REPLAN", CancelCmdOperation, AvButtonStyle.Danger, AvIcon.X));
            openRoomButton = ops.Controls[0];
            replanButton = ops.Controls[1];
            openRoomButton.Help = "Open the operations room and live theater map.";
            replanButton.Help = "Call off this operation and ask staff for new choices.";

            proposalSection = p.Section(AvIcon.ListDetails, "STAFF PROPOSALS", "CHOOSE OR STAFF DECIDES");
            proposals = p.Add(new StrProposalDeck(p.Content, ProposalSlots, PickCmdProposal));
            proposalNote = p.Add(new StrNote(p.Content, AvIcon.ListDetails));

            frontsSection = p.Section(AvIcon.MapPin, "ACTIVE FRONTS", "MOST URGENT");
            fronts = p.Add(new StrFrontBoard(p.Content, FrontSlots, BindFrontRow));
            frontsNote = p.Add(new StrNote(p.Content, AvIcon.MapPin));

            p.Section(AvIcon.AdjustmentsHorizontal, "STAFF POSTURE", "BROAD INTENT");
            postureControl = p.Add(new AvSegmented(p.Content, "POSTURE",
                new[] { "CAUTIOUS", "STEADY", "BOLD" }, () => (int)selectedPosture, i => SetCmdPosture((TheaterWarPosture)i)));
            postureText = p.Add(new ProseText(p.Content));
            warStatusText = p.Add(new ProseText(p.Content));
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

        private void BindFrontRow(int index, StrFrontBoard.Row row)
        {
            IReadOnlyList<TheaterFrontView> list = theaterWar?.Available == true ? theaterWar.Fronts : null;
            FillFront(row, list != null && index < list.Count ? list[index] : null);
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

            operationCard.SetShown(active != null);
            operationNote.SetShown(active == null);
            if (active != null)
                operationCard.Set(active.Kind, active.Label, active.Phase, active.Summary,
                    active.GroundGroups, active.AirGroups, active.NavalGroups);
            else
                operationNote.Set(ready ? "NO PRIMARY OPERATION" : "THEATER STAFF UNAVAILABLE",
                    ready ? "Local clashes continue while the staff assesses openings."
                        : "No staff is running for this faction. Fronts and offers appear once it forms.");
            replanButton.Interactable = ready && war.CanCommand && active != null;

            IReadOnlyList<TheaterProposalView> propList = ready ? war.Proposals : null;
            int count = propList != null ? Mathf.Min(propList.Count, ProposalSlots) : 0;
            proposalSection.SetCaption(!ready ? "UNAVAILABLE" : count == 0
                ? "STAFF WILL ACT ON ITS OWN" : count + " OPENING" + (count == 1 ? "" : "S"));
            proposals.SetShown(count > 0);
            proposalNote.SetShown(count == 0);
            if (count > 0) FillProposals(proposals, propList, ProposalSlots, war.CanCommand);
            else
                proposalNote.Set(ready ? "NO OPENINGS ON OFFER" : "NO STAFF PROPOSALS",
                    ready ? "The staff spends from the faction pool and acts on its own until it sees an opening."
                        : "Proposals appear here once theater staff is available.");

            IReadOnlyList<TheaterFrontView> frontList = ready ? war.Fronts : null;
            int frontCount = frontList?.Count ?? 0;
            frontsSection.SetCaption(!ready ? "NO VERIFIED FRONT REPORT" : frontCount + " TRACKED");
            fronts.SetCount(frontCount);
            fronts.SetShown(frontCount > 0);
            frontsNote.SetShown(frontCount == 0);
            if (frontCount == 0)
                frontsNote.Set("NO FRONTS REPORTED", "Verified fronts appear here with their pressure once staff has contact.");

            selectedPosture = ready ? war.Posture : TheaterWarPosture.Steady;
            postureControl.Refresh();
            postureText.Set(PostureBrief(selectedPosture));
            warStatusText.Set(!ready ? "THEATER STAFF UNAVAILABLE" : war.CanCommand
                ? "STAFF SPENDS FROM THE FACTION POOL AUTOMATICALLY"
                : "OBSERVE / HOST VALIDATES ORDERS");
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
