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
    /// <summary>The cockpit's short war brief. The staff owns forces; the pilot chooses intent.</summary>
    internal sealed partial class StrMfdPanel
    {
        private const int ProposalSlots = 3;
        private const int FrontSlots = 4;
        private readonly TMP_Text[] proposalNames = new TMP_Text[ProposalSlots];
        private readonly TMP_Text[] proposalBriefs = new TMP_Text[ProposalSlots];
        private readonly TMP_Text[] proposalTimes = new TMP_Text[ProposalSlots];
        private readonly AvButton[] proposalButtons = new AvButton[ProposalSlots];
        private readonly GameObject[] proposalRoots = new GameObject[ProposalSlots];
        private readonly TMP_Text[] frontNames = new TMP_Text[FrontSlots];
        private readonly TMP_Text[] frontStates = new TMP_Text[FrontSlots];
        private TMP_Text operationName, operationState, operationForces, proposalNote, frontsNote, warStatus;
        private AvButton cancelOperation;
        private AvButton[] postureButtons;
        private StrPlanningWindow cmdPlanningWindow;

        private void ResetCmd()
        {
            if (cmdPlanningWindow != null) Destroy(cmdPlanningWindow.gameObject);
            cmdPlanningWindow = null;
            operationName = operationState = operationForces = proposalNote = frontsNote = warStatus = null;
            cancelOperation = null;
            postureButtons = null;
            Array.Clear(proposalNames, 0, proposalNames.Length);
            Array.Clear(proposalBriefs, 0, proposalBriefs.Length);
            Array.Clear(proposalTimes, 0, proposalTimes.Length);
            Array.Clear(proposalButtons, 0, proposalButtons.Length);
            Array.Clear(proposalRoots, 0, proposalRoots.Length);
            Array.Clear(frontNames, 0, frontNames.Length);
            Array.Clear(frontStates, 0, frontStates.Length);
        }

        private void BuildCmdPage(GameObject page)
        {
            Rect view = shell.Body;
            const float contentHeight = 766f;
            Rect body;
            RectTransform root = AvScreen.Scroll((RectTransform)page.transform, view,
                Mathf.Max(view.height, contentHeight), out body);
            AvStyled.Spine(root, new Rect(body.x, body.y, 3f, body.height));
            float x = body.x + AvScreen.SpineInset;
            float w = body.width - AvScreen.SpineInset;
            float y = body.y;

            y = SectionHeader(root, x, y, w, "LIVE OPERATION", "STAFF DIRECTED", false);
            AvKit.Panel(root, new Rect(x, y, w, 95f), AvTheme.Surface);
            AvKit.Rule(root, new Rect(x, y, 3f, 95f), AvTheme.RailInfo);
            operationName = AvStyled.Label(root, new Rect(x + 12f, y - 8f, w - 24f, 19f),
                "STAFF ASSESSING", "row-name");
            operationState = AvStyled.Label(root, new Rect(x + 12f, y - 32f, w - 24f, 18f),
                "", "row-sub");
            operationForces = AvStyled.Label(root, new Rect(x + 12f, y - 53f, w - 24f, 18f),
                "", "row-sub");
            AvStyled.Button(root, new Rect(x + 12f, y - 74f, 125f, 20f), "OPEN ROOM", "btn",
                OpenCmdPlanning, AvButtonStyle.Default)
                .WithTooltip("Open the operations room and live theater map.");
            cancelOperation = AvStyled.Button(root,
                new Rect(x + w - 137f, y - 74f, 125f, 20f), "REPLAN", "btn",
                CancelCmdOperation, AvButtonStyle.Danger);
            cancelOperation.WithTooltip("Call off this operation and ask staff for new choices.");
            y -= 106f;

            y = SectionHeader(root, x, y, w, "STAFF PROPOSALS", "CHOOSE OR STAFF DECIDES", false);
            proposalNote = AvStyled.Label(root, new Rect(x, y, w, 16f), "", "row-sub");
            y -= 23f;
            for (int i = 0; i < ProposalSlots; i++)
            {
                int slot = i;
                proposalRoots[i] = new GameObject("Proposal" + i, typeof(RectTransform));
                RectTransform card = (RectTransform)proposalRoots[i].transform;
                card.SetParent(root, false);
                AvKit.Place(card, new Rect(x, y, w, 78f));
                AvKit.Panel(card, new Rect(0f, 0f, w, 76f), AvTheme.Surface);
                AvKit.Rule(card, new Rect(0f, 0f, 3f, 76f), AvTheme.RailReady);
                proposalNames[i] = AvStyled.Label(card, new Rect(12f, -7f, w - 91f, 18f),
                    "", "row-name");
                proposalTimes[i] = AvStyled.Label(card, new Rect(w - 81f, -7f, 69f, 18f),
                    "", "row-value", align: TextAlignmentOptions.MidlineRight);
                proposalBriefs[i] = AvStyled.Label(card, new Rect(12f, -30f, w - 24f, 38f),
                    "", "row-sub");
                proposalBriefs[i].enableWordWrapping = true;
                proposalButtons[i] = AvKit.HitButton(card,
                    new Rect(0f, 0f, w, 76f), () => PickCmdProposal(slot));
                proposalButtons[i].WithTooltip("Choose this staff proposal; the host validates the current offer.");
                y -= 83f;
            }
            y -= 5f;

            y = SectionHeader(root, x, y, w, "ACTIVE FRONTS", "MOST URGENT", false);
            frontsNote = AvStyled.Label(root, new Rect(x, y, w, 16f), "", "row-sub");
            y -= 24f;
            for (int i = 0; i < FrontSlots; i++)
            {
                frontNames[i] = AvStyled.Label(root,
                    new Rect(x + 10f, y, w - 20f, 16f), "", "row-name");
                frontStates[i] = AvStyled.Label(root,
                    new Rect(x + 10f, y - 19f, w - 20f, 16f), "", "row-sub");
                Divider(root, x, y - 38f, w);
                y -= 42f;
            }
            y -= 9f;

            y = SectionHeader(root, x, y, w, "STAFF POSTURE", "BROAD INTENT", false);
            postureButtons = new AvButton[3];
            string[] names = { "CAUTIOUS", "STEADY", "BOLD" };
            for (int i = 0; i < postureButtons.Length; i++)
            {
                TheaterWarPosture posture = (TheaterWarPosture)i;
                float bx = x + i * (w / 3f + 2f);
                postureButtons[i] = AvStyled.Button(root,
                    new Rect(bx, y, w / 3f - 5f, 25f), names[i], "btn",
                    () => SetCmdPosture(posture), AvButtonStyle.Default);
            }
            y -= 36f;
            warStatus = AvStyled.Label(root, new Rect(x, y, w, 20f), "", "row-sub");
        }

        private void RefreshCmd()
        {
            ITheaterWarView war = theaterWar;
            bool ready = war != null && war.Available;
            TheaterLiveOperationView active = ready ? war.ActiveOperation : null;
            operationName.text = active == null ? "NO PRIMARY OPERATION" : active.Label;
            operationState.text = active == null ? "Local clashes continue while the staff assesses openings."
                : active.Kind + " / " + active.Phase + " · " + active.Summary;
            operationForces.text = active == null ? "" :
                "GROUPS  " + active.GroundGroups + " GROUND  /  " +
                active.AirGroups + " AIR  /  " + active.NavalGroups + " NAVAL";
            cancelOperation.SetEnabled(ready && war.CanCommand && active != null);

            IReadOnlyList<TheaterProposalView> proposals = ready ? war.Proposals : null;
            int count = proposals != null ? Mathf.Min(proposals.Count, ProposalSlots) : 0;
            proposalNote.text = !ready ? "WAR PICTURE UNAVAILABLE" : count == 0
                ? "No decision pending. The staff will act on its own."
                : count + " OPENING" + (count == 1 ? "" : "S") + " · SELECT A PRIORITY";
            for (int i = 0; i < ProposalSlots; i++)
            {
                bool visible = i < count;
                if (proposalRoots[i].activeSelf != visible) proposalRoots[i].SetActive(visible);
                if (!visible) continue;
                TheaterProposalView proposal = proposals[i];
                proposalNames[i].text = proposal.Kind + " / " + proposal.Label;
                proposalBriefs[i].text = proposal.Brief + "  ·  " + proposal.Forces +
                    "  ·  RISK " + proposal.Risk;
                proposalTimes[i].text = Mathf.CeilToInt(Mathf.Max(0f, proposal.SecondsRemaining)) + "S";
                proposalButtons[i].SetEnabled(war.CanCommand);
            }

            IReadOnlyList<TheaterFrontView> fronts = ready ? war.Fronts : null;
            int frontCount = fronts != null ? Mathf.Min(fronts.Count, FrontSlots) : 0;
            frontsNote.text = !ready ? "NO VERIFIED FRONT REPORT" :
                (fronts?.Count ?? 0) + " FRONTS TRACKED";
            for (int i = 0; i < FrontSlots; i++)
            {
                TheaterFrontView front = i < frontCount ? fronts[i] : null;
                frontNames[i].text = front == null ? "—" : front.Label;
                frontStates[i].text = front == null ? "" : front.Observed
                    ? front.Status + " · PRESSURE " + Mathf.RoundToInt(Mathf.Clamp01(front.Pressure) * 100f) + "%"
                    : front.AgeSeconds < 0f ? "UNCONFIRMED REPORT"
                    : "UNCONFIRMED REPORT · AGE " + Mathf.RoundToInt(front.AgeSeconds) + "S";
            }
            for (int i = 0; i < 3; i++)
                postureButtons[i].SetLatched(ready && (int)war.Posture == i);
            warStatus.text = !ready ? "THEATER STAFF UNAVAILABLE" : war.CanCommand
                ? "STAFF SPENDS FROM THE FACTION POOL AUTOMATICALLY"
                : "OBSERVE / HOST VALIDATES ORDERS";
        }

        private void PickCmdProposal(int slot)
        {
            IReadOnlyList<TheaterProposalView> proposals = theaterWar?.Proposals;
            if (proposals == null || slot < 0 || slot >= proposals.Count) return;
            TheaterProposalView proposal = proposals[slot];
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
