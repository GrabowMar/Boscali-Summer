using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using BoscaliSummer.Modules.Support.Runtime;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    internal sealed partial class CrewOperationsView
    {
        private TMP_Text cyberInfocon, cyberWatch, cyberDossier, cyberSessionJournal, cyberPatchReadout, cyberTraceCaption, cyberIncidentWatch;
        private readonly TMP_Text[] cyberGateState = new TMP_Text[3];
        private readonly Image[] cyberGateLamps = new Image[3];
        private readonly CyberGateGraphic[] cyberGateCircuits = new CyberGateGraphic[3];
        private readonly AvFx[] cyberGateFx = new AvFx[3];
        private readonly bool[] cyberGateAccepted = new bool[3];
        private readonly Key[] cyberHomeKeys = new Key[CyberLocations.HomeSlots];
        private readonly Image[] cyberPatchLines = new Image[6];
        private readonly AvGaugeGraphic[] cyberPatchPorts = new AvGaugeGraphic[3];
        private AvGaugeGraphic cyberConditionRing;
        private AvLineGraphic cyberTraceHistory;
        private readonly float[] cyberTraceX = new float[64], cyberTraceY = new float[64];
        private int cyberTraceCount;
        private double cyberNextTraceSample;
        private bool cyberTraceActive;
        private int cyberTraceTarget = -1;
        private TMP_Text cyberHandshakeCaption;
        private AvGaugeGraphic cyberHandshake;

        private void BuildCyber(RectTransform root, float w, float h)
        {
            float watchW = Mathf.Clamp(w * .145f, 210, 252), labW = w * .345f;
            float mapX = watchW + 10, labX = w - labW, mapW = labX - mapX - 10;
            float journalY = h * .66f, mapH = journalY - 10, journalH = h - journalY - 48;
            Color phosphor = BranchAccent;

            CyberPane(root, 0, 0, watchW, 288, "WATCH FLOOR / INFOCON", AvIcon.ShieldLock);
            cyberConditionRing = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(14, -47, 92, 92), "InfoconCondition");
            cyberConditionRing.Shape = AvGaugeShape.Arc; cyberConditionRing.StartDeg = 225; cyberConditionRing.SweepDeg = 270;
            cyberConditionRing.Ticks = 30; cyberConditionRing.Thickness = 7;
            cyberConditionRing.Track = BranchEdge.WithAlpha(.45f); cyberConditionRing.FillColor = cyberConditionRing.FillEnd = phosphor;
            cyberConditionRing.TickColor = BranchDim; cyberConditionRing.raycastTarget = false;
            cyberInfocon = Mono(root, "--", 26, 69, 68, 47, 35, BranchInk);
            cyberInfocon.alignment = TextAlignmentOptions.Center;
            cyberWatch = Mono(root, "", 116, 46, watchW - 128, 93, 15, BranchDim);
            Mono(root, "INTRUSION / TRACE RECORDER", 14, 149, watchW - 28, 24, 14, phosphor);
            cyberTraceHistory = Chrome.Graphic<AvLineGraphic>(root, new Rect(14, -180, watchW - 42, 61), "ObservedTraceHistory");
            cyberTraceHistory.LineColor = RoomPaint.Command; cyberTraceHistory.Thickness = 1.8f;
            cyberTraceHistory.FillUnder = true; cyberTraceHistory.FillTop = RoomPaint.Command.WithAlpha(.16f);
            cyberTraceHistory.FillBottom = Color.clear; cyberTraceHistory.raycastTarget = false;
            for (int i = 0; i < 4; i++) Chrome.Rule(root, new Rect(14, -180 - i * 20, watchW - 28, .6f), BranchEdge.WithAlpha(.4f));
            Mono(root, "100", watchW - 38, 176, 29, 18, 11, BranchDim);
            Mono(root, "0", watchW - 22, 232, 15, 18, 11, BranchDim);
            cyberTraceCaption = Mono(root, "", 14, 253, watchW - 28, 25, 14, BranchDim);

            CyberPane(root, 0, 298, watchW, h - 346, "HOME NODE DIRECTORY", AvIcon.Database);
            for (int i = 0; i < cyberHomeKeys.Length; i++)
            {
                int slot = i;
                cyberHomeKeys[i] = Button(root, 8, 339 + i * 29, watchW - 16, 26, "HOME SLOT", () => { selectedNode = slot; dirty = true; }, false, 16);
            }
            Mono(root, "INCIDENTS / NATIVE NETWORK", 14, 612, watchW - 28, 25, 14, phosphor);
            cyberIncidentWatch = Mono(root, "", 14, 642, watchW - 28, Mathf.Max(52, h - 706), 15, BranchDim);

            CyberPane(root, mapX, 0, mapW, mapH, "THEATER TOPOLOGY / SECTOR CONTROL", AvIcon.Link);
            networkMap = new CyberNetmap();
            networkMap.Build(root, new Rect(mapX + 7, -36, mapW - 14, mapH - 83), slot => { selectedNode = slot; dirty = true; });
            if (support != null) networkMap.ResolveName = support.NameForNode;
            situation = Mono(root, "", mapX + 12, mapH - 38, mapW - 24, 30, 16, BranchDim);

            float journalW = mapW * .58f, patchX = mapX + journalW + 8, patchW = mapW - journalW - 8;
            CyberPane(root, mapX, journalY, journalW, journalH, "PROTOCOL ANALYSIS / FACTION JOURNAL", AvIcon.ListDetails);
            cyberSessionJournal = Mono(root, "", mapX + 12, journalY + 38, journalW - 24, journalH - 48, 15, phosphor);
            CyberPane(root, patchX, journalY, patchW, journalH, "PATCH MATRIX / PARALLEL GATES", AvIcon.ChartArrows);
            float rowH = (journalH - 92) / 3, hubY = journalY + 43 + rowH * 1.5f;
            Icon(root, AvIcon.ShieldLock, patchX + 15, hubY - 15, 29, phosphor);
            Icon(root, AvIcon.Database, patchX + patchW - 45, hubY - 15, 29, RoomPaint.Command);
            for (int i = 0; i < 3; i++)
            {
                float y = journalY + 43 + rowH * (i + .5f), portX = patchX + patchW * .47f;
                cyberPatchLines[i * 2] = Lines.Make(root, BranchEdge, null, "GateIngress");
                cyberPatchLines[i * 2 + 1] = Lines.Make(root, BranchEdge, null, "GateEgress");
                Lines.Set(cyberPatchLines[i * 2], patchX + 45, -hubY, portX - 14, -y, 1.2f);
                Lines.Set(cyberPatchLines[i * 2 + 1], portX + 14, -y, patchX + patchW - 45, -hubY, 1.2f);
                cyberPatchPorts[i] = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(portX - 11, -y + 11, 22, 22), "PatchPort");
                cyberPatchPorts[i].Shape = AvGaugeShape.Ring; cyberPatchPorts[i].Thickness = 3;
                cyberPatchPorts[i].Track = BranchEdge; cyberPatchPorts[i].FillColor = cyberPatchPorts[i].FillEnd = phosphor;
                cyberPatchPorts[i].raycastTarget = false;
                Mono(root, "0" + (i + 1), portX - 33, y - 10, 19, 20, 12, BranchDim);
            }
            cyberPatchReadout = Mono(root, "", patchX + 12, journalY + journalH - 31, patchW - 24, 25, 14, BranchDim);
            cyberHandshakeCaption = Mono(root, "", patchX + 12, journalY + journalH - 57, patchW - 24, 20, 12, BranchDim);
            cyberHandshake = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(patchX + 12, -journalY - journalH + 36, patchW - 25, 3), "DefenderResponseClock");
            cyberHandshake.Shape = AvGaugeShape.Segments; cyberHandshake.Segments = 18; cyberHandshake.SegmentGap = 1;
            cyberHandshake.Track = BranchEdge.WithAlpha(.4f); cyberHandshake.FillColor = cyberHandshake.FillEnd = RoomPaint.Command;
            cyberHandshake.raycastTarget = false;
            defenceWatch = Button(root, 0, h - 38, labX - 10, 34, "HOME NET / OPEN DEFENCE", () => SelectPage(2), false, 18);

            CyberPane(root, labX, 0, labW, 151, "TARGET SERVICE / OPERATION DOSSIER", AvIcon.Target);
            task = Text(root, "", labX + 13, 34, labW - 26, 42, 25, BranchInk);
            task.font = AvType.Face(AvFace.CondStrong);
            cyberDossier = Mono(root, "", labX + 13, 83, labW - 26, 60, 16, BranchDim);
            begin = Button(root, labX, 161, (labW - 8) / 2, 36, "QUIET INGRESS", () => support?.RequestCyberBreach(selectedNode, BreachTool.Quiet), false, 18);
            force = Button(root, labX + (labW + 8) / 2, 161, (labW - 8) / 2, 36, "FORCE INGRESS", () => support?.RequestCyberBreach(selectedNode, BreachTool.Force), false, 18);
            float busW = (labW - 16) / 3;
            for (int i = 0; i < 3; i++)
            {
                meters[i] = MakeMeter(root, labX + i * (busW + 8), 208, busW, i == 0 ? "TRACE" : i == 1 ? "COMPUTE" : "QUALITY", i == 0);
                if (i != 0) meters[i].Bar.FillColor = meters[i].Bar.FillEnd = phosphor;
            }
            scan = Button(root, labX, 284, (labW - 8) / 2, 34, "MAP SERVICES", () => support?.RequestCrewCyber(4), false, 18);
            scrub = Button(root, labX + (labW + 8) / 2, 284, (labW - 8) / 2, 34, "DISRUPT TRACE", () => support?.RequestCrewCyber(14), false, 18);
            for (int i = 0; i < 3; i++)
            {
                Capstone capstone = Capstones.All[i];
                payloads[i] = Button(root, labX + i * (busW + 8), 284, busW, 34, Capstones.Name(capstone), () => support?.RequestCyberChoice(capstone), false, 17);
            }
            float gateH = (h - 426) / 3, toolW = (labW - 32) / 3;
            for (int i = 0; i < 3; i++)
            {
                float y = 330 + i * (gateH + 6);
                CyberPane(root, labX, y, labW, gateH, "0" + (i + 1) + " / " + CyberNetwork.LinkName(i), i == 0 ? AvIcon.Lock : i == 1 ? AvIcon.Shield : AvIcon.Link);
                cyberGateState[i] = Mono(root, "", labX + labW * .56f, y + 7, labW * .44f - 14, 21, 14, BranchDim);
                cyberGateState[i].alignment = TextAlignmentOptions.TopRight;
                cyberGateCircuits[i] = Chrome.Graphic<CyberGateGraphic>(root, new Rect(labX + 11, -y - 33, 76, 29), "ProtocolGateCircuit");
                cyberGateCircuits[i].raycastTarget = false;
                cyberGateLamps[i] = Chrome.Rule(root, new Rect(labX + 3, -y - 35, 2, gateH - 44), BranchEdge);
                cyberGateFx[i] = AvFx.On(cyberGateLamps[i]);
                clues[i] = Text(root, "", labX + 100, y + 32, labW - 112, 30, 17, phosphor);
                for (int j = 0; j < 3; j++)
                {
                    int command = 5 + i * 3 + j;
                    gates[i * 3 + j] = Button(root, labX + 8 + j * (toolW + 8), y + 68, toolW, 33,
                        CyberNetwork.ToolName(j), () => support?.RequestCrewCyber(command), false, 17);
                }
                linkBars[i] = new AvStepProgress(root, "GateContinuity");
                Chrome.Place(linkBars[i].Rect, new Rect(labX + 9, -y - gateH + 6, labW - 21, 2));
                linkBars[i].Paint(phosphor, BranchEdge.WithAlpha(.3f));
            }
            outcome = Text(root, "", labX + 8, h - 74, labW - 16, 29, 15, RoomPaint.Ready);
            execute = Button(root, labX, h - 38, labW, 34, "RELEASE PAYLOAD TO TEAM", () => { if (packageAvailable) SelectPage(1); else support?.RequestCrewCyber(15); }, true, 19);
        }

        private void CyberPane(RectTransform root, float x, float y, float w, float h, string title, AvIcon icon)
        {
            RoomPaint.Inset(root, new Rect(x, -y, w - 3, h - 3), BranchSurface, BranchEdge);
            Plate(root, new Rect(x + 1, -y - 1, w - 5, 29), CyberStyle.Bar.WithAlpha(.68f), BranchEdge.WithAlpha(.5f), 2);
            Chrome.Rule(root, new Rect(x + 2, -y - 2, w - 7, 1), BranchAccent.WithAlpha(.42f));
            Icon(root, icon, x + 9, y + 7, 17, BranchAccent);
            Mono(root, title, x + 33, y + 6, w - 43, 24, 14, BranchInk);
            Chrome.Rule(root, new Rect(x + 8, -y - 30, w - 19, .65f), BranchEdge.WithAlpha(.55f));
        }

        private void PaintCyber(CyberNetwork c, double now)
        {
            if (c != null && (selectedNode < 0 || !c.Exists(selectedNode)))
            {
                selectedNode = c.BreachActive ? c.BreachTarget : c.AccessSlot;
                if (selectedNode < 0) for (int i = CyberNetwork.TargetBase; i < CyberNetwork.SlotCount; i++) if (c.Exists(i)) { selectedNode = i; break; }
            }
            networkMap.Layout(c, now, selectedNode, c != null && c.BreachActive ? c.BreachTarget : -1, ReducedMotion);
            bool active = c != null && c.BreachActive, ready = c != null && c.AccessRemaining(now) > 0; packageAvailable = ready;
            bool online = CyberWorkEnabled;
            task.text = active ? CyberWords.Callsign(c, c.BreachTarget) + " / SHARED INTRUSION"
                : ready ? CyberWords.Callsign(c, c.AccessSlot) + " / ACCESS ESTABLISHED"
                : c != null && selectedNode >= 0 ? CyberWords.Callsign(c, selectedNode) + " / " + (CyberLocations.IsHome(c.Node(selectedNode).Kind) ? CyberLocations.NodeName(c.Node(selectedNode).Kind) : CyberLocations.KindName(CyberLocations.LocationOf(c.Node(selectedNode).Kind))) : "NO TARGET / NETWORK OFFLINE";
            situation.text = c == null ? "TOPOLOGY UNAVAILABLE / AWAIT HOST" : "INGRESS " + (c.Reach / 1000).ToString("0.#") + " KM / CONTROL " + (ready ? OpsSectors.Code(c.Node(c.AccessSlot).X, c.Node(c.AccessSlot).Z) : "NONE") + " / INTEL " + Mathf.FloorToInt(c.Intel) + " / Q E SELECT";
            PaintCyberLab(c, now, active, ready);
            BreachDenial ingress = c?.CheckBreach(selectedNode, now) ?? BreachDenial.NoCommand;
            for (int mode = 0; mode < 2; mode++)
            {
                bool quiet = mode == 0; Key entry = quiet ? begin : force;
                float cost = CyberNetwork.CrewIngressCost(quiet), seconds = CyberNetwork.CrewIngressSeconds(quiet);
                bool funded = c != null && c.Computing + .001f >= cost;
                entry.Set((quiet ? "QUIET" : "FORCE") + " / " + cost.ToString("0.#") + " COMP / " + seconds + " S",
                    online && ingress == BreachDenial.None && funded, active && c.BreachQuiet == quiet,
                    (quiet ? "Low exposure / 15-second response window." : "10-second response window / computing and trace rise 60%.") + " / " + CyberWords.Refusal(!funded && ingress == BreachDenial.None ? BreachDenial.LowComputing : ingress));
            }
            BreachDenial profileDenial = c?.CheckCrewWork(4, c.WorkRevision, now) ?? BreachDenial.NoSession;
            BreachDenial scrubDenial = c?.CheckCrewWork(14, c.WorkRevision, now) ?? BreachDenial.NoSession;
            scan.Set(c != null && c.WorkAnalysis > 0 ? "SERVICES MAPPED / SHARED" : "PROFILE / " + (c?.CrewCost(4) ?? 12).ToString("0.#") + " COMP", online && profileDenial == BreachDenial.None, active && c.WorkAnalysis > 0,
                "Profile before solving gates to earn 2 quality per gate. A nearby earned EAVESDROP can supply this profile. / " + CyberWords.Refusal(profileDenial));
            scrub.Set("SCRUB TRACE / 16 COMP", online && scrubDenial == BreachDenial.None, false, "Remove 30% trace / 3-second recharge. " + CyberWords.Refusal(scrubDenial));

            bool choose = ready && c.BreachAwaitingChoice;
            scan.Control.gameObject.SetActive(!choose); scrub.Control.gameObject.SetActive(!choose);
            for (int i = 0; i < payloads.Length; i++)
            {
                payloads[i].Control.gameObject.SetActive(choose);
                payloads[i].Set(Capstones.Name(Capstones.All[i]), online && choose, false, Capstones.Summary(Capstones.All[i]) + " / One chosen faction payload shares this expiring access lease.");
            }
            for (int i = 0; i < 3; i++)
            {
                bool solved = active && c.LinkSolved(i);
                bool accepted = solved || ready, held = active && c.WorkRemaining(now) > 0;
                clues[i].text = ready ? "RELEASED / FACTION DELIVERY AUTHORIZED" : solved ? "VERIFIED / TEAM ROUTE SECURED" : active ? c.LinkClue(i) : "NO SESSION / AWAIT INGRESS";
                clues[i].color = accepted ? BranchAccent : active ? BranchInk : BranchDim;
                cyberGateState[i].text = ready ? "ACCESS SHARED" : solved ? "LINK ACCEPTED" : active ? held ? "RESPONSE HOLD" : c.WorkAnalysis > 0 ? "Q +2 ON MATCH" : "UNPROFILED" : "STANDBY";
                cyberGateState[i].color = accepted ? BranchAccent : held ? RoomPaint.Command : BranchDim;
                cyberGateLamps[i].color = accepted ? BranchAccent : held ? RoomPaint.Command : BranchEdge.WithAlpha(.6f);
                cyberGateCircuits[i].Paint(active, accepted, held, solved ? c.LinkSignature(i) : -1, BranchEdge.WithAlpha(.6f), BranchAccent, RoomPaint.Command);
                linkBars[i].SetProgress(accepted ? 1 : 0, 1);
                if (accepted && !cyberGateAccepted[i] && !ReducedMotion) cyberGateFx[i].Play(AvFxKind.Shine, .25f);
                else if (!accepted || ReducedMotion) cyberGateFx[i].Clear();
                cyberGateAccepted[i] = accepted;
                for (int j = 0; j < 3; j++)
                {
                    int command = 5 + i * 3 + j;
                    BreachDenial denial = c?.CheckCrewWork(command, c.WorkRevision, now) ?? BreachDenial.NoSession;
                    // A prediction cannot reveal an unprofiled gate's answer for free.
                    string trace = active && c.WorkAnalysis > 0 ? "+" + (c.CrewTrace(command) * 100).ToString("0.#") + "% trace" : "Unprofiled / trace uncertain";
                    gates[i * 3 + j].Set(CyberNetwork.ToolName(j) + " / " + (c?.CrewCost(command) ?? 18).ToString("0.#"), online && denial == BreachDenial.None, solved && j == c.LinkSignature(i), trace + " / " + CyberWords.Refusal(denial));
                }
            }
            BreachDenial release = c?.CheckCrewWork(15, c.WorkRevision, now) ?? BreachDenial.NoSession;
            execute.Set(ready ? "OPEN DELIVER / PAYLOAD READY" : active && c.CrewReleaseRemaining(now) > 0 ? "UPLINK HANDSHAKE / " + Mathf.CeilToInt((float)c.CrewReleaseRemaining(now)) + " S" : "RELEASE PAYLOAD TO TEAM", ready || online && release == BreachDenial.None, ready, CyberWords.Refusal(release));
            float unit = active ? c.CrewTrace(4) * 100 : 8;
            int projection = active ? Mathf.Min(6, c.WorkQuality + (3 - c.WorkProgress) * (c.WorkAnalysis > 0 ? 2 : 1)) : 0;
            outcome.text = ready ? "Q" + c.AccessQuality + " ACCESS / " + Mathf.CeilToInt(c.AccessRemaining(now)) + " S / ONE EFFECT" + (c.BreachAwaitingChoice ? " / CHOOSE PAYLOAD ABOVE" : " / DELIVER TO TEAM")
                : active && c.WorkRemaining(now) > 0 ? "DEFENDER RESPONSE / " + Mathf.CeilToInt((float)c.WorkRemaining(now)) + " S / REROUTE AFTER HOLD"
                : active ? "CEILING Q" + projection + " / MATCH +" + (unit * 1.5f).ToString("0.#") + "% / MISMATCH +" + (unit * 4).ToString("0.#") + "% TRACE"
                : "MAP SERVICES / MATCH GATES / SCRUB TRACE / RELEASE TO TEAM";
            if (!(support?.Settings?.EwEnabled.Value ?? true) || !(support?.Settings?.CyberEnabled.Value ?? true))
                outcome.text = "HOST CYBER OPS DISABLED / OFFENSIVE CONTRIBUTIONS UNAVAILABLE";
        }

        private void PaintCyberLab(CyberNetwork c, double now, bool active, bool ready)
        {
            cyberInfocon.text = c == null ? "--" : c.Infocon.ToString();
            cyberConditionRing.Value = c == null ? 0 : c.Infocon / 5f;
            cyberConditionRing.FillColor = cyberConditionRing.FillEnd = c != null && c.Infocon <= 2 ? RoomPaint.Command : BranchAccent;
            cyberWatch.text = c == null ? "NO LINK\nNO COMMAND" : "CONDITION " + c.Infocon + "\n" + c.Phase.ToString().ToUpperInvariant() + "\nHEAT " + Mathf.RoundToInt(c.Heat) + "%\nHARDEN " + c.UpgradeLevel(CyberUpgrade.Trace) + "/3";
            for (int i = 0; i < cyberHomeKeys.Length; i++)
            {
                bool home = c != null && c.Exists(i) && c.Node(i).Static;
                string state = home ? c.Node(i).Down ? "DOWN" : c.Node(i).Compromised ? "BREACHED" : c.Node(i).Isolated ? "ISOLATED" : c.Node(i).HoneypotUntil > now ? "BAITED" : "ONLINE" : "UNASSIGNED";
                cyberHomeKeys[i].Set(home ? CyberWords.Callsign(c, i) + " / " + state : "HOME " + (i + 1).ToString("00") + " / " + state, home, home && selectedNode == i,
                    home ? "Select home infrastructure / " + CyberWords.NodeState(c, i, now) + " / Open DEFENCE for validated countermeasures." : "Unused home-node capacity. Owned airbase infrastructure supplies this slot.");
            }
            int dossierSlot = active ? c.BreachTarget : ready ? c.AccessSlot : selectedNode;
            bool exists = c != null && c.Exists(dossierSlot);
            CyberNode node = exists ? c.Node(dossierSlot) : default;
            string siteName = exists ? support?.NameForNode(node.Kind, node.X, node.Z) : null;
            string siteKind = CyberLocations.IsHome(node.Kind) ? node.Kind == NodeKind.Command ? "COMMAND" : "HOME AIRBASE" : CyberLocations.KindName(CyberLocations.LocationOf(node.Kind));
            cyberDossier.text = exists ? "SITE " + (string.IsNullOrWhiteSpace(siteName) ? siteKind : PlaceNames.Shorten(siteName, 24)) + " / " + CyberWords.NodeState(c, dossierSlot, now) +
                "\nSECTOR " + OpsSectors.Code(node.X, node.Z) + (c.ControlsSector(node.X, node.Z, now) ? " / CONTROLLED" : " / LOCKED") + " / INGRESS " + (c.Reach / 1000).ToString("0.0") + " KM" +
                "\nPROJECT " + c.WorkRevision + " / " + (active ? c.WorkAnalysis > 0 ? "SERVICES MAPPED" : "SERVICES UNKNOWN" : ready ? "FACTION ACCESS SHARED" : CyberWords.Refusal(c.CheckBreach(dossierSlot, now)))
                : "NO VERIFIED NODE\nSelect a plotted network location.\nOwned infrastructure remains on the defence net.";

            string journal = c == null ? "NO HOST JOURNAL / WAIT FOR NETWORK" : "PROJECT " + c.WorkRevision + " / " + (active ? c.BreachQuiet ? "QUIET INGRESS" : "FORCE INGRESS" : ready ? "DELIVERY WINDOW" : "WATCH FLOOR") +
                "\nSERVICES " + (active ? c.WorkAnalysis > 0 ? "PROFILED" : "UNPROFILED" : "STANDBY") + " / GATES " + (active ? c.WorkProgress : ready ? 3 : 0) + "/3 VERIFIED" +
                "\nINTEL BANK " + Mathf.FloorToInt(c.Intel) + " / COMPUTE +" + c.ComputingIncome().ToString("0.0") + "/S";
            if (c != null)
                for (int age = Mathf.Min(2, c.NoticeCount) - 1; age >= 0; age--)
                {
                    string line = CyberWords.Notice(c.NoticeKind(age), CyberWords.Callsign(c, c.NoticeSite(age)), support?.CyberOriginName(c.NoticeOrigin(age)) ?? "ADVERSARY", c.NoticeOrigin(age));
                    if (!string.IsNullOrEmpty(line)) journal += "\n[N" + Mathf.Max(0, c.NoticeSerial - age) + "] " + line;
                }
            cyberSessionJournal.text = journal;
            string incident = c == null ? "HOST LINK ABSENT" : "ACTIVE " + c.ActiveIncidents(IncidentKind.None) + "/" + CyberNetwork.IncidentSlots + " / WON " + c.Defended + "\nADVERSARY WINS " + c.Breached;
            if (c != null)
                for (int i = 0; i < CyberNetwork.IncidentSlots; i++)
                    if (c.IncidentActive(i))
                    {
                        CyberIncident live = c.Incident(i);
                        incident += "\n" + CyberWords.IncidentCode(live.Kind) + " / " + CyberWords.Callsign(c, live.Site);
                        break;
                    }
            cyberIncidentWatch.text = incident;
            for (int i = 0; i < 3; i++)
            {
                bool connected = ready || active && c.LinkSolved(i);
                cyberPatchPorts[i].Value = connected ? 1 : 0;
                cyberPatchLines[i * 2].color = cyberPatchLines[i * 2 + 1].color = connected ? BranchAccent : BranchEdge.WithAlpha(.5f);
            }
            cyberPatchReadout.text = ready ? "TEAM LEASE / " + Mathf.CeilToInt(c.AccessRemaining(now)) + " S" : active ? "ROUTE " + c.WorkProgress + "/3 / REV " + c.WorkRevision : "NO ESTABLISHED ROUTE";
            float handshakeRemaining = active ? (float)c.CrewReleaseRemaining(now) : 0;
            float handshakeDuration = active ? CyberNetwork.CrewIngressSeconds(c.BreachQuiet) : 1;
            cyberHandshake.Value = active ? 1 - Mathf.Clamp01(handshakeRemaining / handshakeDuration) : ready ? 1 : 0;
            cyberHandshake.FillColor = cyberHandshake.FillEnd = ready ? BranchAccent : RoomPaint.Command;
            cyberHandshakeCaption.text = ready ? "HANDSHAKE COMPLETE / TEAM ACCESS" : active ? handshakeRemaining > 0 ? "DEFENDER WINDOW / " + Mathf.CeilToInt(handshakeRemaining) + " S" : "HANDSHAKE COMPLETE / SOLVE GATES" : "RESPONSE CLOCK / NO SESSION";
            PaintCyberTrace(c, now, active);
        }

        private void PaintCyberTrace(CyberNetwork c, double now, bool active)
        {
            if (!active)
            {
                cyberTraceActive = false;
                cyberTraceCaption.text = cyberTraceCount > 0 ? "HELD / " + cyberTraceCount + " LOCAL SAMPLES" : "NO OBSERVED TRACE SAMPLES";
                return;
            }
            if (!cyberTraceActive || cyberTraceTarget != c.BreachTarget)
            { cyberTraceCount = 0; cyberNextTraceSample = now; cyberTraceTarget = c.BreachTarget; }
            cyberTraceActive = true;
            if (now >= cyberNextTraceSample)
            {
                cyberNextTraceSample = now + .5;
                if (cyberTraceCount == cyberTraceY.Length)
                { for (int i = 1; i < cyberTraceCount; i++) cyberTraceY[i - 1] = cyberTraceY[i]; cyberTraceCount--; }
                cyberTraceY[cyberTraceCount++] = c.BreachTrace;
                if (cyberTraceCount == 1) { cyberTraceX[0] = 0; cyberTraceX[1] = 1; cyberTraceY[1] = cyberTraceY[0]; cyberTraceHistory.SetPoints(cyberTraceX, cyberTraceY, 2); }
                else
                { for (int i = 0; i < cyberTraceCount; i++) cyberTraceX[i] = i / (float)(cyberTraceCount - 1); cyberTraceHistory.SetPoints(cyberTraceX, cyberTraceY, cyberTraceCount); }
            }
            cyberTraceHistory.LineColor = c.BreachTrace >= .7f ? AvTheme.RailDanger : RoomPaint.Command;
            cyberTraceHistory.FillTop = cyberTraceHistory.LineColor.WithAlpha(.16f);
            cyberTraceCaption.text = Mathf.RoundToInt(c.BreachTrace * 100) + "% / " + cyberTraceCount + " LOCAL SAMPLES";
        }

    }
}
