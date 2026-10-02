using NOAvionics;
using System;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Window;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    internal sealed partial class CrewOperationsView
    {
        private readonly TMP_Text[] fieldRosterDetails = new TMP_Text[4];
        private readonly TMP_Text[] fieldRosterStates = new TMP_Text[4], fieldRosterTargets = new TMP_Text[4], fieldRosterRoles = new TMP_Text[4];
        private readonly Image[] fieldRosterPortraits = new Image[4];
        private readonly Image[] fieldRosterPins = new Image[4];
        private readonly AvGaugeGraphic[] fieldPhaseBars = new AvGaugeGraphic[4];
        private TMP_Text fieldRosterSummary, fieldSharedSupport, fieldObjectiveName, fieldObjectiveRegister, fieldObjectiveIntel;
        private TMP_Text fieldObjectiveConfidence, fieldDossierName, fieldDossierState, fieldQualityBadge;
        private readonly TMP_Text[] fieldQualityRules = new TMP_Text[3];
        private AvGaugeGraphic fieldQualityPips;
        private TMP_Text fieldRouteReadback, fieldPackageEffect, fieldOrderBrief;
        private FieldMission fieldPreviewMission;
        private bool fieldCompact;
        private bool fieldRosterShowsTarget;
        private int fieldReportLimit;
        private Rect fieldOrderArea;
        private readonly float[] fieldHomeX = new float[DeskMap.Homes], fieldHomeZ = new float[DeskMap.Homes];
        private readonly string[] fieldHomeName = new string[DeskMap.Homes];

        private void BuildField(RectTransform root, float w, float h)
        {
            // The field desk keeps personnel, real terrain and deployment instruments separate.
            // Portraits describe personnel, never observed contacts or simulated squad health.
            fieldCompact = h < 650;
            fieldObjectiveConfidence = fieldDossierName = fieldDossierState = fieldQualityBadge = null;
            fieldQualityPips = null;
            Array.Clear(fieldQualityRules, 0, fieldQualityRules.Length);
            float lw = Mathf.Clamp(w * .18f, 180, 306), rw = Mathf.Clamp(w * .285f, 330, 480);
            lw = Mathf.Min(lw, w - rw - 620);
            float mx = lw + 10, mw = w - lw - rw - 20, rx = mx + mw + 10;
            float rowH = Mathf.Clamp((h - 312) / 4, 56, 86), rosterY = 65;
            fieldRosterShowsTarget = rowH >= 76;
            Instrument(root, 0, 0, lw, rosterY + 4 * (rowH + 6) - 6, "TEAM REGISTER", AvIcon.UsersGroup);
            fieldRosterSummary = Mono(root, "", 10, 39, lw - 20, 20, 12, BranchDim);
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                float y = rosterY + i * (rowH + 6);
                teams[i] = FieldKey(root, 5, y, lw - 10, rowH, FieldWords.Callsign(i), () => { selectedTeam = slot; dirty = true; });
                RectTransform host = teams[i].Control.Rect;
                float portraitSize = Mathf.Min(46, rowH - 16), textX = portraitSize + 17, textW = lw - textX - 24;
                var portrait = new Rect(8, -8, portraitSize, portraitSize);
                Chrome.Panel(host, portrait, BranchSurface);
                fieldRosterPortraits[i] = OpsArtwork.Draw(host, portrait, 4 + i);
                Chrome.Outline(host, portrait, BranchEdge.WithAlpha(.5f));
                RoomPaint.Brackets(host, portrait, 5, BranchInk.WithAlpha(.65f));
                fieldRosterPins[i] = Chrome.Rule(host, new Rect(3, -8, 2, rowH - 16), BranchEdge);
                Chrome.Place(teams[i].Text.rectTransform, new Rect(textX, -5, textW, 21));
                Chrome.SetSize(teams[i].Text, fieldCompact ? 13 : 16);
                fieldRosterStates[i] = Mono(host, "", textX, 26, textW, 17, fieldCompact ? 11 : 13, BranchInk);
                fieldRosterTargets[i] = Mono(host, "", textX, 46, textW, 15, 11, BranchDim);
                fieldRosterTargets[i].gameObject.SetActive(fieldRosterShowsTarget);
                fieldRosterRoles[i] = Mono(host, "", 8, portraitSize + 11, portraitSize + 4, 18, 10, BranchDim);
                fieldRosterRoles[i].gameObject.SetActive(rowH >= 74);
                fieldRosterDetails[i] = Mono(host, "", textX, rowH - 22, textW, 14, 11, BranchDim);
                fieldPhaseBars[i] = Chrome.Graphic<AvGaugeGraphic>(host, new Rect(textX, -rowH + 5, textW, 3), "TeamPhaseClock");
                fieldPhaseBars[i].Shape = AvGaugeShape.Segments; fieldPhaseBars[i].Segments = 12; fieldPhaseBars[i].SegmentGap = 2;
                fieldPhaseBars[i].Track = BranchEdge.WithAlpha(.25f);
                fieldPhaseBars[i].FillColor = fieldPhaseBars[i].FillEnd = BranchAccent; fieldPhaseBars[i].raycastTarget = false;
            }
            float contextY = rosterY + 4 * (rowH + 6) + 6, contextH = Mathf.Clamp(h * .165f, 82, 130);
            Instrument(root, 0, contextY, lw, contextH, "SHARED ASSETS", AvIcon.Antenna);
            fieldSharedSupport = Mono(root, "", 12, contextY + 40, lw - 24, contextH - 46, fieldCompact ? 11 : 13, BranchInk);
            float reportY = contextY + contextH + 8, reportH = h - reportY - 45;
            Instrument(root, 0, reportY, lw, reportH, "FIELD REPORT", AvIcon.Radio);
            situation = Text(root, "", 12, reportY + 39, lw - 24, reportH - 45, fieldCompact ? 11 : 13, BranchDim);
            fieldReportLimit = Mathf.Max(48, Mathf.FloorToInt((lw - 24) / (fieldCompact ? 5.5f : 6.5f)) * Mathf.Max(1, Mathf.FloorToInt((reportH - 45) / (fieldCompact ? 14 : 16))));
            FieldKey(root, 0, h - 35, (lw - 6) / 2, 35, "EFFECTS >", () => SelectPage(1));
            FieldKey(root, (lw + 6) / 2, h - 35, (lw - 6) / 2, 35, "SUPPLY >", () => SelectPage(2));

            float registerH = Mathf.Clamp(h * .19f, 104, 150), mapH = h - registerH - 10, registerY = mapH + 10;
            Instrument(root, mx, 0, mw, mapH, "OPERATIONS THEATER / CONTROL GRID", AvIcon.MapPin);
            fieldMap = new DeskMap();
            Rect map = new Rect(mx + 8, -40, mw - 16, mapH - 48);
            fieldMap.Build(root, map, map, slot =>
            { selectedObjective = slot; selectedAnchor = detachment?.Objective(slot).Anchor ?? 0; dirty = true; });
            Instrument(root, mx, registerY, mw, registerH, "TARGET REGISTER", AvIcon.Target);
            FieldKey(root, mx + mw - 80, registerY + 4, 32, 25, "Q <", () => CycleObjective(-1));
            FieldKey(root, mx + mw - 42, registerY + 4, 32, 25, "E >", () => CycleObjective(1));
            float divider = mw * .56f;
            Chrome.Rule(root, new Rect(mx + divider, -registerY - 42, 1, registerH - 54), BranchEdge.WithAlpha(.45f));
            fieldObjectiveName = Text(root, "", mx + 14, registerY + 41, divider - 26, 31, fieldCompact ? 19 : 24, BranchInk);
            fieldObjectiveName.font = AvType.Face(AvFace.CondStrong);
            fieldObjectiveRegister = Mono(root, "", mx + 14, registerY + 76, divider - 26, registerH - 82, 12, BranchDim);
            float intelY = registerH >= 130 ? 63 : 42;
            if (registerH >= 130)
                fieldObjectiveConfidence = Mono(root, "", mx + divider + 13, registerY + 42, mw - divider - 27, 17, 11, BranchAccent);
            fieldObjectiveIntel = Mono(root, "", mx + divider + 13, registerY + intelY, mw - divider - 27, registerH - intelY - 6, fieldCompact ? 11 : 13, BranchInk);

            float dossierH = Mathf.Clamp(h * .175f, 100, 146), portraitH = Mathf.Clamp(dossierH - 49, 50, 92);
            Instrument(root, rx, 0, rw, dossierH, "DEPLOYMENT DOSSIER", AvIcon.Flag);
            Chrome.Panel(root, new Rect(rx + 9, -38, portraitH + 4, portraitH + 4), BranchSurface);
            for (int i = 0; i < 4; i++) portraits[i] = OpsArtwork.Draw(root, new Rect(rx + 11, -40, portraitH, portraitH), 4 + i);
            Chrome.Outline(root, new Rect(rx + 11, -40, portraitH, portraitH), BranchEdge.WithAlpha(.6f));
            RoomPaint.Brackets(root, new Rect(rx + 9, -38, portraitH + 4, portraitH + 4), 8, BranchAccent.WithAlpha(.7f), 2);
            if (dossierH >= 126)
            {
                fieldDossierName = Text(root, "", rx + portraitH + 24, 39, rw - portraitH - 36, 26, 21, BranchInk);
                fieldDossierName.font = AvType.Face(AvFace.CondStrong);
                Chrome.Panel(root, new Rect(rx + portraitH + 20, -dossierH + 29, rw - portraitH - 32, 20), BranchPane);
                fieldDossierState = Mono(root, "", rx + portraitH + 25, dossierH - 29, rw - portraitH - 42, 20, 12, BranchAccent);
            }
            task = Text(root, "", rx + portraitH + 24, fieldDossierName != null ? 68 : 40, rw - portraitH - 36,
                fieldDossierName != null ? dossierH - 102 : dossierH - 46, fieldCompact ? 14 : 17, BranchInk);
            task.font = AvType.Face(AvFace.CondStrong);
            float metricY = dossierH + 8, metricW = (rw - 12) / 3;
            for (int i = 0; i < 3; i++) meters[i] = FieldMeter(root, rx + i * (metricW + 6), metricY, metricW,
                i == 0 ? "PREPARATION" : i == 1 ? "INTELLIGENCE" : "EXPOSURE", i == 2);
            float routeY = metricY + 63;
            Plate(root, new Rect(rx, -routeY, rw, 39), BranchPane, BranchEdge, 2);
            fieldRouteReadback = Mono(root, "", rx + 10, routeY + 4, rw - 20, 21, 12, BranchDim);
            routeBar = new AvStepProgress(root, "FieldRouteProgress");
            Chrome.Place(routeBar.Rect, new Rect(rx + 10, -routeY - 29, rw - 20, 7)); routeBar.Paint(BranchAccent, BranchEdge.WithAlpha(.3f));
            for (int leg = 1; leg < 3; leg++)
                Chrome.Panel(root, new Rect(rx + 10 + (rw - 20) * leg / 3 - 2, -routeY - 29, 4, 7), BranchPane);
            float orderY = routeY + 49, orderH = Mathf.Clamp(h * .25f, 146, 202), orderBody = orderH - 24;
            fieldOrderArea = new Rect(rx + 10, -orderY - 24, rw - 20, orderBody);
            Plate(root, new Rect(rx, -orderY, rw, orderH), BranchSurface, BranchEdge, 2);
            Mono(root, "APPROACH ORDERS / SHARED CONTRIBUTIONS", rx + 10, orderY + 4, rw - 20, 18, 12, BranchAccent);
            float missionH = (orderBody - 9) / 4;
            for (int i = 0; i < 4; i++)
            {
                int mission = i;
                missions[i] = FieldKey(root, rx + 5, orderY + 24 + i * (missionH + 3), rw - 10, missionH, "", () => Launch((FieldMission)mission));
                Key key = missions[i];
                key.Control.Changed = control => { key.Paint(); if (control.Hovered || control.Focused) { fieldPreviewMission = (FieldMission)mission; dirty = true; } };
            }
            raise = FieldKey(root, rx + 5, orderY + 24, rw - 10, 45, "FORM TEAM", () => support?.RequestSpecOpsRaise(selectedTeam));
            float assistH = fieldCompact ? 36 : 44, routeH = (orderBody - assistH - 12) / 2;
            fieldOrders[0] = FieldKey(root, rx + 5, orderY + 24, (rw - 16) / 2, assistH, "SCOUT", () => support?.RequestCrewField(selectedTeam, 5));
            fieldOrders[1] = FieldKey(root, rx + (rw + 6) / 2, orderY + 24, (rw - 16) / 2, assistH, "COVER", () => support?.RequestCrewField(selectedTeam, 6));
            fieldOrders[2] = FieldKey(root, rx + 5, orderY + 30 + assistH, rw - 10, routeH, "FAST ROUTE", () => support?.RequestCrewField(selectedTeam, 7));
            fieldOrders[3] = FieldKey(root, rx + 5, orderY + 36 + assistH + routeH, rw - 10, routeH, "COVERED ROUTE", () => support?.RequestCrewField(selectedTeam, 8));
            fieldOrderBrief = Text(root, "", fieldOrderArea.x, -fieldOrderArea.y, fieldOrderArea.width, fieldOrderArea.height, fieldCompact ? 13 : 17, BranchDim);
            float adviceY = h - 115, packageY = orderY + orderH + 8, packageH = adviceY - packageY - 8;
            if (!fieldCompact)
            {
                Instrument(root, rx, packageY, rw, packageH, "PACKAGE AUTHORIZATION", AvIcon.Shield);
                outcome = Mono(root, "", rx + 12, packageY + 41, rw - 110, Mathf.Min(66, packageH - 45), 14, BranchInk);
                Chrome.Panel(root, new Rect(rx + rw - 88, -packageY - 41, 74, 58), BranchSurface);
                Chrome.Outline(root, new Rect(rx + rw - 88, -packageY - 41, 74, 58), BranchEdge.WithAlpha(.5f));
                fieldQualityBadge = Text(root, "", rx + rw - 81, packageY + 42, 60, 38, 30, BranchAccent);
                fieldQualityBadge.font = AvType.Face(AvFace.CondStrong);
                fieldQualityPips = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(rx + rw - 80, -packageY - 85, 57, 6), "FieldPackageQuality");
                fieldQualityPips.Shape = AvGaugeShape.Segments; fieldQualityPips.Segments = 4; fieldQualityPips.SegmentGap = 3;
                fieldQualityPips.Track = BranchEdge.WithAlpha(.25f); fieldQualityPips.FillColor = fieldQualityPips.FillEnd = BranchAccent; fieldQualityPips.raycastTarget = false;
                if (packageH >= 164)
                {
                    fieldPackageEffect = Text(root, "", rx + 12, packageY + 111, rw - 24, packageH - 150, 12, BranchDim);
                    for (int tier = 0; tier < 3; tier++)
                    {
                        float col = (rw - 24) / 3;
                        fieldQualityRules[tier] = Mono(root, "", rx + 12 + tier * col, packageY + packageH - 28, col - 5, 16, 10, BranchDim);
                    }
                }
            }
            else outcome = Mono(root, "", rx + 5, packageY, rw - 10, packageH, 12, BranchInk);
            Chrome.Rule(root, new Rect(rx + 5, -adviceY, rw - 10, 1), BranchAccent.WithAlpha(.5f));
            clues[0] = Text(root, "", rx + 5, adviceY + 6, rw - 10, 49, fieldCompact ? 12 : 14, BranchDim);
            fieldOrders[4] = FieldKey(root, rx, h - 54, (rw - 8) / 2, 54, "EXECUTE", () => support?.RequestCrewField(selectedTeam, 0), true);
            fieldOrders[5] = FieldKey(root, rx + (rw + 8) / 2, h - 54, (rw - 8) / 2, 54, "EXTRACT", () => support?.RequestCrewField(selectedTeam, 1));
        }

        private Key FieldKey(RectTransform root, float x, float y, float w, float h, string title, Action action, bool primary = false)
        {
            Key key = Button(root, x, y, w, h, title, action, primary, fieldCompact ? 12 : 14);
            key.Text.font = AvType.Face(AvFace.CondStrong);
            Chrome.Place(key.Text.rectTransform, new Rect(9, -2, w - 18, h - 4));
            return key;
        }

        private Meter FieldMeter(RectTransform root, float x, float y, float w, string title, bool caution)
        {
            Plate(root, new Rect(x, -y, w, 55), BranchPane, BranchEdge.WithAlpha(.7f), 2);
            Mono(root, title, x + 8, y + 5, w - 16, 17, fieldCompact ? 10 : 12, BranchDim);
            var meter = new Meter { Value = Mono(root, "--", x + 8, y + 26, 57, 23, 18, caution ? BranchAccent : BranchInk) };
            meter.Bar = Chrome.Graphic<AvGaugeGraphic>(root, new Rect(x + 62, -y - 35, w - 72, 8), "FieldReadiness");
            meter.Bar.Shape = AvGaugeShape.Segments; meter.Bar.Segments = 8; meter.Bar.SegmentGap = 2;
            meter.Bar.Track = BranchEdge.WithAlpha(.25f); meter.Bar.FillColor = meter.Bar.FillEnd = caution ? BranchAccent : BranchInk;
            meter.Bar.raycastTarget = false; return meter;
        }

        private void PaintField(SpecOpsDetachment d, double now)
        {
            if (d != null && selectedAnchor != 0) selectedObjective = d.SlotOf(selectedAnchor);
            if (d != null && (selectedObjective < 0 || selectedObjective >= d.ObjectiveCount)) selectedObjective = 0;
            FieldObjective objective = d != null ? d.Objective(selectedObjective) : default;
            selectedAnchor = objective.Anchor;
            FieldTeam team = d != null ? d.Team(selectedTeam) : default;
            if (support != null)
                fieldMap.SetHomes(fieldHomeX, fieldHomeZ, fieldHomeName, support.FillFieldHomes(fieldHomeX, fieldHomeZ, fieldHomeName));
            fieldMap.Layout(d, now, selectedObjective, Vector2.zero, false);
            bool live = d != null && d.Enabled;
            fieldRosterSummary.text = d == null ? "AWAITING FIELD NET" : d.Formed + " FORMED / " + d.Count(TeamState.Ready) + " READY / " + d.Count(TeamState.Holding) + " HELD";
            for (int i = 0; i < 4; i++)
            {
                FieldTeam other = d != null ? d.Team(i) : default;
                bool expired = other.State == TeamState.Holding && now >= other.PhaseEnd;
                string state = expired ? "POST EXPIRED" : FieldWords.State(other.State);
                string detail = other.Deployed ? other.State == TeamState.Holding ? "Q" + other.Quality + " / " + other.Charges + " CHG / " + FieldWords.Clock(d.Remaining(i, now)) : "LEG " + other.RouteStep + "/3 / " + FieldWords.Clock(d.Remaining(i, now)) : FieldWords.Rank(other.Rank) + " / " + other.Wins + " WINS";
                Color tone = live && !expired ? FieldStateTone(other.State) : BranchDim;
                teams[i].Set(((char)('A' + i)) + " / " + FieldWords.Callsign(i), true, i == selectedTeam, d == null ? "Awaiting the faction detachment." : FieldWords.TeamLine(other, d.Remaining(i, now)));
                fieldRosterStates[i].text = state;
                fieldRosterStates[i].color = tone;
                fieldRosterTargets[i].text = other.Deployed ? other.Target ?? "HOST OBJECTIVE" : other.Formed ? "FACTION RESERVE" : "NO PERSONNEL";
                fieldRosterRoles[i].text = other.Deployed ? FieldWords.Mission(other.Mission) : other.Formed ? "SOF" : "--";
                fieldRosterPins[i].color = tone;
                fieldRosterPins[i].enabled = other.Formed;
                fieldRosterDetails[i].text = other.Formed ? detail : "EMPTY / FORM TEAM";
                fieldRosterDetails[i].color = i == selectedTeam ? BranchInk : BranchDim;
                float phase = other.Deployed || other.State == TeamState.Recovering ? d?.Progress(i, now) ?? 0 : 0;
                fieldPhaseBars[i].Value = other.State == TeamState.Holding ? 1 - phase : phase;
                fieldPhaseBars[i].FillColor = fieldPhaseBars[i].FillEnd = tone;
                fieldPhaseBars[i].SetVerticesDirty();
                fieldRosterPortraits[i].enabled = other.Formed && fieldRosterPortraits[i].sprite != null;
            }
            int supplier = live ? d.FindMarket(now) : -1;
            double scouting = d?.ScoutRemaining(objective.Anchor, now) ?? 0;
            fieldSharedSupport.text = "POSTS " + (d?.Count(TeamState.Holding) ?? 0) + "/" + FieldCatalog.MaximumHeldPosts + " / GROUND " + (d?.GroundReadiness ?? 1) + "\n" +
                "RECON " + (scouting > 0 ? FieldWords.Clock(scouting) + " MEMORY" : "NO SCOUT MEMORY") + "\n" +
                (supplier >= 0 ? "SUPPLIER " + FieldWords.Callsign(supplier) + " / " + d.Team(supplier).Charges + " CHG" : "SUPPLIER CLOSED / NEED Q2 STEAL");
            situation.text = FieldExcerpt(FieldReport(team, objective), fieldReportLimit);
            bool objectiveKnown = d != null && d.IntelKnown(objective.Anchor, now) && objective.Threat != byte.MaxValue && objective.Radars != byte.MaxValue;
            if (fieldObjectiveConfidence != null)
            {
                fieldObjectiveConfidence.text = objectiveKnown ? "DEFENDER PICTURE / VERIFIED" : "DEFENDER PICTURE / UNCONFIRMED";
                fieldObjectiveConfidence.color = objectiveKnown ? RoomPaint.Ready : BranchAccent;
            }
            fieldObjectiveName.text = objective.Name ?? "SELECT AN OBJECTIVE";
            fieldObjectiveRegister.text = objective.Anchor == 0 ? "NO HOST OBJECTIVE" : (objective.Friendly ? "FRIENDLY" : objective.Hostile ? "HOSTILE" : "NEUTRAL") + " / " + FieldWords.Kind(objective.Kind) +
                "\nSECTOR " + OpsSectors.Code(objective.X, objective.Z) + (d.ControlsSector(objective.X, objective.Z, now) ? " / CONTROLLED" : " / LOCKED");
            fieldObjectiveIntel.text = objective.Anchor == 0 ? "GRID -- / --\nDEFENDERS UNKNOWN\nEMITTERS UNKNOWN\nRECON REQUIRED" : "GRID " + TheaterGrid.Kilometres(objective.X, objective.Z) + "\nDEFENDERS " + (objectiveKnown ? objective.Threat.ToString() : "UNKNOWN") + " / 2 KM\nEMITTERS " + (objectiveKnown ? objective.Radars.ToString() : "UNKNOWN") + " / 2.5 KM\nRECON " + (scouting > 0 ? FieldWords.Clock(scouting) : objectiveKnown ? "PICTURE KNOWN" : "REQUIRED");

            bool deciding = team.State == TeamState.Deciding, planning = team.State == TeamState.Ready;
            bool orderWindow = live && deciding && now < team.PhaseEnd;
            string target = team.Deployed ? team.Target ?? "OBJECTIVE" : objective.Name ?? "SELECT OBJECTIVE";
            string missionLine = team.Deployed ? FieldWords.Mission(team.Mission) + " / " + (deciding ? "LEG " + Math.Min(3, team.RouteStep + 1) + " OF 3" : FieldWords.Clock(d.Remaining(selectedTeam, now))) : FieldWords.Rank(team.Rank) + " / " + team.Wins + " WINS";
            if (fieldDossierName != null)
            {
                bool expired = team.State == TeamState.Holding && now >= team.PhaseEnd;
                fieldDossierName.text = FieldWords.Callsign(selectedTeam) + " / " + (team.Formed ? FieldWords.Rank(team.Rank) : "NO PERSONNEL");
                fieldDossierState.text = "STATUS / " + (expired ? "POST EXPIRED" : FieldWords.State(team.State));
                fieldDossierState.color = live && !expired ? FieldStateTone(team.State) : BranchDim;
                task.text = target + "\n" + missionLine;
            }
            else task.text = FieldWords.Callsign(selectedTeam) + " / " + FieldWords.State(team.State) + "\n" + target + "\n" + missionLine;
            if (!team.Deployed)
            {
                for (int i = 0; i < 3; i++) meters[i].Set(0, 100, "--");
                routeBar.SetProgress(0, 3);
            }
            fieldRouteReadback.text = team.State == TeamState.Holding ? "POST WINDOW <= " + FieldWords.Clock(SpecOpsDetachment.PostPressureWindow(team, now)) + " / " + team.Charges + " CHARGES"
                : deciding ? "APPROACH " + team.RouteStep + "/3 / " + (now < team.OrderReadyAt ? "ACK " + FieldWords.Clock(team.OrderReadyAt - now) : "WINDOW " + FieldWords.Clock(d.Remaining(selectedTeam, now)))
                : team.Deployed ? "INSERTION / " + FieldWords.Clock(d.Remaining(selectedTeam, now)) : "APPROACH / THREE LEGS BEFORE EXECUTION";
            for (int i = 0; i < 4; i++)
            {
                missions[i].Control.gameObject.SetActive(planning);
                var mission = (FieldMission)i;
                var denial = d != null ? d.CheckLaunch(selectedTeam, mission, objective.Anchor) : SpecOpsDenial.Disabled;
                float price = support?.SpecOpsMissionCost(mission) ?? FieldCatalog.MissionCost(mission);
                string reason = denial != SpecOpsDenial.None ? FieldShortDenial(denial) : !Affordable(price) ? "LOW OPS" : FieldCatalog.PostSummary(mission, 0);
                missions[i].Set((i + 1) + " / " + FieldWords.Mission(mission) + " / " + price + " OPS" + (fieldCompact ? " / " : "\n") + reason,
                    CanSend && denial == SpecOpsDenial.None && Affordable(price), mission == fieldPreviewMission,
                    (denial != SpecOpsDenial.None ? FieldWords.Denial(denial) : !Affordable(price) ? "LOW OPS RESERVE" : FieldWords.BriefEffect(mission, 0)) + " / Complete three approach legs. Preparation, intel and real pressure determine post quality.");
            }
            raise.Control.gameObject.SetActive(team.State == TeamState.Unformed);
            SpecOpsDenial raiseDenial = d?.CheckRaise(selectedTeam) ?? SpecOpsDenial.Disabled;
            float raisePrice = support?.SpecOpsRaiseCost() ?? FieldCatalog.RaiseCost;
            raise.Set("FORM " + FieldWords.Callsign(selectedTeam) + " / " + raisePrice + " OPS", CanSend && raiseDenial == SpecOpsDenial.None && Affordable(raisePrice), false,
                raiseDenial != SpecOpsDenial.None ? FieldWords.Denial(raiseDenial) : !Affordable(raisePrice) ? "LOW OPS RESERVE" : "Form a permanent faction team in this empty slot.");
            fieldOrderBrief.gameObject.SetActive(!planning && !deciding);
            Rect brief = fieldOrderArea;
            if (team.State == TeamState.Unformed) { brief.y -= 54; brief.height -= 54; }
            Chrome.Place(fieldOrderBrief.rectTransform, brief);
            fieldOrderBrief.text = team.State == TeamState.Holding ? FieldWords.Post(team.Mission) + " ESTABLISHED\n" + FieldCatalog.PostSummary(team.Mission, team.Quality) + "\nUse EFFECTS within sector " + OpsSectors.Code(team.X, team.Z) + "."
                : team.State == TeamState.EnRoute ? "FROM " + FieldWords.Origin(team) + "\nAircrew recon can supply this insertion. Clear defenders before the team arrives."
                : team.State == TeamState.OnTask ? "PACKAGE DELIVERY / " + FieldWords.Clock(d.Remaining(selectedTeam, now)) + "\nSuppress hostile defenders to preserve the post."
                : team.State == TeamState.Unformed ? "Permanent shared team slot.\nRank grows through successful hostile operations."
                : "RECOVERY / " + FieldWords.Clock(d?.Remaining(selectedTeam, now) ?? 0) + "\nPreserve teams by extracting before the site is compromised.";
            for (int i = 0; i < 4; i++) fieldOrders[i].Control.gameObject.SetActive(deciding);
            string assistanceDenial = !live ? "DETACHMENT DISABLED" : now >= team.PhaseEnd ? "DECISION WINDOW EXPIRED" : team.RouteStep >= 3 ? "ROUTE COMPLETE / EXECUTE OR EXTRACT" : null;
            fieldOrders[0].Set("SCOUT / " + (support?.CrewAssistancePrice(5) ?? 100) + " OPS\n" + ((team.CrewSupport & 1) != 0 ? "ASSIGNED / THIS LEG" : "INTEL +22 / LESS EXPOSURE"), AffordableAssistance(5) && CanSend && orderWindow && team.RouteStep < 3 && (team.CrewSupport & 1) == 0, (team.CrewSupport & 1) != 0,
                assistanceDenial ?? (!AffordableAssistance(5) ? "LOW OPS RESERVE" : (team.CrewSupport & 1) != 0 ? "SCOUT ALREADY ASSIGNED TO THIS LEG" : "Adds 22 intelligence and reduces exposure on the next move. Knowledge persists; route cover is consumed per leg."));
            fieldOrders[1].Set("COVER / " + (support?.CrewAssistancePrice(6) ?? 150) + " OPS\n" + ((team.CrewSupport & 2) != 0 ? "ASSIGNED / THIS LEG" : "LESS EXPOSURE / NEXT LEG"), AffordableAssistance(6) && CanSend && orderWindow && team.RouteStep < 3 && (team.CrewSupport & 2) == 0, (team.CrewSupport & 2) != 0,
                assistanceDenial ?? (!AffordableAssistance(6) ? "LOW OPS RESERVE" : (team.CrewSupport & 2) != 0 ? "COVER ALREADY ASSIGNED TO THIS LEG" : "Reduces exposure on the next move. Aircrew clearing threats also reduces actual route and post pressure."));
            for (int i = 0; i < 2; i++)
            {
                bool fast = i == 0;
                bool known = d != null && d.IntelKnown(team.Anchor, now) && team.CurrentThreat != byte.MaxValue && team.CurrentRadars != byte.MaxValue;
                FieldRouteForecast forecast = SpecOpsDetachment.RouteForecast(team, fast, now);
                string prediction = "PREP " + forecast.Preparation + " / INTEL " + forecast.Intel + " / EXP " + (known ? forecast.Exposure.ToString() : "?") + " / " + forecast.CooldownSeconds + " S";
                string help = "After this move: " + prediction + " / " + (known ? "Q" + forecast.Quality + " / " + forecast.Charges + " charges" : "Scout or aircrew recon to resolve the defender picture.") + " / " + (forecast.RouteStep >= 3 && !forecast.CanExecute ? "EXECUTION BLOCKED: acknowledgement misses the window or approach exceeds limits." : "Passive pressure continues while waiting.");
                string routeDenial = !live ? "DETACHMENT DISABLED" : now >= team.PhaseEnd ? "DECISION WINDOW EXPIRED" : team.RouteStep >= 3 ? "APPROACH COMPLETE" : now < team.OrderReadyAt ? "ACKNOWLEDGEMENT / " + FieldWords.Clock(team.OrderReadyAt - now) : null;
                fieldOrders[i + 2].Set((fast ? "FAST / " : "COVERED / ") + SpecOpsDetachment.RouteName(team, fast) + "\n" + (routeDenial ?? prediction), CanSend && orderWindow && team.RouteStep < 3 && now >= team.OrderReadyAt, false, routeDenial ?? help);
            }
            SpecOpsDenial executeDenial = d?.CheckDirective(selectedTeam, SpecOpsDirective.Execute, now, team.Revision) ?? SpecOpsDenial.Disabled;
            int quality = SpecOpsDetachment.ExecutionQuality(team);
            bool executeReady = CanSend && deciding && team.RouteStep == 3 && executeDenial == SpecOpsDenial.None;
            fieldOrders[4].Set("EXECUTE\n" + (!CanSend ? "AWAIT HOST" : executeReady ? "Q" + quality + " / " + FieldCatalog.PostCharges(quality) + " CHARGES" : deciding && team.RouteStep < 3 ? "NEEDS THREE LEGS" : FieldShortDenial(executeDenial)), executeReady, false,
                !CanSend ? "Awaiting the faction host acknowledgement or a fresh operation snapshot." : team.RouteStep < 3 ? "Complete all three approach legs first." : FieldWords.Denial(executeDenial) + " / " + FieldCatalog.PostSummary(team.Mission, quality));
            fieldOrders[5].Set("EXTRACT\nPRESERVE TEAM", CanSend && team.Deployed, false, "Withdraw this team safely. Remaining post charges and market supply end with extraction.");
            FieldMission package = team.Deployed ? team.Mission : fieldPreviewMission;
            int packageQuality = team.State == TeamState.Holding || team.State == TeamState.OnTask ? team.Quality : team.Deployed ? quality : 0;
            string packageState = team.State == TeamState.Holding ? live ? "POST HELD" : "POST / SERVER DISABLED" : team.Deployed ? "ON EXECUTION" : "BASE PACKAGE / PREVIEW";
            float sectorX = team.Deployed ? team.X : objective.X, sectorZ = team.Deployed ? team.Z : objective.Z;
            string sectorCode = team.Deployed || objective.Anchor != 0 ? OpsSectors.Code(sectorX, sectorZ) : "--";
            outcome.text = packageState + " / Q" + packageQuality + " / " + (team.State == TeamState.Holding ? team.Charges : FieldCatalog.PostCharges(packageQuality)) + " CHG\n" + FieldCatalog.PostSummary(package, packageQuality) +
                (fieldCompact ? "" : "\nSECTOR " + sectorCode + (d != null && d.ControlsSector(sectorX, sectorZ, now) ? " / CONTROLLED" : " / LOCKED"));
            if (fieldQualityBadge != null)
            {
                bool banked = live && team.State == TeamState.Holding && now < team.PhaseEnd && team.Charges > 0;
                Color tone = banked ? RoomPaint.Ready : team.Deployed ? BranchAccent : BranchDim;
                fieldQualityBadge.text = "Q" + packageQuality;
                fieldQualityBadge.color = tone;
                fieldQualityPips.FillColor = fieldQualityPips.FillEnd = tone;
                fieldQualityPips.Value = (packageQuality + 1) / 4f;
                fieldQualityPips.SetVerticesDirty();
                for (int tier = 0; tier < fieldQualityRules.Length; tier++)
                    if (fieldQualityRules[tier] != null)
                    {
                        fieldQualityRules[tier].text = tier == 0 ? "Q1 P70 I40 X<=60" : tier == 1 ? "Q2 P80 I60 X<=45" : "Q3 P95 I80 X<=30";
                        fieldQualityRules[tier].color = packageQuality > tier ? RoomPaint.Ready : BranchDim;
                    }
            }
            if (fieldPackageEffect != null) fieldPackageEffect.text = FieldWords.BriefEffect(package, packageQuality) + "\n" + (team.Deployed ? "FROM " + FieldWords.Origin(team) : "Three shared approach legs, then execute. Native combat changes the pressure.");
            clues[0].text = d == null ? "Awaiting the faction detachment." : FieldWords.OperatorAdvice(team, now);
        }

        private Color FieldStateTone(TeamState state)
        {
            if (state == TeamState.Ready || state == TeamState.Holding) return RoomPaint.Ready;
            if (state == TeamState.Deciding || state == TeamState.OnTask) return BranchAccent;
            return state == TeamState.EnRoute ? BranchInk : BranchDim;
        }

        private static string FieldShortDenial(SpecOpsDenial denial)
        {
            switch (denial)
            {
                case SpecOpsDenial.None: return "READY";
                case SpecOpsDenial.Disabled: return "SERVER OFF";
                case SpecOpsDenial.WrongObjective: return "NOT HERE";
                case SpecOpsDenial.NoRadars: return "NO RADARS";
                case SpecOpsDenial.ObjectiveTaken: return "ASSIGNED";
                case SpecOpsDenial.PostLimit: return "POST CAP 2";
                case SpecOpsDenial.SeizeUnavailable: return "GARRISONS OFF";
                case SpecOpsDenial.OrderCoolingDown: return "AWAIT ACK";
                case SpecOpsDenial.Preparing: return "PREP BELOW 60";
                case SpecOpsDenial.Exposed: return "EXPOSURE ABOVE 75";
                case SpecOpsDenial.NotAtDecision: return "WINDOW CLOSED";
                case SpecOpsDenial.Busy: return "TEAM BUSY";
                case SpecOpsDenial.Unformed: return "FORM TEAM FIRST";
                case SpecOpsDenial.StaleObjective: return "TARGET GONE";
                default: return "AWAIT FIELD ORDER";
            }
        }

        private static string FieldExcerpt(string report, int limit)
        {
            if (string.IsNullOrEmpty(report) || report.Length <= limit) return report;
            int cut = report.LastIndexOf(' ', Math.Max(0, limit - 3));
            return report.Substring(0, cut > limit / 2 ? cut : limit - 3) + "...";
        }
    }
}
