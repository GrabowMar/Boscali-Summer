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

        private AvRow operationRow;
        private AvSection proposalSection;
        private AvRowStack proposals;
        private AvSection frontsSection;
        private AvList fronts;
        private AvSegmented postureControl;
        private ProseText warStatusText;
        private AvControl openRoomButton, replanButton;
        private StrPlanningWindow cmdPlanningWindow;
        private TheaterWarPosture selectedPosture;

        private void ResetCmd()
        {
            if (cmdPlanningWindow != null) Destroy(cmdPlanningWindow.gameObject);
            cmdPlanningWindow = null;
            operationRow = null;
            proposalSection = null;
            proposals = null;
            frontsSection = null;
            fronts = null;
            postureControl = null;
            warStatusText = null;
            openRoomButton = replanButton = null;
        }

        private void BuildCmdPage(AvFlow p)
        {
            p.Section(AvIcon.Flag, "LIVE OPERATION", "STAFF DIRECTED");
            operationRow = p.Add(new AvRow(p.Content));
            AvButtons ops = p.Buttons(
                new AvControl.Spec("OPEN ROOM", OpenCmdPlanning, AvButtonStyle.Default, AvIcon.Maximize),
                new AvControl.Spec("REPLAN", CancelCmdOperation, AvButtonStyle.Danger, AvIcon.X));
            openRoomButton = ops.Controls[0];
            replanButton = ops.Controls[1];

            proposalSection = p.Section(AvIcon.ListDetails, "STAFF PROPOSALS", "CHOOSE OR STAFF DECIDES");
            proposals = p.Add(new AvRowStack(p.Content, ProposalSlots, PickCmdProposal));

            frontsSection = p.Section(AvIcon.MapPin, "ACTIVE FRONTS", "MOST URGENT");
            fronts = p.Add(new AvList(p.Content, console.Ticker, FrontSlots, BindFrontRow));

            p.Section(AvIcon.AdjustmentsHorizontal, "STAFF POSTURE", "BROAD INTENT");
            postureControl = p.Add(new AvSegmented(p.Content, "POSTURE",
                new[] { "CAUTIOUS", "STEADY", "BOLD" }, () => (int)selectedPosture, i => SetCmdPosture((TheaterWarPosture)i)));
            warStatusText = p.Add(new ProseText(p.Content));
        }

        private void BindFrontRow(int index, AvRow row)
        {
            IReadOnlyList<TheaterFrontView> list = theaterWar?.Available == true ? theaterWar.Fronts : null;
            TheaterFrontView front = list != null && index < list.Count ? list[index] : null;
            if (front == null) { row.Set("—", "", "", AvState.Inert); return; }
            if (front.Observed)
            {
                row.Set(front.Label, front.Status, TheaterReadout.Percent(Mathf.Clamp01(front.Pressure)),
                    front.Pressure > 0.5f ? AvState.Caution : AvState.Info);
            }
            else
            {
                string age = front.AgeSeconds < 0f ? "UNCONFIRMED REPORT"
                    : "UNCONFIRMED · AGE " + Mathf.RoundToInt(front.AgeSeconds) + "S";
                row.Set(front.Label, age, "", AvState.Inert);
            }
        }

        private void RefreshCmd()
        {
            ITheaterWarView war = theaterWar;
            bool ready = war != null && war.Available;
            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;
            operationRow.Set(
                active == null ? "NO PRIMARY OPERATION" : active.Label,
                active == null ? "Local clashes continue while the staff assesses openings."
                    : active.Kind + " / " + active.Phase + " · " + active.Summary +
                      "  ·  " + active.GroundGroups + " GROUND / " + active.AirGroups + " AIR / " + active.NavalGroups + " NAVAL",
                "", AvState.Info);
            replanButton.Interactable = ready && war.CanCommand && active != null;

            IReadOnlyList<TheaterProposalView> propList = ready ? war.Proposals : null;
            int count = propList != null ? Mathf.Min(propList.Count, ProposalSlots) : 0;
            proposalSection.SetCaption(!ready ? "UNAVAILABLE" : count == 0
                ? "STAFF WILL ACT ON ITS OWN" : count + " OPENING" + (count == 1 ? "" : "S"));
            for (int i = 0; i < ProposalSlots; i++)
            {
                if (i >= count) { proposals.Hide(i); continue; }
                TheaterProposalView proposal = propList[i];
                proposals.Show(i);
                AvRow row = proposals.Row(i);
                row.Set(proposal.Kind + " / " + proposal.Label,
                    proposal.Brief + "  ·  " + proposal.Forces + "  ·  RISK " + proposal.Risk,
                    Mathf.CeilToInt(Mathf.Max(0f, proposal.SecondsRemaining)) + "S", AvState.Ready);
                row.Interactable = war.CanCommand;
            }

            IReadOnlyList<TheaterFrontView> frontList = ready ? war.Fronts : null;
            frontsSection.SetCaption(!ready ? "NO VERIFIED FRONT REPORT" : (frontList?.Count ?? 0) + " TRACKED");
            fronts.SetCount(frontList?.Count ?? 0);

            selectedPosture = ready ? war.Posture : TheaterWarPosture.Steady;
            postureControl.Refresh();
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
