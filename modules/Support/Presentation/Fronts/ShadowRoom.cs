using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The SOF front's room (R5, presence / infiltration map). Top: own camps, the operation footprint, teams with routes and growing
    /// exposure rings, held buildings, revealed targets and enemy teams. Bottom: a card per team and the selected team's mission
    /// picker and orders. Paints from <see cref="ShadowRoomView"/> only.
    /// </summary>
    internal sealed class ShadowRoom : IFrontRoom
    {
        public const int Push = 0, Hold = 1, Exfil = 2, Lift = 3;
        private const float TopH = 540f, Gap = 8f, ColW = 270f, CardH = 172f;
        private const int Cols = 12, Rows = 8, MaxTeams = 4, MaxTargets = 8, MaxCamps = 3, MaxHeld = 4, MaxEnemies = 4, Missions = 5;
        private static readonly string[] OrderWord = { "PUSH", "HOLD", "EXFIL", "LIFT" };
        private static readonly string[] OrderHelp =
        {
            "Push: move faster and 1.5x as visible. Toggle.",
            "Hold: stay put and let exposure fall twice as fast. Toggle.",
            "Exfil: abandon the mission and go home.",
            "Lift: call a helicopter to pick the team up where it stands.",
        };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private float heroW, colX;
        private FrontMapPlate plate;

        private FrontBox situation, picker;
        private FrontVector side;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] teamLabels = new TMP_Text[MaxTeams], targetLabels = new TMP_Text[MaxTargets], campLabels = new TMP_Text[MaxCamps],
            heldLabels = new TMP_Text[MaxHeld], enemyMarks = new TMP_Text[MaxEnemies];
        private TMP_Text fobLabel;
        private readonly Image[] teamHits = new Image[MaxTeams];
        private readonly int[] hitSlots = new int[MaxTeams], hitTargets = new int[MaxTargets];
        private readonly Image[] targetHits = new Image[MaxTargets];

        private TMP_Text opHead, opName, opNote, teamHead, teamCount, teamNote, heldHead, revHead, tapHead, tapLine, keyHead;
        private readonly TMP_Text[] heldName = new TMP_Text[MaxHeld], heldTime = new TMP_Text[MaxHeld], revKey = new TMP_Text[5], revVal = new TMP_Text[5], keyText = new TMP_Text[6];

        private readonly Card[] cards = new Card[MaxTeams];
        private readonly AvControl[] mission = new AvControl[Missions], order = new AvControl[4];
        private readonly TMP_Text[] missionA = new TMP_Text[Missions], missionB = new TMP_Text[Missions];
        private TMP_Text targetLine, pickHint, orderNote;
        private int selectedSlot = -1, selectedTarget;
        private readonly MissionKind[] missionKind = new MissionKind[Missions];
        private readonly string[] helpShown = new string[Missions];

        public ShadowRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        private static Color Own => AvInk.State(AvState.Ready);

        private void Do(Action a) { actions.Touch(); a(); }

        public void Build(RectTransform hero, float w, float h)
        {
            heroW = w;
            colX = w - 12f - ColW;
            BuildSituation(hero);
            float cy = TopH + Gap, cw = (w - 3f * Gap) / MaxTeams;
            for (int i = 0; i < MaxTeams; i++)
            {
                int slot = i;
                cards[i] = new Card(hero, skin, i * (cw + Gap), cy, cw, CardH, () => Do(() => actions.SelectTeam(slot)));
            }
            float py = cy + CardH + Gap;
            BuildPicker(hero, 0f, py, w, h - py);
        }

        // ---- Situation -----------------------------------------------------------------------------------------------

        private TMP_Text Head(RectTransform b, string name, float x, float y, string text)
        {
            TMP_Text t = FrontKit.Mono(b, name, x, y, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            t.text = text;
            return t;
        }

        private const float KeyX = 14f, KeyY = 455f;
        private static readonly Rect HeldCard = new Rect(8f, 29f, 282f, 168f), KeyCard = new Rect(8f, 452f, 410f, 58f);
        private Rect OpCard => new Rect(colX - 6f, 29f, ColW + 12f, 162f);

        private void BuildSituation(RectTransform hero)
        {
            situation = new FrontBox(hero, skin, "Shadow", 0f, 0f, heroW, TopH, "SOF SITUATION · PRESENCE AND INFILTRATION", AvIcon.Eye);
            RectTransform b = situation.Rect;
            plate = new FrontMapPlate(b, skin, heroW, TopH, Cols, Rows);
            side = FrontVector.Add(b, "Side");   // overlay cards on the map
            AvLay.Place(side.rectTransform, 0f, 0f, heroW, TopH);
            plate.Labels.Fixed(HeldCard);
            plate.Labels.Fixed(KeyCard);
            plate.Labels.Fixed(OpCard);
            airbases = new FrontMap.AirbaseLayer(b);
            fobLabel = FrontKit.Mono(b, "FobLabel", 0f, 0f, 190f, 14f, 10.5f, TextAlignmentOptions.TopLeft);
            for (int i = 0; i < MaxTeams; i++) teamLabels[i] = Pool(b, "TeamLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxTargets; i++) targetLabels[i] = Pool(b, "TargetLabel" + i, 170f, 14f);
            for (int i = 0; i < MaxCamps; i++) campLabels[i] = Pool(b, "CampLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxHeld; i++) heldLabels[i] = Pool(b, "HeldLabel" + i, 170f, 28f);
            for (int i = 0; i < MaxEnemies; i++)
            {
                enemyMarks[i] = FrontKit.Mono(b, "Enemy" + i, 0f, 0f, 18f, 18f, 14f, TextAlignmentOptions.Midline, true);
                enemyMarks[i].text = "?";
                enemyMarks[i].gameObject.SetActive(false);
            }
            fobLabel.gameObject.SetActive(false);
            for (int i = 0; i < MaxTeams; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "TeamHit" + i, 0f, 0f, 34f, 26f, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => actions.SelectTeam(hitSlots[slot])); };
                hit.gameObject.SetActive(false);
                teamHits[i] = hit;
            }
            for (int i = 0; i < MaxTargets; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "TargetHit" + i, 0f, 0f, 30f, 30f, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => actions.SelectTarget(hitTargets[slot])); };
                hit.gameObject.SetActive(false);
                targetHits[i] = hit;
            }

            opHead = Head(b, "OpHead", colX, 34f, "OPERATION FOOTPRINT");
            opName = FrontKit.Mono(b, "OpName", colX, 52f, ColW, 18f, 12f, TextAlignmentOptions.MidlineLeft, true);
            opNote = FrontKit.Mono(b, "OpNote", colX, 70f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            teamHead = Head(b, "TeamHead", colX, 96f, "TEAMS IN THE FIELD");
            teamCount = FrontKit.Mono(b, "TeamCount", colX, 112f, 120f, 36f, 24f, TextAlignmentOptions.MidlineLeft, true);
            teamNote = FrontKit.Mono(b, "TeamNote", colX + 110f, 114f, ColW - 110f, 30f, 10.5f, TextAlignmentOptions.MidlineRight);
            tapHead = Head(b, "TapHead", colX, 152f, "SIGNAL TAP");
            tapLine = FrontKit.Mono(b, "TapLine", colX, 170f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            heldHead = Head(b, "HeldHead", 16f, 34f, "HELD BUILDINGS");
            for (int i = 0; i < MaxHeld; i++)
            {
                heldName[i] = FrontKit.Mono(b, "HeldName" + i, 16f, 52f + i * 16f, 170f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                heldTime[i] = FrontKit.Mono(b, "HeldTime" + i, 186f, 52f + i * 16f, 96f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            }
            revHead = Head(b, "RevHead", 16f, 124f, "REVEALED");
            string[] rk = { "GROUND", "ANCHORS", "BUILDINGS", "RELAYS", "ENEMY TEAMS" };
            for (int i = 0; i < 5; i++)
            {
                revKey[i] = FrontKit.Mono(b, "RevKey" + i, 16f + (i % 2) * 135f, 142f + (i / 2) * 16f, 100f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                revKey[i].text = rk[i];
                revVal[i] = FrontKit.Mono(b, "RevVal" + i, 16f + (i % 2) * 135f + 80f, 142f + (i / 2) * 16f, 45f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            }
            keyHead = Head(b, "KeyHead", KeyX + 4f, KeyY, "KEY");
            string[] words = { "TEAM", "ROUTE", "EXPOSURE", "CAMP", "TARGET", "ENEMY TEAM" };
            for (int i = 0; i < 6; i++)
            {
                keyText[i] = FrontKit.Mono(b, "Key" + i, KeyX + 26f + (i % 3) * 130f, KeyY + 18f + (i / 3) * 17f, 100f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                keyText[i].text = words[i];
            }
            skin.Add(() =>
            {
                opHead.color = AvInk.Dim; teamHead.color = AvInk.Dim; heldHead.color = AvInk.Dim; revHead.color = AvInk.Dim; tapHead.color = AvInk.Dim; keyHead.color = AvInk.Dim;
                foreach (TMP_Text t in revKey) t.color = AvInk.Dim;
                foreach (TMP_Text t in keyText) t.color = AvInk.Dim;
                foreach (TMP_Text t in enemyMarks) t.color = AvInk.State(AvState.Danger);
            });
        }

        private static TMP_Text Pool(RectTransform b, string name, float w, float h)
        {
            TMP_Text t = FrontKit.Mono(b, name, 0f, 0f, w, h, 10.5f, TextAlignmentOptions.TopLeft);
            t.gameObject.SetActive(false);
            return t;
        }

        // ---- Picker ----------------------------------------------------------------------------------------------------

        private void BuildPicker(RectTransform hero, float x, float y, float w, float h)
        {
            picker = new FrontBox(hero, skin, "Picker", x, y, w, h, "MISSION ORDERS", AvIcon.Target);
            RectTransform b = picker.Rect;
            float bw = 170f, step = 176f;
            for (int i = 0; i < Missions; i++)
            {
                int slot = i;
                mission[i] = Button(b, "MISSION", 8f + i * step, 30f, bw, AvButtonStyle.Default, () => Do(() => actions.TeamMission(selectedSlot, (int)missionKind[slot], selectedTarget)), "");
                missionA[i] = FrontKit.Mono(b, "MissionA" + i, 8f + i * step, 68f, bw, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                missionB[i] = FrontKit.Mono(b, "MissionB" + i, 8f + i * step, 84f, bw, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            }
            float ox = colX, ow = (ColW - 6f) / 2f;
            for (int i = 0; i < 4; i++)
            {
                int verb = i;
                order[i] = Button(b, OrderWord[i], ox + (i % 2) * (ow + 6f), 30f + (i / 2) * 40f, ow, i == 2 ? AvButtonStyle.Danger : AvButtonStyle.Default, () => Do(() => actions.TeamOrder(selectedSlot, verb)), OrderHelp[i]);
            }
            orderNote = FrontKit.Mono(b, "OrderNote", ox, 112f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            targetLine = FrontKit.Mono(b, "TargetLine", 10f, 114f, 870f, 18f, 11f, TextAlignmentOptions.MidlineLeft, true);
            pickHint = FrontKit.Mono(b, "PickHint", 10f, 138f, 870f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            pickHint.text = "> CLICK A REVEALED TARGET, THEN A MISSION. ODDS FALL AS EXPOSURE RISES; AN EXPLOIT CUTS THE COST.";
            skin.Add(() => pickHint.color = AvInk.Dim);
        }

        private AvControl Button(RectTransform p, string label, float x, float y, float w, AvButtonStyle style, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, style));
            AvLay.Place(c.Rect, x, y, w, 34f);
            c.SingleLine();
            if (!string.IsNullOrEmpty(help)) c.Help = help;
            return c;
        }

        // ---- Paint -----------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            ShadowRoomView s = v.Shadow;
            selectedSlot = s.SelectedSlot;
            selectedTarget = s.SelectedTargetId;
            PaintSituation(v, s);
            PaintCards(s);
            PaintPicker(s);
        }

        private Vector2 P(float x, float y) => plate.P(x, y);

        public bool Tick(Vector2 mouse) => plate.Tick(mouse);

        private static Color Tone(AvState s) =>
            s == AvState.Ready ? Own : s == AvState.Caution ? AvInk.State(AvState.Caution) : s == AvState.Danger ? AvInk.State(AvState.Danger) : s == AvState.Info ? AvInk.Key : AvInk.Dim;

        private static Color ExposureInk(float pct) => pct < 40f ? Own : pct < 70f ? AvInk.State(AvState.Caution) : AvInk.State(AvState.Danger);

        private void PaintSituation(FrontRoomView v, ShadowRoomView s)
        {
            FrontMapView mv = plate.View;
            Vector2 sum = Vector2.zero;
            int na = 0;
            if (s.HasFob) { sum += new Vector2(s.FobX, s.FobY); na++; }
            for (int i = 0; i < s.Camps.Count; i++) { sum += new Vector2(s.Camps[i].X, s.Camps[i].Y); na++; }
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) { sum += new Vector2(s.Teams[i].X, s.Teams[i].Y); na++; }
            if (na > 0) mv.AutoCenter(sum / na);
            plate.Begin();
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Color own = Own, amber = AvInk.State(AvState.Caution), danger = AvInk.State(AvState.Danger), key = AvInk.Key, dim = AvInk.Dim, foe = AvInk.Hostile;
            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, P);

            // Operation footprint.
            if (s.HasFob)
            {
                Vector2 f = P(s.FobX, s.FobY);
                float r = s.FobRadius * mv.Scale;
                g.Disc(f, r, own.WithAlpha(0.05f), 64);
                g.Ring(f, r, 1.4f, own.WithAlpha(0.7f), 80, 1f, 1f);
                g.Ring(f, r * 0.55f, 1f, own.WithAlpha(0.3f), 56, 1f, 1f);
                g.Line(f + new Vector2(0f, 8f), f + new Vector2(0f, -14f), 1.6f, own);
                g.Tri(f + new Vector2(0f, -14f), f + new Vector2(10f, -10f), f + new Vector2(0f, -6f), own);
                labels.Take(new Rect(f.x - 12f, f.y - 16f, 24f, 26f));
            }

            // Camps.
            for (int i = 0; i < MaxCamps; i++)
            {
                if (i >= s.Camps.Count) { campLabels[i].gameObject.SetActive(false); continue; }
                SofCampView c = s.Camps[i];
                Vector2 p = P(c.X, c.Y);
                Color k = c.Health == AnchorHealth.Live ? own : c.Health == AnchorHealth.Damaged ? amber : danger;
                g.Disc(p, 13f, AvInk.Ground.WithAlpha(0.88f), 20);
                g.Polyline(new[] { p + new Vector2(0f, -9f), p + new Vector2(10f, 4f), p + new Vector2(-10f, 4f) }, 1.6f, k, true);
                g.Line(p + new Vector2(-10f, 4f), p + new Vector2(10f, 4f), 2f, k);
                g.Line(p + new Vector2(0f, 4f), p + new Vector2(0f, -2f), 1.4f, k);
                if (c.Health == AnchorHealth.Down) Cross(g, p, 9f, danger);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }

            // Revealed targets.
            for (int i = 0; i < MaxTargets; i++) if (i >= s.Targets.Count) targetHits[i].gameObject.SetActive(false);
            for (int i = 0; i < s.Targets.Count; i++)
            {
                SofTargetView t = s.Targets[i];
                Vector2 p = P(t.X, t.Y);
                if (i < MaxTargets && !plate.Shown(p, 10f)) targetHits[i].gameObject.SetActive(false);
                else if (i < MaxTargets)
                {
                    targetHits[i].gameObject.SetActive(true);
                    hitTargets[i] = t.Id;
                    AvLay.Place(targetHits[i].rectTransform, p.x - 15f, p.y - 15f, 30f, 30f);
                }
                Color k = t.Resisted ? dim : foe;
                g.Disc(p, 11f, AvInk.Ground.WithAlpha(0.85f), 18);
                TargetGlyph(g, t.Kind, p, 8f, k);
                if (t.Resisted) g.Line(p + new Vector2(-9f, 9f), p + new Vector2(9f, -9f), 1.6f, dim);
                if (t.Exploit) g.Ring(p, 15f, 1.3f, amber, 24, 1f, 1f);
                if (t.Selected) Brackets(g, p, 17f, AvInk.Select);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }

            // Revealed enemy teams.
            for (int i = 0; i < MaxEnemies; i++)
            {
                if (i >= s.Enemies.Count) { enemyMarks[i].gameObject.SetActive(false); continue; }
                Vector2 p = P(s.Enemies[i].X, s.Enemies[i].Y);
                g.Quad(p + new Vector2(0f, -11f), p + new Vector2(11f, 0f), p + new Vector2(0f, 11f), p + new Vector2(-11f, 0f), foe.WithAlpha(0.22f));
                g.Polyline(new[] { p + new Vector2(0f, -11f), p + new Vector2(11f, 0f), p + new Vector2(0f, 11f), p + new Vector2(-11f, 0f) }, 1.6f, foe, true);
                enemyMarks[i].gameObject.SetActive(true);
                AvLay.Place(enemyMarks[i].rectTransform, p.x - 9f, p.y - 9f, 18f, 18f);
                labels.Take(new Rect(p.x - 13f, p.y - 13f, 26f, 26f));
            }

            // Held buildings.
            for (int i = 0; i < MaxHeld; i++)
            {
                if (i >= s.Held.Count) { heldLabels[i].gameObject.SetActive(false); continue; }
                Vector2 p = P(s.Held[i].X, s.Held[i].Y);
                g.Disc(p, 14f, own.WithAlpha(0.16f), 20);
                g.Rect(p.x - 7f, p.y - 4f, 14f, 11f, own.WithAlpha(0.5f));
                g.Frame(p.x - 7f, p.y - 4f, 14f, 11f, 1.4f, own);
                g.Line(p + new Vector2(0f, -4f), p + new Vector2(0f, -12f), 1.4f, own);
                g.Tri(p + new Vector2(0f, -12f), p + new Vector2(8f, -9f), p + new Vector2(0f, -6f), own);
                labels.Take(new Rect(p.x - 14f, p.y - 15f, 28f, 28f));
            }

            // Teams: exposure rings, routes, then glyphs.
            for (int i = 0; i < s.Teams.Count && i < MaxTeams; i++)
            {
                SofTeamView t = s.Teams[i];
                if (!t.Present) continue;
                Vector2 p = P(t.X, t.Y);
                Color k = Tone(t.Tone);
                float er = 14f + t.Exposure / 100f * 56f;
                Color ek = ExposureInk(t.Exposure);
                g.Ring(p, 70f, 1f, ek.WithAlpha(0.18f), 56, 1f, 1f);
                g.Disc(p, er, ek.WithAlpha(0.1f), 48);
                g.Ring(p, er, 1.6f, ek.WithAlpha(0.9f), 56);
                if (t.HasDest)
                {
                    Vector2 d = P(t.DestX, t.DestY), u = (d - p).normalized, nrm = new Vector2(-u.y, u.x);
                    g.Dashed(p, d, 1.8f, k.WithAlpha(0.9f), 8f, 5f);
                    g.Tri(d, d - u * 10f + nrm * 5f, d - u * 10f - nrm * 5f, k);
                    g.Frame(d.x - 6f, d.y - 6f, 12f, 12f, 1.2f, k.WithAlpha(0.8f));
                    labels.Take(new Rect(d.x - 8f, d.y - 8f, 16f, 16f));
                }
                if (t.Lasing)
                {
                    Vector2 tg = P(t.TargetX, t.TargetY);
                    g.Dashed(p, tg, 1.4f, key, 3f, 3f);
                    g.Ring(tg, 18f, 1.4f, key, 30);
                }
            }
            int shown = 0;
            for (int i = 0; i < MaxTeams; i++)
            {
                if (i >= s.Teams.Count || !s.Teams[i].Present) { teamLabels[i].gameObject.SetActive(false); teamHits[i].gameObject.SetActive(false); continue; }
                SofTeamView t = s.Teams[i];
                Vector2 p = P(t.X, t.Y);
                Color k = Tone(t.Tone);
                g.Rect(p.x - 10f, p.y - 7f, 20f, 14f, AvInk.Ground.WithAlpha(0.9f));
                g.Frame(p.x - 10f, p.y - 7f, 20f, 14f, 1.8f, k);
                g.Line(p + new Vector2(-6f, -4f), p + new Vector2(6f, 4f), 1.6f, k);
                g.Line(p + new Vector2(-6f, 4f), p + new Vector2(6f, -4f), 1.6f, k);
                if (t.Helicopter) { g.Line(p + new Vector2(-8f, -11f), p + new Vector2(8f, -11f), 1.6f, k); g.Line(p + new Vector2(0f, -11f), p + new Vector2(0f, -7f), 1.4f, k); }
                if (t.Wounded) g.Disc(p + new Vector2(11f, -8f), 3f, danger, 8);
                if (t.Selected) Brackets(g, p, 17f, AvInk.Select);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
                shown++;
                Image hit = teamHits[i];
                hit.gameObject.SetActive(plate.Shown(p, 10f));
                hitSlots[i] = t.Slot;
                AvLay.Place(hit.rectTransform, p.x - 17f, p.y - 13f, 34f, 26f);
            }

            // Labels last, once every glyph has claimed its space.
            if (s.HasFob && plate.Shown(P(s.FobX, s.FobY), 12f))
            {
                fobLabel.gameObject.SetActive(true);
                FrontKit.Set(fobLabel, s.FobName, own);
                FrontMap.Plate(g, labels.Place(fobLabel, P(s.FobX, s.FobY), 12f, true));
            }
            else fobLabel.gameObject.SetActive(false);
            for (int i = 0; i < MaxTeams; i++)
            {
                if (i >= s.Teams.Count || !s.Teams[i].Present) continue;
                SofTeamView t = s.Teams[i];
                TMP_Text lt = teamLabels[i];
                if (!plate.Shown(P(t.X, t.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                lt.text = C2Kit.Tint("<b>" + t.Callsign + "</b> · " + t.State, Tone(t.Tone)) + "\n" + C2Kit.Tint("EXPOSURE " + t.Exposure + " %", ExposureInk(t.Exposure));
                lt.color = AvInk.Ink;
                FrontMap.Plate(g, labels.Place(lt, P(t.X, t.Y), 14f, false));
            }
            for (int i = 0; i < MaxCamps && i < s.Camps.Count; i++)
            {
                SofCampView c = s.Camps[i];
                TMP_Text lt = campLabels[i];
                if (!plate.Shown(P(c.X, c.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                Color k = c.Health == AnchorHealth.Live ? own : c.Health == AnchorHealth.Damaged ? amber : danger;
                lt.text = C2Kit.Tint("<b>" + c.Name + "</b>", k) + "\n" + C2Kit.Tint(c.Health == AnchorHealth.Down ? "DOWN · RESTORE " + c.Rebuild + " %" : c.Health == AnchorHealth.Damaged ? "DAMAGED" : "LIVE", dim);
                lt.color = AvInk.Ink;
                FrontMap.Plate(g, labels.Place(lt, P(c.X, c.Y), 14f, false));
            }
            for (int i = 0; i < MaxHeld && i < s.Held.Count; i++)
            {
                SofHeldView hv = s.Held[i];
                TMP_Text lt = heldLabels[i];
                if (!plate.Shown(P(hv.X, hv.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                lt.text = C2Kit.Tint("<b>" + hv.Name + "</b>", own) + "\n" + C2Kit.Tint("HELD · " + FrontKit.Clock(hv.SecondsLeft), amber);
                lt.color = AvInk.Ink;
                FrontMap.Plate(g, labels.Place(lt, P(hv.X, hv.Y), 14f, false));
            }
            for (int i = 0; i < MaxTargets; i++)
            {
                if (i >= s.Targets.Count) { targetLabels[i].gameObject.SetActive(false); continue; }
                SofTargetView t = s.Targets[i];
                TMP_Text lt = targetLabels[i];
                if (!plate.Shown(P(t.X, t.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                lt.text = C2Kit.Tint("<b>" + t.Name + "</b>" + (t.Exploit ? " · EXPLOIT" : t.Resisted ? " · RESISTED" : ""), t.Resisted ? dim : t.Exploit ? amber : foe);
                lt.color = AvInk.Ink;
                FrontMap.Plate(g, labels.Place(lt, P(t.X, t.Y), 14f, true));
            }
            airbases.Names(g, v.Airbases, P, mv, (t, a) => labels.Place(t, a, 9f, true, true));

            int up = 0;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) up++;
            if (s.HasFob) plate.Dot(s.FobX, s.FobY, own);
            for (int i = 0; i < s.Camps.Count; i++) plate.Dot(s.Camps[i].X, s.Camps[i].Y, own);
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) plate.Dot(s.Teams[i].X, s.Teams[i].Y, Tone(s.Teams[i].Tone));
            for (int i = 0; i < s.Targets.Count; i++) plate.Dot(s.Targets[i].X, s.Targets[i].Y, foe);
            plate.End();

            situation.SetMeta(s.Active ? up + " TEAMS OUT · " + s.Targets.Count + " TARGETS REVEALED · " + s.Enemies.Count + " ENEMY TEAMS" : s.Meta, s.Enemies.Count > 0 ? FrontKit.Tone(AvState.Danger) : dim);
            side.Clear();
            PaintSide(side, s, up);
            side.Flush();
        }

        private void PaintSide(FrontVector g, ShadowRoomView s, int up)
        {
            FrontMapPlate.Card(g, OpCard.x, OpCard.y, OpCard.width, OpCard.height);
            FrontMapPlate.Card(g, HeldCard.x, HeldCard.y, HeldCard.width, HeldCard.height);
            FrontMapPlate.Card(g, KeyCard.x, KeyCard.y, KeyCard.width, KeyCard.height);
            FrontKit.Set(opName, s.HasFob ? s.FobName : "NO OPERATION FOOTPRINT", s.HasFob ? Own : AvInk.Dim);
            FrontKit.Set(opNote, C2Kit.FitTo(opNote, s.FobNote, ColW), AvInk.Dim);
            FrontKit.Set(teamCount, up + " / " + s.TeamCap, up > 0 ? FrontKit.Tone(AvState.Ready) : AvInk.Dim);
            int worst = 0;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present) worst = Math.Max(worst, s.Teams[i].Exposure);
            FrontKit.Set(teamNote, up == 0 ? "NONE OUT" : "WORST EXPOSURE " + worst + " %", up == 0 ? AvInk.Dim : worst < 40 ? FrontKit.Tone(AvState.Ready) : worst < 70 ? FrontKit.Tone(AvState.Caution) : FrontKit.Tone(AvState.Danger));
            for (int i = 0; i < MaxHeld; i++)
            {
                bool on = i < s.Held.Count;
                FrontKit.Set(heldName[i], on ? C2Kit.FitTo(heldName[i], s.Held[i].Name, 166f) : (i == 0 ? "NONE HELD" : ""), on ? AvInk.Ink : AvInk.Dim);
                FrontKit.Set(heldTime[i], on ? FrontKit.Clock(s.Held[i].SecondsLeft) : "", FrontKit.Tone(AvState.Caution));
            }
            int[] kinds = new int[4];
            for (int i = 0; i < s.Targets.Count; i++) kinds[(int)s.Targets[i].Kind]++;
            for (int i = 0; i < 4; i++) FrontKit.Set(revVal[i], kinds[i].ToString(), kinds[i] > 0 ? AvInk.Ink : AvInk.Dim);
            FrontKit.Set(revVal[4], s.Enemies.Count.ToString(), s.Enemies.Count > 0 ? FrontKit.Tone(AvState.Danger) : AvInk.Dim);
            FrontKit.Set(tapLine, C2Kit.FitTo(tapLine, s.TapLine, ColW), AvInk.Dim);

            Color[] ink = { Own, Own, AvInk.State(AvState.Caution), Own, AvInk.Hostile, AvInk.Hostile };
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = new Vector2(KeyX + 12f + (i % 3) * 130f, KeyY + 26f + (i / 3) * 17f);
                switch (i)
                {
                    case 0: g.Frame(p.x - 8f, p.y - 5f, 16f, 10f, 1.6f, ink[i]); g.Line(p + new Vector2(-4f, -3f), p + new Vector2(4f, 3f), 1.2f, ink[i]); g.Line(p + new Vector2(-4f, 3f), p + new Vector2(4f, -3f), 1.2f, ink[i]); break;
                    case 1: g.Dashed(p + new Vector2(-8f, 0f), p + new Vector2(5f, 0f), 1.8f, ink[i], 4f, 3f); g.Tri(p + new Vector2(9f, 0f), p + new Vector2(3f, -4f), p + new Vector2(3f, 4f), ink[i]); break;
                    case 2: g.Disc(p, 7f, ink[i].WithAlpha(0.12f), 16); g.Ring(p, 7f, 1.4f, ink[i], 18); break;
                    case 3: g.Polyline(new[] { p + new Vector2(0f, -6f), p + new Vector2(7f, 3f), p + new Vector2(-7f, 3f) }, 1.4f, ink[i], true); break;
                    case 4: TargetGlyph(g, TargetKind.Building, p, 5f, ink[i]); break;
                    default: g.Polyline(new[] { p + new Vector2(0f, -7f), p + new Vector2(7f, 0f), p + new Vector2(0f, 7f), p + new Vector2(-7f, 0f) }, 1.4f, ink[i], true); break;
                }
            }
        }

        private static void TargetGlyph(FrontVector g, TargetKind kind, Vector2 p, float r, Color k)
        {
            switch (kind)
            {
                case TargetKind.Ground:
                    g.Polyline(new[] { p + new Vector2(-r, -r * 0.7f), p + new Vector2(r, -r * 0.7f), p + new Vector2(0f, r) }, 1.6f, k, true);
                    break;
                case TargetKind.Anchor:
                    g.Polyline(new[] { p + new Vector2(0f, -r), p + new Vector2(r, 0f), p + new Vector2(0f, r), p + new Vector2(-r, 0f) }, 1.6f, k, true);
                    g.Disc(p, 2f, k, 8);
                    break;
                case TargetKind.Relay:
                    g.Ring(p, r, 1.6f, k, 20);
                    g.Disc(p, 2.2f, k, 8);
                    break;
                default:
                    g.Frame(p.x - r * 0.85f, p.y - r * 0.85f, r * 1.7f, r * 1.7f, 1.6f, k);
                    break;
            }
        }

        private static void Cross(FrontVector g, Vector2 p, float r, Color k)
        {
            g.Line(p + new Vector2(-r, -r), p + new Vector2(r, r), 2f, k);
            g.Line(p + new Vector2(-r, r), p + new Vector2(r, -r), 2f, k);
        }

        private static void Brackets(FrontVector g, Vector2 p, float r, Color k)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector2 q = p + new Vector2(sx * r, sy * r);
                    g.Line(q, q + new Vector2(-sx * 6f, 0f), 1.8f, k);
                    g.Line(q, q + new Vector2(0f, -sy * 6f), 1.8f, k);
                }
        }

        // ---- Cards and picker --------------------------------------------------------------------------------------------

        private void PaintCards(ShadowRoomView s)
        {
            for (int i = 0; i < MaxTeams; i++)
            {
                if (i < s.Teams.Count) cards[i].Paint(s.Teams[i], i);
                else cards[i].Empty(i);
            }
        }

        private void PaintPicker(ShadowRoomView s)
        {
            SofTeamView? sel = null;
            for (int i = 0; i < s.Teams.Count; i++) if (s.Teams[i].Present && s.Teams[i].Slot == s.SelectedSlot) sel = s.Teams[i];
            picker.SetMeta(sel.HasValue ? sel.Value.Callsign + " · " + sel.Value.State : "NO TEAM SELECTED", sel.HasValue ? Tone(sel.Value.Tone) : AvInk.Dim);
            for (int i = 0; i < Missions; i++)
            {
                bool on = i < s.Missions.Count;
                mission[i].Rect.gameObject.SetActive(on);
                missionA[i].gameObject.SetActive(on);
                missionB[i].gameObject.SetActive(on);
                if (!on) continue;
                SofMissionView m = s.Missions[i];
                missionKind[i] = m.Kind;
                mission[i].Label = m.Name;
                mission[i].Interactable = sel.HasValue && m.Allowed;
                mission[i].Latched = m.Current;
                if (helpShown[i] != m.Note) { helpShown[i] = m.Note; if (!string.IsNullOrEmpty(m.Note)) mission[i].Help = m.Note; }
                FrontKit.Set(missionA[i], m.Cost + " · ODDS " + m.Odds, m.Allowed ? AvInk.Ink : AvInk.Dim);
                FrontKit.Set(missionB[i], C2Kit.FitTo(missionB[i], m.Exploit ? "EXPLOIT · " + m.Note : m.Note, 170f), m.Allowed ? (m.Exploit ? FrontKit.Tone(AvState.Caution) : AvInk.Dim) : FrontKit.Tone(AvState.Caution));
            }
            for (int i = 0; i < 4; i++)
            {
                order[i].Interactable = sel.HasValue && s.OrderOn[i];
                order[i].Latched = s.OrderLatched[i];
            }
            FrontKit.Set(targetLine, C2Kit.FitTo(targetLine, s.TargetLine, 870f), s.SelectedTargetId != 0 ? AvInk.Ink : AvInk.Dim);
            FrontKit.Set(orderNote, C2Kit.FitTo(orderNote, s.OrderNote, ColW), AvInk.Dim);
        }

        private sealed class Card
        {
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text call, state, mission, flags, expKey, expVal, ammoKey, ammoVal, odds, eta;
            private readonly FrontBar exp, ammo;
            private bool selected;
            private Color railInk = Color.clear;

            public Card(RectTransform p, FrontSkin skin, float x, float y, float w, float h, Action click)
            {
                RectTransform root = AvLay.Child(p, "TeamCard");
                AvLay.Place(root, x, y, w, h);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, h, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, h, Color.clear);
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) click(); };
                call = FrontKit.Mono(root, "Call", 12f, 4f, 120f, 24f, 16f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", 120f, 4f, w - 130f, 24f, 11.5f, TextAlignmentOptions.MidlineRight, true, 1f);
                mission = FrontKit.Mono(root, "Mission", 12f, 30f, w - 22f, 16f, 11f, TextAlignmentOptions.MidlineLeft);
                flags = FrontKit.Mono(root, "Flags", 12f, 48f, w - 22f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                expKey = FrontKit.Mono(root, "ExpKey", 12f, 72f, 100f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
                expVal = FrontKit.Mono(root, "ExpVal", w - 112f, 72f, 100f, 14f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                exp = new FrontBar(root, skin, 12f, 88f, w - 24f, 10f);
                ammoKey = FrontKit.Mono(root, "AmmoKey", 12f, 108f, 100f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
                ammoVal = FrontKit.Mono(root, "AmmoVal", w - 112f, 108f, 100f, 14f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                ammo = new FrontBar(root, skin, 12f, 124f, w - 24f, 10f);
                odds = FrontKit.Mono(root, "Odds", 12f, 146f, 130f, 20f, 12f, TextAlignmentOptions.MidlineLeft, true);
                eta = FrontKit.Mono(root, "Eta", w - 142f, 146f, 130f, 20f, 11f, TextAlignmentOptions.MidlineRight);
                expKey.text = "EXPOSURE"; ammoKey.text = "AMMO";
                skin.Add(Restyle);
            }

            private void Restyle()
            {
                frame.Paint(selected ? AvInk.Select.WithAlpha(0.18f) : AvInk.Ground.WithAlpha(0.5f), selected ? AvInk.Select : AvInk.Hairline);
                rail.color = railInk;
                expKey.color = AvInk.Dim; ammoKey.color = AvInk.Dim;
            }

            public void Empty(int slot)
            {
                selected = false; railInk = AvInk.Muted;
                Restyle();
                FrontKit.Set(call, "SLOT " + (slot + 1), AvInk.Dim);
                FrontKit.Set(state, "FREE", AvInk.Dim);
                FrontKit.Set(mission, "No team raised.", AvInk.Dim);
                FrontKit.Set(flags, "Queue TRAIN TEAM in the programme list.", AvInk.Dim);
                exp.Show(false); ammo.Show(false);
                FrontKit.Set(expVal, "", AvInk.Dim); FrontKit.Set(ammoVal, "", AvInk.Dim);
                expKey.gameObject.SetActive(false); ammoKey.gameObject.SetActive(false);
                FrontKit.Set(odds, "", AvInk.Dim); FrontKit.Set(eta, "", AvInk.Dim);
            }

            public void Paint(in SofTeamView t, int slot)
            {
                if (!t.Present) { Empty(slot); return; }
                selected = t.Selected;
                Color k = Tone(t.Tone);
                railInk = k;
                Restyle();
                expKey.gameObject.SetActive(true); ammoKey.gameObject.SetActive(true);
                exp.Show(true); ammo.Show(true);
                FrontKit.Set(call, t.Callsign, AvInk.Ink);
                FrontKit.Set(state, t.State, k);
                FrontKit.Set(mission, C2Kit.FitTo(mission, t.Mission, 262f), AvInk.Ink);
                FrontKit.Set(flags, C2Kit.FitTo(flags, t.Flags, 262f), AvInk.Dim);
                FrontKit.Set(expVal, t.Exposure + " %", ExposureInk(t.Exposure));
                exp.Set(t.Exposure / 100f, ExposureInk(t.Exposure));
                FrontKit.Set(ammoVal, t.Ammo + " %", t.Ammo < 25 ? AvInk.State(AvState.Danger) : AvInk.Ink);
                ammo.Set(t.Ammo / 100f, t.Ammo < 25 ? AvInk.State(AvState.Danger) : AvInk.Key);
                FrontKit.Set(odds, "ODDS " + t.Odds + " %", t.Odds >= 60 ? AvInk.Ink : FrontKit.Tone(AvState.Caution));
                FrontKit.Set(eta, t.Eta, AvInk.Dim);
            }
        }
    }
}
