using System;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The SHADOW room's TARGETS tab (spec 7.3: the old TARGETS and ORDERS as one flow), top to bottom: IN THE FIELD, each team out with its mission, state and
    /// exposure; filter chips ALL / SITES / BUILDINGS / NETWORK / GROUND (ruling D-R1); one list of index cards, one per revealed target, where the aim (the picked
    /// target, or the area RECON point clicked on open map ground) opens in place as its dossier: the recon print and file, the missions it takes lit (the rest
    /// greyed with what they need), the teams (the nearest that can go pre-picked, any other a click away), how it goes in, odds and cost, and one SEND that sends at
    /// once or says why it cannot; CLEAR is the only way to drop the aim (ruling D-R5). HOSTILES NEAR US with SWEEP at the foot. Every press is a request.
    /// </summary>
    internal sealed partial class ShadowRoom
    {
        private const float FieldTop = 28f, FieldRowH = 16f, CardH = 30f, CardGap = 3f, BodyH = 250f, HostH = 66f, PlateH = 22f, KeyW = 58f;
        private static readonly string[] FilterWord = { "ALL", "SITES", "BUILDINGS", "NETWORK", "GROUND" };
        private static readonly string ReconHint = "CLICK OPEN GROUND ON THE MAP: RECON " + (SofRules.ReconRadius / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " KM THERE";

        private FrontBox targets;
        private FrontVector listG, fileMarks;
        private TMP_Text fieldHead, fieldRight, fieldNote, listNote, listHint, listMore, emptyHead, guideHead, guideText, hostHead, hostLine;
        private const float GuideH = 78f;
        private readonly TMP_Text[] fieldL = new TMP_Text[MaxTeams], fieldR = new TMP_Text[MaxTeams];
        private readonly AvControl[] filterChips = new AvControl[5];
        private readonly int[] filterCount = new int[5];
        private int targetFilter; // 0 ALL, else SofAim.Sites .. SofAim.Ground
        private AvControl sweep;

        // The index cards: vis[k] is a target's index in the view, or -1 for the RECON point's card (always first).
        private readonly Image[] rowHit = new Image[MaxTargets];
        private readonly RawImage[] rowThumb = new RawImage[MaxTargets];
        private readonly TMP_Text[] rowName = new TMP_Text[MaxTargets], rowGrid = new TMP_Text[MaxTargets], rowTag = new TMP_Text[MaxTargets];
        private readonly int[] rowId = new int[MaxTargets], vis = new int[MaxTargets + 1];
        private int listScroll, listCount, followAim;
        private bool listBelow;

        // The open card: the dossier, the missions, the teams, how it goes in, SEND.
        private RawImage filePaper, filePhoto;
        private TMP_Text fileTab, fileA, fileB, fileC, fileD, fileNote, missionKey, missionNote, teamKey, crewNote, goKey, goIn, helo, sendNote;
        private Stamp fileStamp;
        private TMP_Text[] cardTexts;
        private readonly AvControl[] plates = new AvControl[Missions];
        private readonly string[] plateHelp = new string[Missions];
        private readonly AvControl[] crew = new AvControl[MaxTeams];
        private readonly int[] crewSlot = new int[MaxTeams];
        private readonly string[] crewHelp = new string[MaxTeams];
        private AvControl send, clear;
        // The mission SEND sends, picked for the aim sendFor (a new aim opens on its best mission); whether SEND is live now.
        private MissionKind sendKind;
        private int sendFor;
        private bool canSend;

        private void BuildTargets(RectTransform column, float cw, float ch)
        {
            targets = new FrontBox(column, skin, "Targets", 0f, 0f, cw, ch, "TARGETS", AvIcon.Crosshair, FrontLook.Folder).Tagged("FILE 07-T");
            RectTransform b = targets.Rect;
            float secW = cw - 2f * SecX, cardW = cw - 2f * CardX;
            // Draw order: the slabs and card stock, the prints, the dossier sheet and its print, the marks and plates over them, then words and controls.
            listG = FrontVector.Add(b, "ListGfx");
            AvLay.Place(listG.rectTransform, 0f, 0f, cw, ch);
            for (int i = 0; i < MaxTargets; i++) rowThumb[i] = FrontArt.Fill(b, "ops_recon_ground", 0f, 0f, 1f, 1f, PrintTint);
            filePaper = FrontArt.Fill(b, "ops_bg_dossier", 0f, 0f, 1f, 1f, Color.white);
            filePhoto = FrontArt.Fill(b, "ops_recon_ground", 0f, 0f, 1f, 1f, PrintTint);
            fileMarks = FrontVector.Add(b, "FileMarks");
            AvLay.Place(fileMarks.rectTransform, 0f, 0f, cw, ch);

            fieldHead = FrontKit.Mono(b, "FieldHead", SecX + 10f, FieldTop + 5f, 200f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            fieldRight = FrontKit.Mono(b, "FieldRight", SecX + secW - 10f - 260f, FieldTop + 5f, 260f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
            fieldRight.text = "ALSO ON TEAMS & BASES";
            for (int i = 0; i < MaxTeams; i++)
            {
                fieldL[i] = FrontKit.Mono(b, "FieldL" + i, SecX + 10f, 0f, 290f, FieldRowH, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                fieldR[i] = FrontKit.Mono(b, "FieldR" + i, SecX + secW - 10f - 270f, 0f, 270f, FieldRowH, 10.5f, TextAlignmentOptions.MidlineRight);
            }
            fieldNote = FrontKit.Mono(b, "FieldNote", SecX + 10f, 0f, secW - 20f, FieldRowH, 10.5f, TextAlignmentOptions.MidlineLeft);

            string[] filterHelp =
            {
                "Every revealed target.", "Their sites: uplinks, EW trucks and camps. SABOTAGE knocks one out.", "Buildings a team can SEIZE: held, a FOB can stand there.",
                "Relays and data centers: a TAP feeds CYBER their network (and a data center can be sabotaged).", "Contacts on the ground: LASE marks one for your strikes.",
            };
            float fw = (secW - 4f * 6f) / 5f;
            for (int i = 0; i < filterChips.Length; i++)
            {
                int f = i;
                filterChips[i] = Button(b, FilterWord[i], SecX + i * (fw + 6f), 0f, fw, 24f, AvButtonStyle.Default, () => { actions.Touch(); SetFilter(f); }, filterHelp[i]);
            }

            // Controls before the row hits: a press by words finds a button before a card that happens to say the same word.
            for (int i = 0; i < Missions; i++)
            {
                int slot = i;
                plates[i] = Button(b, "MISSION", 0f, 0f, 90f, PlateH, AvButtonStyle.Default, () => Do(() => PickMission(slot)), "");
            }
            for (int i = 0; i < MaxTeams; i++)
            {
                int slot = i;
                crew[i] = Button(b, "TEAM", 0f, 0f, 110f, PlateH, AvButtonStyle.Default, () => Do(() => PickTeam(crewSlot[slot])), "");
            }
            send = Button(b, "SEND", 0f, 0f, 190f, 26f, AvButtonStyle.Primary, () => Do(SendNow), "Sends the picked team on the picked mission at once: your allocation pays, the host judges.");
            clear = Button(b, "CLEAR", 0f, 0f, 74f, 20f, AvButtonStyle.Default, () => Do(ClearAim), "Drops this target (or the RECON point): nothing is sent.");
            sweep = Button(b, "SWEEP", SecX + secW - 8f - 160f, 0f, 160f, 24f, AvButtonStyle.Danger, () => Do(() => actions.Sweep()),
                "Send a QRF after every hostile team detected near our sites: it reaches them " + (int)SofRules.SweepSeconds + " s after the press, costs " + SofRules.SweepCost +
                " AP and hunts them for " + (int)SofRules.HuntSeconds + " s (their exposure climbs), then " + (int)SofRules.SweepCooldownSeconds + " s to refit.");

            for (int i = 0; i < MaxTargets; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "RowHit" + i, CardX, 0f, cardW, CardH, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left && rowId[slot] != 0) Do(() => PickTarget(rowId[slot])); };
                hit.gameObject.SetActive(false);
                rowHit[i] = hit;
                rowName[i] = FrontKit.Mono(b, "RowName" + i, 0f, 0f, 200f, 15f, 11f, TextAlignmentOptions.MidlineLeft, true);
                rowGrid[i] = FrontKit.Mono(b, "RowGrid" + i, 0f, 0f, 200f, 13f, 10f, TextAlignmentOptions.MidlineLeft);
                rowTag[i] = FrontKit.Mono(b, "RowTag" + i, 0f, 0f, 200f, CardH, 10.5f, TextAlignmentOptions.MidlineRight, true);
                rowName[i].gameObject.SetActive(false); rowGrid[i].gameObject.SetActive(false); rowTag[i].gameObject.SetActive(false);
            }

            fileTab = FrontKit.Mono(b, "FileTab", 0f, 0f, 112f, 13f, 10f, TextAlignmentOptions.Midline, true, 1f);
            fileA = FrontKit.Mono(b, "FileA", 0f, 0f, 300f, 16f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            fileB = FrontKit.Mono(b, "FileB", 0f, 0f, 300f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
            fileC = FrontKit.Mono(b, "FileC", 0f, 0f, 300f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
            fileD = FrontKit.Mono(b, "FileD", 0f, 0f, 300f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
            fileNote = FrontKit.Mono(b, "FileNote", 0f, 0f, 300f, 15f, 10f, TextAlignmentOptions.MidlineLeft);
            fileStamp = new Stamp(b, 0f, 0f, 92f, 22f, 6f);
            missionKey = Key(b, "MissionKey", "MISSION");
            teamKey = Key(b, "TeamKey", "TEAM");
            goKey = Key(b, "GoKey", "GO IN");
            missionNote = FrontKit.Mono(b, "MissionNote", 0f, 0f, 300f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
            crewNote = FrontKit.Mono(b, "CrewNote", 0f, 0f, 300f, PlateH, 10.5f, TextAlignmentOptions.MidlineLeft);
            goIn = FrontKit.Mono(b, "GoIn", 0f, 0f, 300f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            helo = FrontKit.Mono(b, "Helo", 0f, 0f, 300f, 15f, 10.5f, TextAlignmentOptions.MidlineLeft);
            sendNote = FrontKit.Mono(b, "SendNote", 0f, 0f, 300f, 30f, 10.5f, TextAlignmentOptions.MidlineLeft);
            sendNote.enableWordWrapping = true;

            listNote = FrontKit.Mono(b, "ListNote", CardX + 2f, 0f, cardW - 4f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            listHint = FrontKit.Mono(b, "ListHint", CardX + 2f, 0f, cardW - 4f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            listMore = FrontKit.Mono(b, "ListMore", CardX + 2f, 0f, cardW - 4f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            emptyHead = FrontKit.Mono(b, "EmptyHead", 0f, 0f, 260f, 26f, 12f, TextAlignmentOptions.Midline, true, 2f);
            guideHead = FrontKit.Mono(b, "GuideHead", CardX + 2f, 0f, cardW - 4f, 16f, 11f, TextAlignmentOptions.MidlineLeft, true, 1f);
            guideHead.text = "HOW A MISSION GOES";
            guideText = FrontKit.Mono(b, "GuideText", CardX + 2f, 0f, cardW - 4f, GuideH - 26f, 10.5f, TextAlignmentOptions.TopLeft);
            guideText.enableWordWrapping = true;
            guideText.text = "1 · PICK A TARGET HERE OR ON THE MAP, OR CLICK OPEN GROUND FOR AN AREA RECON\n"
                + "2 · THE NEAREST TEAM THAT CAN GO IS PICKED: ANOTHER IS ONE CLICK AWAY\n"
                + "3 · SEND: IT MARCHES AT " + Mathf.RoundToInt(SofRules.SpeedMetresPerSecond * 3.6f) + " KM/H, ROLLS ITS ODDS ON SITE, COMES HOME";
            hostHead = FrontKit.Mono(b, "HostHead", SecX + 10f, 0f, 260f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            hostLine = FrontKit.Mono(b, "HostLine", SecX + 10f, 0f, secW - 20f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
            hostLine.enableWordWrapping = true;
            cardTexts = new[] { fileTab, fileA, fileB, fileC, fileD, fileNote, missionKey, missionNote, teamKey, crewNote, goKey, goIn, helo, sendNote };
            HideCard();
            skin.Add(() => { fieldRight.color = RoomInk.Khaki; listMore.color = RoomInk.Khaki; guideHead.color = RoomInk.Gold; guideText.color = RoomInk.Khaki; });
        }

        private static TMP_Text Key(RectTransform b, string name, string word)
        {
            TMP_Text t = FrontKit.Mono(b, name, 0f, 0f, KeyW, PlateH, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
            t.text = word;
            return t;
        }

        /// <summary>A filter chip. The picked target stays on the list whatever the filter (only CLEAR drops a pick, ruling D-R5).</summary>
        private void SetFilter(int f)
        {
            targetFilter = f;
            listScroll = 0;
            if (last != null) PaintTargets(last);
        }

        /// <summary>A mission plate: the mission SEND will send (local; the view already priced every one).</summary>
        private void PickMission(int i)
        {
            if (lastView == null || i >= lastView.Missions.Count || !lastView.Missions[i].Valid) return;
            sendKind = lastView.Missions[i].Kind;
            PaintTargets(last);
        }

        /// <summary>SEND: the picked team on the picked mission at the aim, at once; nothing is kept back to send later (bug 4).</summary>
        private void SendNow()
        {
            ShadowRoomView s = lastView;
            if (s == null || !canSend || s.SelectedSlot < 0) return;
            int aim = Aim(s);
            if (aim == 0) return;
            s.Reason = "";
            actions.TeamMission(s.SelectedSlot, (int)sendKind, aim);
        }

        /// <summary>CLEAR, the only way to drop the aim (ruling D-R5): the target pick or the RECON point goes, nothing is sent.</summary>
        private void ClearAim()
        {
            if (lastView == null) return;
            lastView.HasPoint = false;
            lastView.Reason = "";
            actions.SelectTarget(0);
        }

        /// <summary>The aim the card sends at: the picked target's id, else the RECON point's (negative) id, else 0.</summary>
        private static int Aim(ShadowRoomView s) => s.SelectedTargetId != 0 ? s.SelectedTargetId : s.HasPoint ? s.PointId : 0;

        private bool Shows(ShadowRoomView s, int i) => targetFilter == 0 || s.Targets[i].Filter == targetFilter || s.Targets[i].Selected;

        // ---- Paint ------------------------------------------------------------------------------------------------------

        private void PaintTargets(FrontRoomView v)
        {
            ShadowRoomView s = v.Shadow;
            FrontVector g = listG, m = fileMarks;
            g.Clear();
            m.Clear();
            float cw = targets.Width, secW = cw - 2f * SecX, cardW = cw - 2f * CardX;
            targets.SetMeta(s.Active ? s.Targets.Count + " REVEALED · " + s.Field.Count + " OUT" : s.Meta, s.Targets.Count > 0 ? FrontKit.Tone(AvState.Danger) : RoomInk.Gold.WithAlpha(0.8f));

            float y = PaintField(s, g, FieldTop, secW) + 6f;

            // Filter chips with their counts.
            for (int f = 0; f < filterCount.Length; f++) filterCount[f] = 0;
            foreach (SofTargetView t in s.Targets) { filterCount[0]++; filterCount[t.Filter]++; }
            float fw = (secW - 4f * 6f) / 5f;
            for (int f = 0; f < filterChips.Length; f++)
            {
                AvControl c = filterChips[f];
                OpsKit.Label(c, FilterWord[f] + " " + filterCount[f]);
                c.Latched = targetFilter == f;
                Put(c.Rect, SecX + f * (fw + 6f), y, fw, 24f);
            }
            y += 30f;

            // The aim this view was built for: a point clicked a frame ago waits one rebuild for its words, so its card never opens half empty.
            int aim = Aim(s);
            bool open = aim != 0 && s.AimSeen == aim && s.Missions.Count > 0;
            listCount = 0;
            if (open && s.SelectedTargetId == 0) vis[listCount++] = -1;
            for (int i = 0; i < s.Targets.Count && i < MaxTargets; i++) if (Shows(s, i)) vis[listCount++] = i;

            // The well from here to HOSTILES at the foot; the cards that fit, the open one whole (a new aim is brought into view).
            float hostY = targets.Height - 8f - HostH, bottom = hostY - 6f, top = y + 6f;
            bool notes = s.Reason.Length > 0 && aim == 0;
            if (notes) top += 22f;
            Slab(g, SecX, y, secW, bottom - y);
            int openAt = -1;
            for (int k = 0; k < listCount; k++) if (IsOpen(s, vis[k], open)) openAt = k;
            int openAim = open ? aim : 0;
            bool follow = openAim != followAim; // a newly opened card is brought into view whole (the wheel may take it out again)
            followAim = openAim;
            listScroll = Mathf.Clamp(listScroll, 0, Math.Max(0, listCount - 1));
            int shown = Measure(s, open, listScroll, top, bottom - 24f, out listBelow);
            if (follow && openAt >= 0 && (openAt < listScroll || openAt >= listScroll + shown))
            {
                listScroll = openAt;
                while (listScroll > 0 && openAt < listScroll - 1 + Measure(s, open, listScroll - 1, top, bottom - 24f, out _)) listScroll--;
                shown = Measure(s, open, listScroll, top, bottom - 24f, out listBelow);
            }

            Show(listNote, notes);
            if (notes)
            {
                Put(listNote.rectTransform, CardX + 2f, y + 6f, cardW - 4f, 18f);
                FrontKit.Set(listNote, C2Kit.FitTo(listNote, s.Reason + " · PICK ANOTHER", cardW - 4f), RoomInk.Gold);
            }

            float ry = top;
            bool cardShown = false;
            int r = 0;
            for (int k = listScroll; k < listScroll + shown; k++)
            {
                int ti = vis[k];
                bool isOpen = IsOpen(s, ti, open);
                Row(s, ti, r++, ry, cardW, isOpen, g, m);
                if (isOpen) { PaintCard(v, s, ry + CardH, cardW, m); cardShown = true; }
                ry += CardH + (isOpen ? BodyH : 0f) + CardGap;
            }
            for (int i = r; i < MaxTargets; i++)
            {
                Show(rowHit[i], false); Show(rowName[i], false); Show(rowGrid[i], false); Show(rowTag[i], false);
                Show(rowThumb[i], false);
            }
            if (!cardShown) HideCard();

            // Under the cards: rows hidden above or below, why the list is empty, and the area RECON hint.
            bool more = listScroll > 0 || listBelow;
            Show(listMore, more);
            if (more)
            {
                Put(listMore.rectTransform, CardX + 2f, ry, cardW - 4f, 14f);
                FrontKit.Set(listMore, "CARDS " + (listScroll + 1) + "-" + (listScroll + shown) + " OF " + listCount + " · WHEEL SCROLLS", RoomInk.Khaki);
                ry += 16f;
            }
            bool empty = listCount == 0;
            string hint = !s.Active ? (s.Meta == FrontViews.NoCamp ? "BUILD A CAMP ON TEAMS & BASES, THEN TRAIN A TEAM" : "THE HOST HAS NOT SPOKEN YET")
                : s.Targets.Count == 0 ? (s.Teams.Count > 0 ? ReconHint : "TRAIN A TEAM ON TEAMS & BASES, THEN RECON GROUND")
                : empty ? "ALL SHOWS EVERY ONE" : s.HasPoint ? "" : "OR " + ReconHint;
            Show(emptyHead, empty);
            if (empty)
            {
                // An empty card tray: three dashed slots and a label slip pinned over them, what fills it under the slip.
                for (int i = 0; i < 3; i++)
                {
                    float sy = ry + i * (CardH + CardGap);
                    g.Rect(CardX, sy, cardW, CardH, Paper.Khaki.WithAlpha(0.08f)); // a tint (alpha under 0.2), not a ground
                    Hand.DashRect(g, CardX + 0.5f, sy + 0.5f, cardW - 1f, CardH - 1f, 1f, Paper.Khaki.WithAlpha(0.5f));
                }
                const float sw = 300f;
                float sx = CardX + (cardW - sw) * 0.5f, slipY = ry + (3f * (CardH + CardGap) - 26f) * 0.5f;
                g.Rect(sx + 2f, slipY + 2f, sw, 26f, Paper.Shadow);
                g.Rect(sx, slipY, sw, 26f, Paper.Card);
                g.Frame(sx, slipY, sw, 26f, 1f, Paper.CardEdge);
                Hand.Pin(g, new Vector2(sx + 11f, slipY + 13f), 3.6f, Paper.Grease);
                Put(emptyHead.rectTransform, sx + 20f, slipY, sw - 24f, 26f);
                FrontKit.Set(emptyHead, !s.Active ? (s.Meta == FrontViews.NoCamp ? "NO CAMP YET" : "WAITING FOR THE HOST")
                    : s.Targets.Count == 0 ? "NO TARGETS REVEALED" : "NONE UNDER " + FilterWord[targetFilter], Paper.Pen);
                ry += 3f * (CardH + CardGap) + 2f;
            }
            Show(listHint, hint.Length > 0 && ry + 18f <= bottom);
            if (hint.Length > 0)
            {
                Put(listHint.rectTransform, CardX + 2f, ry + 2f, cardW - 4f, 16f);
                listHint.alignment = empty ? TextAlignmentOptions.Midline : TextAlignmentOptions.MidlineLeft;
                FrontKit.Set(listHint, C2Kit.FitTo(listHint, hint, cardW - 4f), empty ? RoomInk.Gold : RoomInk.Khaki);
                ry += 20f;
            }

            // How a mission goes, in the room the list leaves at the foot of the well.
            float guideY = bottom - GuideH - 4f;
            bool guide = s.Active && !open && ry + 8f <= guideY;
            Show(guideHead, guide); Show(guideText, guide);
            if (guide)
            {
                g.Rect(CardX, guideY, cardW, 1f, RoomInk.Gold.WithAlpha(0.35f));
                Put(guideHead.rectTransform, CardX + 2f, guideY + 6f, cardW - 4f, 16f);
                Put(guideText.rectTransform, CardX + 2f, guideY + 24f, cardW - 4f, GuideH - 26f);
            }

            PaintHostiles(s, g, hostY, secW);
            g.Flush();
            m.Flush();
        }

        private static bool IsOpen(ShadowRoomView s, int ti, bool open) => open && (ti < 0 || s.Targets[ti].Selected);

        /// <summary>How many cards from <paramref name="from"/> fit between <paramref name="top"/> and <paramref name="bottom"/> (the first always does), and whether more follow.</summary>
        private int Measure(ShadowRoomView s, bool open, int from, float top, float bottom, out bool below)
        {
            float ry = top;
            int n = 0;
            below = false;
            for (int k = from; k < listCount; k++)
            {
                float h = CardH + (IsOpen(s, vis[k], open) ? BodyH : 0f);
                if (n > 0 && ry + h > bottom) { below = true; break; }
                ry += h + CardGap;
                n++;
            }
            return n;
        }

        /// <summary>IN THE FIELD: every team out of camp, its mission, state and exposure; the strip says who is ready when none is out. Returns its bottom.</summary>
        private float PaintField(ShadowRoomView s, FrontVector g, float y, float secW)
        {
            int n = Math.Min(s.Field.Count, MaxTeams), lines = Math.Max(1, n);
            float h = 26f + lines * FieldRowH + 6f;
            Slab(g, SecX, y, secW, h);
            Put(fieldHead.rectTransform, SecX + 10f, y + 5f, 200f, 18f);
            Put(fieldRight.rectTransform, SecX + secW - 10f - 260f, y + 5f, 260f, 18f);
            FrontKit.Set(fieldHead, "IN THE FIELD " + s.Field.Count, RoomInk.Gold);
            for (int i = 0; i < MaxTeams; i++)
            {
                bool on = i < n;
                Show(fieldL[i], on); Show(fieldR[i], on);
                if (!on) continue;
                SofFieldView f = s.Field[i];
                float ry = y + 26f + i * FieldRowH;
                Put(fieldL[i].rectTransform, SecX + 10f, ry, 290f, FieldRowH);
                Put(fieldR[i].rectTransform, SecX + secW - 10f - 270f, ry, 270f, FieldRowH);
                FrontKit.Set(fieldL[i], C2Kit.FitTo(fieldL[i], f.Left, 290f), Tone(f.Tone));
                FrontKit.Set(fieldR[i], C2Kit.FitTo(fieldR[i], f.Right, 270f), RoomInk.FieldInk);
            }
            Show(fieldNote, n == 0);
            if (n == 0)
            {
                Put(fieldNote.rectTransform, SecX + 10f, y + 26f, secW - 20f, FieldRowH);
                FrontKit.Set(fieldNote, C2Kit.FitTo(fieldNote, s.FieldNote, secW - 20f), RoomInk.Khaki);
            }
            return y + h;
        }

        /// <summary>One index card: shadow, cream stock, a red margin, a punched hole, the recon print (or the kind circled in china marker), name, grid and the tag.</summary>
        private void Row(ShadowRoomView s, int ti, int slot, float y, float w, bool isOpen, FrontVector g, FrontVector m)
        {
            float x = CardX;
            bool point = ti < 0;
            SofTargetView t = point ? default : s.Targets[ti];
            Image hit = rowHit[slot];
            Show(hit, true);
            Put(hit.rectTransform, x, y, w, CardH);
            rowId[slot] = point ? 0 : t.Id;
            float h = CardH + (isOpen ? BodyH : 0f);
            g.Rect(x + 1.5f, y + 2f, w, h, Paper.Shadow);
            g.Rect(x, y, w, CardH, Paper.Card);
            g.Rect(x + 3f, y + 15f, w - 6f, 1f, RoomInk.Rgba(0.35f, 0.5f, 0.68f, 0.17f)); // ruling: a tint (alpha under 0.2), not a ground
            g.Rect(x + 48f, y + 1f, 1f, CardH - 2f, RoomInk.Rgba(0.78f, 0.25f, 0.2f, 0.3f));
            if (isOpen) g.Rect(x, y + CardH, w, BodyH, Paper.Folder); // under the dossier sheet
            g.Frame(x, y, w, h, isOpen ? 2f : 1f, isOpen ? Paper.Pen : Paper.CardEdge);
            Hand.Hole(g, x + 6.5f, y + 6.5f, 2.3f);

            RawImage thumb = rowThumb[slot];
            Texture2D print = point || thumb == null ? null : FrontArt.Get(PrintOf(t));
            Show(thumb, print != null);
            Vector2 gp = new Vector2(x + 25f, y + CardH * 0.5f);
            if (print != null)
            {
                thumb.texture = print;
                thumb.color = t.Resisted ? PrintTint.WithAlpha(0.45f) : PrintTint;
                FrontArt.Place(thumb, x + 4f, y + 3f, 42f, CardH - 6f);
                m.Frame(x + 3f, y + 2f, 44f, CardH - 4f, 1f, Paper.PhotoPaper);
            }
            else if (point)
            {
                Hand.Circle(g, gp, 11f, 1.8f, Paper.Grease, 5);
                FrontKit.Cross(g, gp, 4f, Paper.Pen);
            }
            else
            {
                Color mark = t.Resisted ? Paper.PenDim : Paper.Grease;
                Hand.Circle(g, gp, 12f, 1.8f, mark, t.Id * 5 + 1);
                TargetGlyph(g, t.Kind, gp, 5f, Paper.Pen);
            }
            FrontVector mk = print != null ? m : g;
            if (!point && t.Resisted) mk.Line(gp + new Vector2(-12f, 9f), gp + new Vector2(12f, -9f), 1.8f, Paper.Grease);
            if (!point && t.Exploit) mk.Disc(new Vector2(x + 41f, y + 8f), 3f, Paper.Amber, 10);
            if (isOpen) Hand.Clip(g, x + w - 9f, y + 9f);

            float tagW = 200f, nw = w - 56f - tagW - 14f;
            TMP_Text name = rowName[slot], grid = rowGrid[slot], tag = rowTag[slot];
            Show(name, true); Show(grid, true); Show(tag, true);
            Put(name.rectTransform, x + 54f, y + 1f, nw, 15f);
            Put(grid.rectTransform, x + 54f, y + 15f, nw, 13f);
            Put(tag.rectTransform, x + w - 18f - tagW, y, tagW, CardH);
            FrontKit.Set(name, C2Kit.FitTo(name, point ? "RECON POINT" : t.Name, nw), Paper.Pen);
            FrontKit.Set(grid, point ? s.FileB : "GRID " + t.Grid, Paper.PenDim);
            Color tk = point ? Paper.Olive : t.TagTone == AvState.Ready ? Paper.Olive : t.TagTone == AvState.Caution ? Paper.Amber : t.TagTone == AvState.Inert ? Paper.PenDim : Paper.Blue;
            FrontKit.Set(tag, C2Kit.FitTo(tag, point ? "AREA RECON" : t.Tag, tagW), tk);
        }

        /// <summary>
        /// The open card's body under its header at <paramref name="by"/>: the dossier (print, file lines, a stamp on an exploit or resisted print, CLEAR), the missions,
        /// the teams, how it goes in, then SEND with the reason it is dark or what one press does.
        /// </summary>
        private void PaintCard(FrontRoomView v, ShadowRoomView s, float by, float w, FrontVector m)
        {
            float x = CardX;
            SofTargetView pick = default;
            bool have = false;
            foreach (SofTargetView t in s.Targets) if (t.Selected) { pick = t; have = true; }
            Show(filePaper, true);
            if (filePaper != null) FrontArt.Place(filePaper, x + 1f, by, w - 2f, BodyH - 1f);
            m.Rect(x + 1f, by, w - 2f, 1f, Paper.FolderEdge);

            // The dossier.
            var photo = new Rect(x + 10f, by + 8f, 112f, 78f);
            Texture2D print = have ? FrontArt.Get(PrintOf(pick)) : null;
            bool printed = print != null && filePhoto != null;
            Show(filePhoto, printed);
            if (printed) { filePhoto.texture = print; filePhoto.color = pick.Resisted ? PrintTint.WithAlpha(0.45f) : PrintTint; FrontArt.Place(filePhoto, photo.x, photo.y, photo.width, photo.height); }
            Hand.Recon(m, photo, have ? pick.Id : 3, have ? pick.Kind : TargetKind.Ground, have, printed);
            if (!have) Hand.Circle(m, photo.center, 18f, 1.8f, Paper.Grease, 5); // a bare ground point: the mark on a blank print
            Show(fileTab, true);
            Put(fileTab.rectTransform, photo.x, photo.yMax + 3f, photo.width, 13f);
            FrontKit.Set(fileTab, s.FileTab, Paper.PenDim);
            float tx = photo.xMax + 12f, tw = x + w - 10f - tx;
            Show(clear.Rect, true);
            Put(clear.Rect, x + w - 10f - 74f, by + 6f, 74f, 20f);
            HolePlate(m, x + w - 10f - 74f, by + 6f, 74f, 20f, true);
            Line(fileA, tx, by + 6f, tw - 84f, 16f, s.FileA, Paper.Pen);
            Line(fileB, tx, by + 24f, tw - 84f, 15f, s.FileB, Paper.Blue);
            Line(fileC, tx, by + 40f, tw, 15f, s.FileC, Paper.PenDim);
            Line(fileD, tx, by + 56f, tw, 15f, s.FileD, Paper.Amber);
            Line(fileNote, tx, by + 72f, tw, 15f, have ? AnalystNote(pick.Kind, pick.Exploit, pick.Resisted) : "MARKED ON THE MAP · ANY TEAM CAN GO", Paper.PenDim);
            if (have && (pick.Exploit || pick.Resisted))
            {
                fileStamp.Place(photo.x + 12f, photo.y + photo.height - 26f);
                fileStamp.Set(pick.Resisted ? "RESISTED" : "EXPLOIT", pick.Resisted ? Paper.Red : Paper.Amber);
            }
            else fileStamp.Set("", Paper.Pen);

            // The missions: the ones the aim takes lit, the chosen one latched; a new aim opens on its best one.
            int aim = Aim(s);
            if (aim != sendFor) { sendFor = aim; sendKind = MissionKind.None; }
            int chosen = -1;
            for (int i = 0; i < s.Missions.Count; i++) if (s.Missions[i].Valid && s.Missions[i].Kind == sendKind) chosen = i;
            for (int i = 0; i < s.Missions.Count && chosen < 0; i++) if (s.Missions[i].Valid && s.Missions[i].Best) chosen = i;
            for (int i = 0; i < s.Missions.Count && chosen < 0; i++) if (s.Missions[i].Valid) chosen = i;
            if (chosen >= 0) sendKind = s.Missions[chosen].Kind;
            float y = by + 104f, px = x + 10f + KeyW, pw = (w - 20f - KeyW - 4f * 5f) / Missions;
            Show(missionKey, true);
            Put(missionKey.rectTransform, x + 10f, y, KeyW, PlateH);
            missionKey.color = Paper.PenDim;
            for (int i = 0; i < Missions; i++)
            {
                bool on = i < s.Missions.Count;
                AvControl c = plates[i];
                Show(c.Rect, on);
                if (!on) continue;
                SofMissionView mv = s.Missions[i];
                float bx = px + i * (pw + 5f);
                Put(c.Rect, bx, y, pw, PlateH);
                OpsKit.Label(c, mv.Name);
                OpsKit.Enable(c, mv.Valid);
                c.Latched = i == chosen;
                HolePlate(m, bx, y, pw, PlateH, mv.Valid);
                string help = mv.Valid ? mv.Name + ": " + mv.Note + ". " + mv.Odds + ", " + mv.Ap + " AP. Picks it; SEND sends it." : mv.Name + ": " + mv.Note + ".";
                if (plateHelp[i] != help) { plateHelp[i] = help; c.Help = help; }
            }
            SofMissionView pickM = chosen >= 0 ? s.Missions[chosen] : default;
            Line(missionNote, px, y + PlateH + 4f, w - 20f - KeyW, 15f,
                chosen >= 0 ? pickM.Name + " · " + pickM.Note + " · " + pickM.Odds + " · " + pickM.Ap + " AP" + (pickM.Exploit ? " (EXPLOIT)" : "") : "NO MISSION TAKES THIS AIM", Paper.Pen);

            // The teams the card can send: the picked one latched (a new aim pre-picked the nearest that can go).
            y += PlateH + 24f;
            Show(teamKey, true);
            Put(teamKey.rectTransform, x + 10f, y, KeyW, PlateH);
            teamKey.color = Paper.PenDim;
            float tcw = (w - 20f - KeyW - 3f * 6f) / MaxTeams;
            for (int i = 0; i < MaxTeams; i++)
            {
                bool on = i < s.Crew.Count;
                AvControl c = crew[i];
                Show(c.Rect, on);
                if (!on) continue;
                SofCrewView cv = s.Crew[i];
                crewSlot[i] = cv.Slot;
                float bx = px + i * (tcw + 6f);
                Put(c.Rect, bx, y, tcw, PlateH);
                OpsKit.Label(c, cv.Word);
                c.Latched = cv.Slot == s.SelectedSlot;
                HolePlate(m, bx, y, tcw, PlateH, true);
                if (crewHelp[i] != cv.Help) { crewHelp[i] = cv.Help; c.Help = cv.Help; }
            }
            Show(crewNote, s.Crew.Count == 0);
            if (s.Crew.Count == 0) Line(crewNote, px, y, w - 20f - KeyW, PlateH, "NO TEAM ON THE ROSTER", Paper.PenDim);

            // How it goes in (ruling D-R2: GROUND is the insertion you pick; a helicopter is the LIFT verb, a pilot's to fly).
            y += PlateH + 8f;
            Show(goKey, true);
            Put(goKey.rectTransform, x + 10f, y - 3f, KeyW, PlateH);
            goKey.color = Paper.PenDim;
            Line(goIn, px, y, w - 20f - KeyW, 15f, s.GoIn.Length > 0 ? s.GoIn : "NO TEAM PICKED", Paper.Pen);
            Line(helo, px, y + 16f, w - 20f - KeyW, 15f, s.Helo, Paper.PenDim);

            // SEND: lit only when the picked team can go now and the pilot can pay; dark with the reason otherwise.
            y += 40f;
            string call = s.SelectedSlot >= 0 ? SofRules.Callsign(s.SelectedSlot) : "";
            int ap = chosen >= 0 ? pickM.Ap : 0;
            canSend = chosen >= 0 && s.SelectedSlot >= 0 && s.SendWhy.Length == 0 && v.Allocation >= ap;
            Show(send.Rect, true);
            Put(send.Rect, x + 10f, y, 190f, 26f);
            OpsKit.Label(send, call.Length > 0 && chosen >= 0 ? "SEND " + call + " · " + pickM.Name : "SEND");
            send.SetStyle(canSend ? AvButtonStyle.Primary : AvButtonStyle.Default);
            OpsKit.Enable(send, canSend);
            HolePlate(m, x + 10f, y, 190f, 26f, canSend);
            string why = canSend ? "ONE PRESS SENDS " + call + " NOW · " + ap + " AP OF YOUR " + v.Allocation
                : s.SendWhy.Length > 0 ? s.SendWhy : chosen < 0 ? "NO MISSION TAKES THIS AIM" : "NEEDS " + ap + " AP · YOU HAVE " + v.Allocation;
            Show(sendNote, true);
            Put(sendNote.rectTransform, x + 210f, y - 2f, w - 220f, 30f);
            FrontKit.Set(sendNote, why, canSend ? Paper.Olive : Paper.Red);
        }

        private static void Show(Component c, bool on) { if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on); }

        /// <summary>Places a rect only when it moved (a repaint lays out few things).</summary>
        private static void Put(RectTransform t, float x, float y, float w, float h)
        {
            Vector2 p = t.anchoredPosition, s = t.sizeDelta;
            if (p.x != x || p.y != -y || s.x != w || s.y != h) AvLay.Place(t, x, y, w, h);
        }

        /// <summary>A typed line of the open card, cut to its width.</summary>
        private static void Line(TMP_Text t, float x, float y, float w, float h, string s, Color ink)
        {
            Show(t, true);
            Put(t.rectTransform, x, y, w, h);
            FrontKit.Set(t, C2Kit.FitTo(t, s ?? "", w), ink);
        }

        private void HideCard()
        {
            Show(filePaper, false); Show(filePhoto, false); fileStamp.Set("", Paper.Pen);
            foreach (TMP_Text t in cardTexts) Show(t, false);
            foreach (AvControl c in plates) Show(c.Rect, false);
            foreach (AvControl c in crew) Show(c.Rect, false);
            Show(send.Rect, false); Show(clear.Rect, false);
            canSend = false;
        }

        /// <summary>HOSTILES NEAR US: how many enemy teams our sites detect, what the QRF does, and SWEEP (its label carries the countdown or the refit).</summary>
        private void PaintHostiles(ShadowRoomView s, FrontVector g, float y, float secW)
        {
            Slab(g, SecX, y, secW, HostH);
            Put(hostHead.rectTransform, SecX + 10f, y + 5f, 260f, 18f);
            Put(sweep.Rect, SecX + secW - 8f - 160f, y + 4f, 160f, 24f);
            Put(hostLine.rectTransform, SecX + 10f, y + 32f, secW - 20f, 30f);
            bool hot = s.QrfTone == AvState.Danger;
            FrontKit.Set(hostHead, "HOSTILES NEAR US " + s.Near, hot ? AvInk.State(AvState.Danger) : RoomInk.Gold);
            FrontKit.Set(hostLine, s.Active ? s.QrfLine : "NO QRF · " + (s.Meta == FrontViews.NoCamp ? "BUILD A CAMP FIRST" : "WAITING FOR THE HOST"), hot ? AvInk.State(AvState.Danger) : RoomInk.Khaki);
            OpsKit.Label(sweep, s.SweepLabel);
            OpsKit.Enable(sweep, s.CanSweep);
        }

        /// <summary>Cosmetic flavour: an analyst's line per target kind (no reading of its own).</summary>
        private static string AnalystNote(TargetKind kind, bool exploit, bool resisted)
        {
            string note = kind == TargetKind.Ground ? "PATROL ROUTE UNCONFIRMED · LASE FROM A RIDGE"
                : kind == TargetKind.Anchor ? "HARDENED SITE · HIT IT FROM THE FLANK"
                : kind == TargetKind.Building ? "SEIZE AND HOLD · EXPECT A COUNTERATTACK"
                : "TAP POINT · KEEP THE TEAM QUIET ON SITE";
            return resisted ? "SPACE BEATS SOF HERE · LEAVE IT TO THE SATELLITES" : exploit ? note + " · SOFT TARGET" : note;
        }

        private static readonly Color PrintTint = RoomInk.Rgb(0xFFF7E6); // a warm photographic print, not a screen

        /// <summary>The recon print for a target: by kind, an anchor by its sub-kind (cosmetic only).</summary>
        private static string PrintOf(SofTargetView t)
        {
            AnchorSub sub = t.Sub;
            return "ops_recon_" + (t.Kind == TargetKind.Ground ? "ground" : t.Kind == TargetKind.Building ? "building" : t.Kind == TargetKind.Relay ? "relay"
                : sub == AnchorSub.EwTruck ? "ewtruck" : sub == AnchorSub.DataCenter ? "datacenter" : sub == AnchorSub.Camp ? "camp" : "uplink");
        }
    }
}
