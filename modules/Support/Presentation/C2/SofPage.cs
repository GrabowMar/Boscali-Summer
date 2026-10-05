using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>The five requests the SOF page can make; every press is a request, never an effect (the host judges it).</summary>
    internal sealed class SofPageActions
    {
        public Action Raise;
        public Action<int, TeamVerb> Order;
        public Action<int, float, float> Divert;
        public Action<int, MissionKind, int, float, float> Mission;
        /// <summary>Arms a right-click on the map and reports the world point; false when the map input is busy.</summary>
        public Func<string, Action<float, float>, bool> Pick;
    }

    /// <summary>
    /// The [4] SOF tab (spec 6): the AO MAP (camps, held buildings, own teams with their routes, revealed enemy teams, mission targets), the team roster with an exposure bar
    /// each, the orders strip (RAISE, PUSH, HOLD, DIVERT, EXFIL, LIFT, STOP), the mission strip (RECON, LASE, SABOT, SEIZE, TAP, PICK) with the selected target's cost and odds,
    /// and the SOF CALL rows (JTAC LASE / UNLASE, FORTIFY bound exactly as the CAP page binds them). Painted only from the faction mirror (<see cref="CapView.Sof"/>): the page
    /// never reads a game object. PICK arms the existing support map gesture (a right-click on the maximised map) and offers SEND TEAM HERE for the nearest revealed target or a point.
    /// </summary>
    internal sealed class SofPage
    {
        private const float Gap = 6f, Pad = 8f, ButtonH = 24f;
        private const int Targets = SofWire.MaxTargets, Slots = SofRules.MaxTeams;
        private readonly float width;
        private readonly bool full;
        private readonly Action<AvPart> register;
        private readonly SofPageActions act;
        private readonly C2ConsoleView console;
        private readonly C2Box map, teamsBox, callsBox;
        private readonly float mapW, mapH, rowH;
        private readonly Image[] gridLines;
        private readonly AvLineGraphic[] routes = new AvLineGraphic[Slots];
        private readonly AvControl[] targetMarks = new AvControl[Targets];
        private readonly int[] targetIds = new int[Targets];
        private readonly Image[] targetSelect = new Image[Targets];
        private readonly AvControl[] teamMarks = new AvControl[Slots];
        private readonly Image[] campDots = new Image[CampRules.MaxCamps], heldDots = new Image[SofWire.MaxHeld], enemyDots = new Image[SofWire.MaxEnemies];
        private readonly TMP_Text[] campTags = new TMP_Text[CampRules.MaxCamps], heldTags = new TMP_Text[SofWire.MaxHeld];
        private readonly Image pickDot;
        private readonly TMP_Text headline, detail, infoLine, tapLine;
        private readonly AvControl[] rowButtons = new AvControl[Slots];
        private readonly TMP_Text[] rowText = new TMP_Text[Slots];
        private readonly Image[] rowBack = new Image[Slots], rowFill = new Image[Slots];
        private readonly AvControl raise, push, hold, divert, exfil, lift, stop, recon, lase, sabot, seize, tap, pick;
        private readonly List<C2Row> rows = new List<C2Row>(3);
        private readonly List<SupportActionId> ids = new List<SupportActionId>(3);
        private readonly List<Action> pinActions = new List<Action>(3);
        private readonly List<CapPage.RowMemo> memos = new List<CapPage.RowMemo>(3);
        private readonly CyberMapProjection projection = new CyberMapProjection();
        private readonly List<MapPoint> fit = new List<MapPoint>(48);
        private readonly List<Rect> placed = new List<Rect>(40);
        private SofStateData state;
        private int selectedTeam = -1, selectedTarget, paintedSeq = -1, paintedSecond = -1, paintedSelTeam = -2, paintedSelTarget = -1;
        private float pickX, pickZ, nextPress;
        private bool hasPick;
        private string standing = "SELECT A TEAM, THEN A TARGET · PICK ARMS A MAP CLICK";

        /// <summary>The footer words of this page: the standing hint, replaced by a refusal from the host.</summary>
        public string Words { get; private set; }

        public SofPage(RectTransform parent, float width, float height, CallsController calls, Action<AvPart> register, SofPageActions actions)
        {
            this.width = width;
            this.register = register ?? (_ => { });
            act = actions ?? new SofPageActions();
            full = height >= 560f;
            Words = standing;
            for (int i = 0; i < CallSheet.Rows.Count; i++) if (CallSheet.Rows[i].Family == CallFamily.Sof) ids.Add(CallSheet.Rows[i].Id);

            float gap = full ? Gap : 4f;
            int consoleLines = full ? 4 : 2;
            float consoleH = C2ConsoleView.HeightFor(consoleLines);
            rowH = full ? 18f : 15f;
            float callRowH = full ? 40f : 26f;
            float callsBody = ids.Count * (callRowH + 2f) + 2f;
            float teamsBody = Slots * rowH + 6f + 2f * (ButtonH + 4f) + (full ? 18f : 14f);
            float fixedH = gap + consoleH + gap + (C2Box.HeaderH + teamsBody) + gap + (C2Box.HeaderH + callsBody) + gap + C2Box.HeaderH + 2f;
            float mapBody = Mathf.Max(100f, height - fixedH);
            mapW = width - 2f; mapH = mapBody - 2f;

            float y = gap;
            console = Make(new C2ConsoleView(parent, consoleLines));
            console.Place(new AvSlot(0f, y, width, consoleH));
            y += consoleH + gap;

            map = Make(new C2Box(parent, "AO MAP"));
            map.BodyHeight = mapBody;
            map.SetMeta("NO CAMP STANDING");
            map.Place(new AvSlot(0f, y, width, C2Box.HeaderH + mapBody));
            y += C2Box.HeaderH + mapBody + gap;
            gridLines = BuildGrid(map.Body);
            for (int i = 0; i < routes.Length; i++) routes[i] = Line(map.Body, "Route" + i, 1.4f);
            for (int i = 0; i < campDots.Length; i++) { campDots[i] = Dot(map.Body, "Camp" + i); campTags[i] = Tag(map.Body, "CampTag" + i); }
            for (int i = 0; i < heldDots.Length; i++) { heldDots[i] = Dot(map.Body, "Held" + i); heldTags[i] = Tag(map.Body, "HeldTag" + i); }
            for (int i = 0; i < enemyDots.Length; i++) enemyDots[i] = Dot(map.Body, "Enemy" + i);
            pickDot = Dot(map.Body, "Pick");
            for (int i = 0; i < targetMarks.Length; i++)
            {
                int slot = i;
                targetSelect[i] = AvLay.Solid(map.Body, "Select" + i, Color.clear);
                targetSelect[i].raycastTarget = false;
                targetSelect[i].gameObject.SetActive(false);
                targetMarks[i] = AvControl.Make(map.Body, new AvControl.Spec("", () => PickTarget(targetIds[slot]), AvButtonStyle.Quiet));
                targetMarks[i].SingleLine();
                targetMarks[i].Rect.gameObject.SetActive(false);
            }
            for (int i = 0; i < teamMarks.Length; i++)
            {
                int slot = i;
                teamMarks[i] = AvControl.Make(map.Body, new AvControl.Spec("", () => PickTeam(slot), AvButtonStyle.Default));
                teamMarks[i].SingleLine();
                teamMarks[i].Rect.gameObject.SetActive(false);
            }
            headline = C2Kit.Mono(map.Body, "Headline", 12f, TextAlignmentOptions.Center, true, 2f);
            detail = C2Kit.Mono(map.Body, "Detail", 10f, TextAlignmentOptions.Center);
            C2Kit.Place(headline, 4f, mapH * 0.5f - 22f, mapW - 8f, 18f);
            C2Kit.Place(detail, 4f, mapH * 0.5f - 2f, mapW - 8f, 16f);

            teamsBox = Make(new C2Box(parent, "TEAMS & MISSIONS"));
            teamsBox.BodyHeight = teamsBody;
            teamsBox.SetMeta("NONE");
            teamsBox.Place(new AvSlot(0f, y, width, C2Box.HeaderH + teamsBody));
            y += C2Box.HeaderH + teamsBody + gap;
            float iw = width - 2f;
            for (int i = 0; i < Slots; i++)
            {
                int slot = i;
                float ry = 2f + i * rowH;
                // The click target goes first so the text and the exposure bar sit on top of its fill; none of them takes a click.
                rowButtons[i] = AvControl.Make(teamsBox.Body, new AvControl.Spec("", () => PickTeam(slot), AvButtonStyle.Quiet));
                rowButtons[i].SingleLine();
                AvLay.Place(rowButtons[i].Rect, 1f, ry, iw - 2f, rowH);
                rowBack[i] = AvLay.Solid(teamsBox.Body, "RowBack" + i, Color.clear);
                rowFill[i] = AvLay.Solid(teamsBox.Body, "RowFill" + i, Color.clear);
                rowBack[i].raycastTarget = false; rowFill[i].raycastTarget = false;
                AvLay.Place(rowBack[i].rectTransform, iw - Pad - 60f, ry + rowH * 0.5f - 2f, 60f, 4f);
                AvLay.Place(rowFill[i].rectTransform, iw - Pad - 60f, ry + rowH * 0.5f - 2f, 0f, 4f);
                rowText[i] = C2Kit.Mono(teamsBox.Body, "Row" + i, 10.5f, TextAlignmentOptions.MidlineLeft);
                rowText[i].raycastTarget = false;
                C2Kit.Place(rowText[i], Pad, ry, iw - 2f * Pad - 68f, rowH);
            }
            float by = 2f + Slots * rowH + 4f;
            infoLine = C2Kit.Mono(teamsBox.Body, "Info", 10f, TextAlignmentOptions.MidlineLeft, true);
            C2Kit.Place(infoLine, Pad, by, iw - 2f * Pad, full ? 18f : 14f);
            tapLine = C2Kit.Mono(teamsBox.Body, "Tap", 10f, TextAlignmentOptions.MidlineRight);
            C2Kit.Place(tapLine, Pad, by, iw - 2f * Pad, full ? 18f : 14f);
            by += (full ? 18f : 14f) + 2f;
            float bw = Mathf.Floor((iw - 2f * Pad - 6f * 4f) / 7f), bw6 = Mathf.Floor((iw - 2f * Pad - 5f * 4f) / 6f);
            raise = Button(teamsBox.Body, "RAISE", AvButtonStyle.Primary, Pad, by, bw, () => Press(() => act.Raise?.Invoke()));
            push = Button(teamsBox.Body, "PUSH", AvButtonStyle.Default, Pad + (bw + 4f), by, bw, () => Press(() => Order(TeamVerb.Push)));
            hold = Button(teamsBox.Body, "HOLD", AvButtonStyle.Default, Pad + 2 * (bw + 4f), by, bw, () => Press(() => Order(TeamVerb.Hold)));
            divert = Button(teamsBox.Body, "DIVERT", AvButtonStyle.Default, Pad + 3 * (bw + 4f), by, bw, () => Press(Divert));
            exfil = Button(teamsBox.Body, "EXFIL", AvButtonStyle.Default, Pad + 4 * (bw + 4f), by, bw, () => Press(() => Order(TeamVerb.Exfil)));
            lift = Button(teamsBox.Body, "LIFT", AvButtonStyle.Default, Pad + 5 * (bw + 4f), by, bw, () => Press(() => Order(TeamVerb.Lift)));
            stop = Button(teamsBox.Body, "STOP", AvButtonStyle.Quiet, Pad + 6 * (bw + 4f), by, bw, () => Press(() => Order(TeamVerb.Cancel)));
            by += ButtonH + 4f;
            recon = Button(teamsBox.Body, "RECON", AvButtonStyle.Default, Pad, by, bw6, () => Press(() => Mission(MissionKind.Recon)));
            lase = Button(teamsBox.Body, "LASE", AvButtonStyle.Default, Pad + (bw6 + 4f), by, bw6, () => Press(() => Mission(MissionKind.Lase)));
            sabot = Button(teamsBox.Body, "SABOT", AvButtonStyle.Default, Pad + 2 * (bw6 + 4f), by, bw6, () => Press(() => Mission(MissionKind.Sabotage)));
            seize = Button(teamsBox.Body, "SEIZE", AvButtonStyle.Default, Pad + 3 * (bw6 + 4f), by, bw6, () => Press(() => Mission(MissionKind.Seize)));
            tap = Button(teamsBox.Body, "TAP", AvButtonStyle.Default, Pad + 4 * (bw6 + 4f), by, bw6, () => Press(() => Mission(MissionKind.Tap)));
            pick = Button(teamsBox.Body, "PICK", AvButtonStyle.Primary, Pad + 5 * (bw6 + 4f), by, bw6, () => Press(ArmPick));
            raise.Help = "Raise a team at a standing camp: 60 CR, 1:30 to deploy (1.5x slower while the camp is damaged). Two teams per faction, three at 5 or more humans.";
            push.Help = "PUSH: the selected team moves 1.5x faster but its exposure rises 50 % faster. Press again to stop pushing.";
            hold.Help = "HOLD: the selected team stops and its exposure falls twice as fast. Press again to move on.";
            divert.Help = "DIVERT: send the selected team to the picked point (or the selected target). It abandons its mission.";
            exfil.Help = "EXFIL: the selected team returns to the nearest camp or held building.";
            lift.Help = "LIFT: ask for a player helicopter. Land within 150 m of the team for 10 s to board it; land within 300 m of its objective (or a base) for 10 s to set it down. 40 CR per delivery or extraction.";
            stop.Help = "STOP: end the selected team's mission or lift request where it stands.";
            recon.Help = "RECON the picked point: 60 s on site, then every enemy ground unit within 2 km is revealed for 5 minutes. 25 CR.";
            lase.Help = "LASE the selected ground contact: the team holds a laser on it. Any CALL can use AIM: TEAM. 25 CR.";
            sabot.Help = "SABOTAGE the selected enemy anchor (uplink, EW truck, data center, camp): 90 s on site, then its main unit is destroyed and the anchor goes DOWN. 60 CR.";
            seize.Help = "SEIZE the selected building: 120 s on site. Held 10 minutes; it observes 3 km around and is a lift and exfil point. 50 CR.";
            tap.Help = "NETWORK TAP the selected relay or data center: 60 s on site, then your intrusions trace 30 % slower for 10 minutes and enemy intrusions are counted. 40 CR.";
            pick.Help = "PICK: right-click the maximised map to choose a point. The nearest revealed target is selected; with none, RECON or DIVERT the point.";

            callsBox = Make(new C2Box(parent, "SOF CALLS"));
            callsBox.BodyHeight = callsBody;
            callsBox.SetMeta("LIVE · SAME AUTHORITY AS CAP");
            callsBox.Place(new AvSlot(0f, y, width, C2Box.HeaderH + callsBody));
            for (int i = 0; i < ids.Count; i++)
            {
                C2Row row = Make(new C2Row(callsBox.Body, callRowH, full));
                row.Place(new AvSlot(1f, 1f + i * (callRowH + 2f), width - 4f, callRowH));
                pinActions.Add(CapPage.Bind(row, calls, ids[i]));
                rows.Add(row);
                memos.Add(default);
            }
            Restyle();
        }

        private T Make<T>(T part) where T : AvPart
        {
            register(part);
            return part;
        }

        private AvControl Button(RectTransform parent, string label, AvButtonStyle style, float x, float y, float w, Action click)
        {
            AvControl c = AvControl.Make(parent, new AvControl.Spec(label, click, style));
            c.SingleLine();
            AvLay.Place(c.Rect, x, y, w, ButtonH);
            return c;
        }

        private static AvLineGraphic Line(RectTransform body, string name, float thickness)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(body, false);
            var g = go.AddComponent<AvLineGraphic>();
            g.raycastTarget = false;
            g.FillUnder = false;
            g.Thickness = thickness;
            AvLay.Fill(g.rectTransform);
            g.SetPoints(null, null, 0);
            return g;
        }

        private static Image Dot(RectTransform body, string name)
        {
            Image img = AvLay.Solid(body, name, Color.clear);
            img.raycastTarget = false;
            img.gameObject.SetActive(false);
            return img;
        }

        private static TMP_Text Tag(RectTransform body, string name)
        {
            TMP_Text t = C2Kit.Mono(body, name, 10f, TextAlignmentOptions.MidlineLeft, true);
            t.gameObject.SetActive(false);
            return t;
        }

        private Image[] BuildGrid(RectTransform body)
        {
            const float Cell = 40f;
            var list = new List<Image>(40);
            for (float x = Cell; x < mapW - 2f; x += Cell) list.Add(GridLine(body, x, 0f, 1f, mapH));
            for (float yy = Cell; yy < mapH - 2f; yy += Cell) list.Add(GridLine(body, 0f, yy, mapW, 1f));
            return list.ToArray();
        }

        private static Image GridLine(RectTransform body, float x, float y, float w, float h)
        {
            Image img = AvLay.Solid(body, "Grid", Color.clear);
            img.raycastTarget = false;
            AvLay.Place(img.rectTransform, x, y, w, h);
            return img;
        }

        // ---- Selection and presses ------------------------------------------------------------------------

        private void PickTeam(int slot) { selectedTeam = selectedTeam == slot ? -1 : slot; paintedSelTeam = -2; }

        private void PickTarget(int id) { selectedTarget = selectedTarget == id ? 0 : id; hasPick = hasPick && selectedTarget == 0; paintedSelTarget = -1; }

        /// <summary>A press is a request, never an effect: the host judges it. A 0.35 s debounce keeps a double click from sending twice.</summary>
        private void Press(Action send)
        {
            if (Time.unscaledTime < nextPress) return;
            nextPress = Time.unscaledTime + 0.35f;
            send?.Invoke();
        }

        private void Order(TeamVerb verb) { if (selectedTeam >= 0) act.Order?.Invoke(selectedTeam, verb); }

        private bool TryDestination(out float x, out float z)
        {
            x = z = 0f;
            if (hasPick) { x = pickX; z = pickZ; return true; }
            if (state != null && TryTarget(selectedTarget, out SofTargetRow t)) { x = t.X; z = t.Z; return true; }
            return false;
        }

        private void Divert()
        {
            if (selectedTeam < 0) return;
            if (TryDestination(out float x, out float z)) { act.Divert?.Invoke(selectedTeam, x, z); return; }
            int slot = selectedTeam; // nothing picked yet: arm the map, and the next right-click diverts the team there
            if (act.Pick != null) act.Pick("DIVERT " + SofRules.Callsign(slot), (px, pz) => act.Divert?.Invoke(slot, px, pz));
        }

        private void Mission(MissionKind kind)
        {
            if (selectedTeam < 0) return;
            if (kind == MissionKind.Recon) { if (TryDestination(out float x, out float z)) act.Mission?.Invoke(selectedTeam, kind, 0, x, z); return; }
            if (selectedTarget > 0) act.Mission?.Invoke(selectedTeam, kind, selectedTarget, 0f, 0f);
        }

        /// <summary>The existing support map gesture: right-click the maximised map. SEND TEAM HERE: the nearest revealed target within 1.5 km is selected, else the point is held for RECON or DIVERT.</summary>
        private void ArmPick()
        {
            if (act.Pick == null) return;
            bool armed = act.Pick("SEND TEAM", (px, pz) =>
            {
                pickX = px; pickZ = pz; hasPick = true; selectedTarget = 0;
                float best = 1500f;
                if (state != null)
                    foreach (SofTargetRow t in state.Targets)
                    {
                        float d = SofRules.Distance(t.X, t.Z, px, pz);
                        if (d < best) { best = d; selectedTarget = t.Id; hasPick = false; }
                    }
                standing = selectedTarget != 0 ? "TARGET SELECTED · CHOOSE A MISSION" : "POINT PICKED · RECON OR DIVERT A TEAM THERE";
                paintedSeq = -1;
            });
            if (!armed) standing = "NEGATIVE: MAP INPUT BUSY — CANCEL THE ARMED ORDER FIRST";
        }

        private bool TryTarget(int id, out SofTargetRow row)
        {
            row = default;
            if (state == null) return false;
            foreach (SofTargetRow t in state.Targets) if (t.Id == id) { row = t; return true; }
            return false;
        }

        // ---- Paint -----------------------------------------------------------------------------------------

        public void Paint(CapView v)
        {
            console.Show(v.Console);
            state = v.SofKnown && v.Sof != null && v.Sof.Active ? v.Sof : null;
            float now = SupportManager.MissionNow();
            int second = Mathf.FloorToInt(now);
            int seq = v.SofKnown && v.Sof != null ? v.Sof.Seq : -2;
            if (selectedTeam >= 0 && (state == null || !HasTeam(selectedTeam))) selectedTeam = -1;
            if (selectedTeam < 0 && state != null) selectedTeam = FirstTeam();
            if (selectedTarget != 0 && !TryTarget(selectedTarget, out _)) selectedTarget = 0;
            if (seq != paintedSeq || selectedTeam != paintedSelTeam || selectedTarget != paintedSelTarget || second != paintedSecond)
            {
                paintedSeq = seq; paintedSelTeam = selectedTeam; paintedSelTarget = selectedTarget; paintedSecond = second;
                PaintMap(now);
                PaintTeams(v, now);
            }
            Words = StandingWords(v);
            PaintCalls(v);
        }

        private bool HasTeam(int slot)
        {
            foreach (SofTeamRow t in state.Teams) if (t.Slot == slot) return true;
            return false;
        }

        private int FirstTeam() => state.Teams.Count > 0 ? state.Teams[0].Slot : -1;

        // The footer words only change with the host's refusal text, the mirror and the page's own standing hint: build them once per change, not once per frame.
        private string wordsSaid, wordsStanding, wordsCached;
        private bool wordsKnown, wordsActive;
        private int wordsSeq = int.MinValue;

        private string StandingWords(CapView v)
        {
            string said = v.Words;
            int seq = v.SofKnown && v.Sof != null ? v.Sof.Seq : -2;
            if (wordsCached != null && ReferenceEquals(said, wordsSaid) && wordsKnown == v.SofKnown && wordsActive == (state != null) && wordsSeq == seq && ReferenceEquals(standing, wordsStanding)) return wordsCached;
            wordsSaid = said; wordsKnown = v.SofKnown; wordsActive = state != null; wordsSeq = seq; wordsStanding = standing;
            return wordsCached = BuildStandingWords(v, said);
        }

        private string BuildStandingWords(CapView v, string said)
        {
            if (!string.IsNullOrEmpty(said) && C2Cap.StartsNegative(said)) return said;
            if (!v.SofKnown) return "WAITING FOR THE HOST · SOF LINK";
            if (state == null) return "NEGATIVE: SOF OFFLINE — NO CAMP STANDING, CALLS STILL LIVE";
            if (state.Camps.Count > 0 && !state.Camps.Exists(c => c.Health != AnchorHealth.Down))
                return "CAMP DOWN · RESTORING " + state.Camps[0].Rebuild + " % · NO NEW TEAMS · TEAMS IN THE FIELD CARRY ON";
            return standing;
        }

        private static string Code(TargetKind kind, AnchorSub sub) =>
            kind == TargetKind.Ground ? "GND" : kind == TargetKind.Building ? "BLD" : kind == TargetKind.Relay ? "RLY" :
            sub == AnchorSub.Uplink ? "UPL" : sub == AnchorSub.EwTruck ? "EW" : sub == AnchorSub.DataCenter ? "DC" : "CMP";

        private void PaintMap(float now)
        {
            bool active = state != null;
            headline.gameObject.SetActive(!active);
            detail.gameObject.SetActive(!active);
            if (!active)
            {
                OpsText.Set(headline, paintedSeq == -2 ? "NO LINK" : "NO CAMP STANDING");
                OpsText.Set(detail, paintedSeq == -2 ? "> waiting for the host SOF state" : "> no camp standing — restore one or use the CALLS below");
            }
            map.SetMeta(SofPageWords.Sub(state));
            for (int i = 0; i < targetMarks.Length; i++) { targetMarks[i].Rect.gameObject.SetActive(false); targetSelect[i].gameObject.SetActive(false); }
            for (int i = 0; i < teamMarks.Length; i++) teamMarks[i].Rect.gameObject.SetActive(false);
            for (int i = 0; i < campDots.Length; i++) { campDots[i].gameObject.SetActive(false); campTags[i].gameObject.SetActive(false); }
            for (int i = 0; i < heldDots.Length; i++) { heldDots[i].gameObject.SetActive(false); heldTags[i].gameObject.SetActive(false); }
            for (int i = 0; i < enemyDots.Length; i++) enemyDots[i].gameObject.SetActive(false);
            for (int i = 0; i < routes.Length; i++) routes[i].SetPoints(null, null, 0);
            pickDot.gameObject.SetActive(false);
            if (!active) return;

            placed.Clear();
            fit.Clear();
            foreach (SofCampRow c in state.Camps) fit.Add(new MapPoint(c.X, c.Z));
            foreach (SofHeldRow h in state.Held) fit.Add(new MapPoint(h.X, h.Z));
            foreach (SofTeamRow t in state.Teams) { fit.Add(new MapPoint(t.X, t.Z)); if (t.HasDest) fit.Add(new MapPoint(t.DestX, t.DestZ)); }
            foreach (SofTargetRow t in state.Targets) fit.Add(new MapPoint(t.X, t.Z));
            foreach (SofEnemyRow e in state.Enemies) fit.Add(new MapPoint(e.X, e.Z));
            if (hasPick) fit.Add(new MapPoint(pickX, pickZ));
            projection.Fit(fit, mapW, mapH);

            for (int i = 0; i < state.Camps.Count && i < campDots.Length; i++)
            {
                SofCampRow c = state.Camps[i];
                MapPoint p = projection.ToScreen(c.X, c.Z);
                AvState tone = c.Health == AnchorHealth.Live ? AvState.Ready : c.Health == AnchorHealth.Damaged ? AvState.Caution : AvState.Danger;
                campDots[i].gameObject.SetActive(true); campDots[i].color = OpsInk.Rail(tone);
                AvLay.Place(campDots[i].rectTransform, p.X - 5f, p.Y - 5f, 10f, 10f);
                campTags[i].gameObject.SetActive(true);
                OpsText.Set(campTags[i], "CP" + (i + 1));
                campTags[i].color = OpsInk.Word(tone);
                Vector2 tag = Free(p.X + 8f, p.Y - 7f, 26f, 14f);
                C2Kit.Place(campTags[i], tag.x, tag.y, 26f, 14f);
            }
            for (int i = 0; i < state.Held.Count && i < heldDots.Length; i++)
            {
                SofHeldRow h = state.Held[i];
                MapPoint p = projection.ToScreen(h.X, h.Z);
                heldDots[i].gameObject.SetActive(true); heldDots[i].color = OpsInk.Rail(AvState.Info);
                AvLay.Place(heldDots[i].rectTransform, p.X - 4f, p.Y - 4f, 8f, 8f);
                heldTags[i].gameObject.SetActive(true);
                OpsText.Set(heldTags[i], "H" + (i + 1) + " " + SofWords.Clock(h.Until - now));
                heldTags[i].color = OpsInk.Word(AvState.Info);
                Vector2 tag = Free(p.X + 7f, p.Y - 7f, 54f, 14f);
                C2Kit.Place(heldTags[i], tag.x, tag.y, 54f, 14f);
            }
            for (int i = 0; i < state.Enemies.Count && i < enemyDots.Length; i++)
            {
                MapPoint p = projection.ToScreen(state.Enemies[i].X, state.Enemies[i].Z);
                enemyDots[i].gameObject.SetActive(true); enemyDots[i].color = OpsInk.Rail(AvState.Danger);
                AvLay.Place(enemyDots[i].rectTransform, p.X - 3f, p.Y - 3f, 6f, 6f);
            }
            for (int i = 0; i < state.Teams.Count && i < teamMarks.Length; i++)
            {
                SofTeamRow t = state.Teams[i];
                if (t.State == TeamState.Raising || t.State == TeamState.Lost) continue;
                MapPoint p = projection.ToScreen(t.X, t.Z);
                int slot = t.Slot;
                AvControl m = teamMarks[i];
                m.Rect.gameObject.SetActive(true);
                m.Label = SofRules.Callsign(slot).Replace("-", "");
                m.SetStyle(t.State == TeamState.Pinned ? AvButtonStyle.Primary : AvButtonStyle.Default);
                m.Latched = slot == selectedTeam;
                m.Help = SofPageWords.TeamLine(t, now);
                Vector2 at = Free(p.X - 15f, p.Y - 9f, 30f, 18f);
                AvLay.Place(m.Rect, at.x, at.y, 30f, 18f);
                if (t.HasDest && i < routes.Length)
                {
                    MapPoint d = projection.ToScreen(t.DestX, t.DestZ);
                    var xs = new[] { Mathf.Clamp01(p.X / mapW), Mathf.Clamp01(d.X / mapW) };
                    var ys = new[] { Mathf.Clamp01(1f - p.Y / mapH), Mathf.Clamp01(1f - d.Y / mapH) };
                    routes[i].LineColor = slot == selectedTeam ? OpsInk.Word(AvState.Caution) : OpsInk.Hairline;
                    routes[i].SetPoints(xs, ys, 2);
                }
            }
            for (int i = 0; i < state.Targets.Count && i < targetMarks.Length; i++)
            {
                SofTargetRow t = state.Targets[i];
                MapPoint p = projection.ToScreen(t.X, t.Z);
                Vector2 at = Free(p.X - 17f, p.Y - 9f, 34f, 18f);
                float bx = at.x, by = at.y;
                targetIds[i] = t.Id;
                AvControl m = targetMarks[i];
                m.Rect.gameObject.SetActive(true);
                m.Label = Code(t.Kind, t.Sub) + (t.Exploit ? "+" : t.Resisted ? "-" : "");
                m.SetStyle(AvButtonStyle.Quiet);
                m.Latched = t.Id == selectedTarget;
                m.Help = SofWords.Target(t.Kind, t.Sub, t.Id) + (t.Exploit ? " · EXPLOIT: SOF beats CYBER (-25 % cost, x1.5 duration, +25 % odds)" : t.Resisted ? " · RESISTED: SPACE beats SOF" : "");
                AvLay.Place(m.Rect, bx, by, 34f, 18f);
                targetSelect[i].gameObject.SetActive(t.Id == selectedTarget);
                targetSelect[i].color = OpsInk.Select;
                AvLay.Place(targetSelect[i].rectTransform, bx - 2f, by - 2f, 38f, 22f);
            }
            if (hasPick)
            {
                MapPoint p = projection.ToScreen(pickX, pickZ);
                pickDot.gameObject.SetActive(true); pickDot.color = OpsInk.Select;
                AvLay.Place(pickDot.rectTransform, p.X - 5f, p.Y - 5f, 10f, 10f);
            }
        }

        private static readonly Vector2[] Nudges =
        {
            new Vector2(0f, 0f), new Vector2(0f, -20f), new Vector2(0f, 20f), new Vector2(38f, 0f), new Vector2(-38f, 0f), new Vector2(38f, -20f), new Vector2(-38f, -20f),
            new Vector2(38f, 20f), new Vector2(-38f, 20f), new Vector2(0f, -40f), new Vector2(0f, 40f), new Vector2(76f, 0f), new Vector2(-76f, 0f), new Vector2(76f, -20f),
            new Vector2(-76f, -20f), new Vector2(76f, 20f), new Vector2(-76f, 20f), new Vector2(38f, -40f), new Vector2(-38f, -40f), new Vector2(38f, 40f), new Vector2(-38f, 40f),
            new Vector2(0f, -60f), new Vector2(0f, 60f), new Vector2(114f, 0f), new Vector2(-114f, 0f), new Vector2(114f, -20f), new Vector2(-114f, -20f), new Vector2(114f, 20f),
            new Vector2(-114f, 20f), new Vector2(76f, -40f), new Vector2(-76f, -40f), new Vector2(76f, 40f), new Vector2(-76f, 40f), new Vector2(0f, -80f), new Vector2(0f, 80f)
        };

        /// <summary>Greedy label declutter: the nearest free spot (inside the map) to the wanted one, tried in a fixed order, so two map labels never share pixels.</summary>
        private Vector2 Free(float x, float y, float w, float h)
        {
            Vector2 first = new Vector2(Mathf.Clamp(x, 0f, mapW - w), Mathf.Clamp(y, 0f, mapH - h));
            for (int i = 0; i < Nudges.Length; i++)
            {
                float nx = Mathf.Clamp(x + Nudges[i].x, 0f, mapW - w), ny = Mathf.Clamp(y + Nudges[i].y, 0f, mapH - h);
                var r = new Rect(nx - 1f, ny - 1f, w + 2f, h + 2f);
                bool hit = false;
                for (int k = 0; k < placed.Count && !hit; k++) hit = placed[k].Overlaps(r);
                if (hit) continue;
                placed.Add(r);
                return new Vector2(nx, ny);
            }
            placed.Add(new Rect(first.x, first.y, w, h));
            return first;
        }

        private bool TeamRow(int slot, out SofTeamRow row)
        {
            row = default;
            if (state == null) return false;
            foreach (SofTeamRow t in state.Teams) if (t.Slot == slot) { row = t; return true; }
            return false;
        }

        private void PaintTeams(CapView v, float now)
        {
            bool active = state != null;
            teamsBox.SetMeta(active ? state.Teams.Count + "/" + state.TeamCap + " TEAMS" : "NONE");
            for (int i = 0; i < Slots; i++)
            {
                bool has = active && i < state.Teams.Count;
                SofTeamRow t = has ? state.Teams[i] : default;
                rowButtons[i].Rect.gameObject.SetActive(has);
                rowBack[i].gameObject.SetActive(has); rowFill[i].gameObject.SetActive(has);
                if (!has)
                {
                    OpsText.Set(rowText[i], i == 0 ? (active ? "NO TEAM · RAISE ONE AT THE CAMP" : "SOF OFFLINE") : "");
                    rowText[i].color = OpsInk.Muted;
                    continue;
                }
                bool sel = t.Slot == selectedTeam;
                rowButtons[i].Latched = sel;
                int slot = t.Slot;
                rowButtons[i].Help = "Select team " + SofRules.Callsign(slot) + ". Odds " + t.Odds + " % · ammo " + t.Ammo + (t.Wounded ? " · WIA" : "");
                OpsText.Set(rowText[i], C2Kit.FitTo(rowText[i], SofPageWords.TeamLine(t, now), width - 2f - 2f * Pad - 68f));
                AvState tone = t.State == TeamState.Pinned || t.State == TeamState.Lost ? AvState.Danger : t.Exposure >= 70 ? AvState.Caution : AvState.Ready;
                rowText[i].color = t.State == TeamState.Lost ? OpsInk.Dim : sel ? OpsInk.Ink : OpsInk.Muted;
                rowBack[i].color = OpsInk.Inert;
                rowFill[i].color = OpsInk.Rail(tone);
                Vector2 size = rowFill[i].rectTransform.sizeDelta;
                rowFill[i].rectTransform.sizeDelta = new Vector2(60f * Mathf.Clamp01(t.Exposure / 100f), size.y);
            }
            SofTeamRow cur = default;
            bool have = TeamRow(selectedTeam, out cur);
            bool field = have && (cur.State == TeamState.Moving || cur.State == TeamState.OnSite || cur.State == TeamState.Returning || (cur.State == TeamState.Ready && !cur.Carried));
            bool moving = have && (cur.State == TeamState.Moving || cur.State == TeamState.Returning);
            bool ready = have && cur.State == TeamState.Ready;
            bool canDest = hasPick || selectedTarget != 0;
            SofTargetRow tgt = default;
            bool hasTarget = selectedTarget != 0 && TryTarget(selectedTarget, out tgt);
            raise.Interactable = active && state.Teams.Count < state.TeamCap && state.Camps.Exists(c => c.Health != AnchorHealth.Down);
            push.Interactable = moving; push.Latched = have && cur.Push;
            hold.Interactable = moving || (have && cur.State == TeamState.OnSite); hold.Latched = have && cur.Hold;
            divert.Interactable = have && cur.State != TeamState.Raising && cur.State != TeamState.Recovering && cur.State != TeamState.Pinned && cur.State != TeamState.Lost;
            exfil.Interactable = field;
            lift.Interactable = ready && !cur.Carried; lift.Latched = have && cur.LiftWaiting;
            stop.Interactable = have && (cur.State == TeamState.Moving || cur.State == TeamState.OnSite || cur.State == TeamState.Returning || cur.LiftWaiting);
            recon.Interactable = ready && hasPick;
            lase.Interactable = ready && hasTarget && SofRules.Valid(MissionKind.Lase, tgt.Kind, tgt.Sub);
            sabot.Interactable = ready && hasTarget && SofRules.Valid(MissionKind.Sabotage, tgt.Kind, tgt.Sub);
            seize.Interactable = ready && hasTarget && SofRules.Valid(MissionKind.Seize, tgt.Kind, tgt.Sub);
            tap.Interactable = ready && hasTarget && SofRules.Valid(MissionKind.Tap, tgt.Kind, tgt.Sub);
            pick.Interactable = active;
            if (!active) { OpsText.Set(infoLine, "SOF OFFLINE"); infoLine.color = OpsInk.Dim; OpsText.Set(tapLine, ""); return; }
            if (hasTarget)
            {
                MissionKind kind = tgt.Kind == TargetKind.Ground ? MissionKind.Lase : tgt.Kind == TargetKind.Building ? MissionKind.Seize : tgt.Kind == TargetKind.Relay ? MissionKind.Tap : MissionKind.Sabotage;
                float ox = have ? cur.X : tgt.X, oz = have ? cur.Z : tgt.Z; // the host measures the ring boost from the team, so the preview does too
                bool ring = v.Cyber != null && v.CyberKnown && v.Cyber.Nodes.Exists(n => n.Held && SofRules.Distance(n.X, n.Z, ox, oz) <= SofRules.RingBoostMetres);
                int odds = SofRules.Odds(have ? cur.Exposure : 0f, 0, have && (cur.Carried || cur.Insert == Insertion.Helicopter), tgt.Exploit, ring);
                string text = SofWords.Target(tgt.Kind, tgt.Sub, tgt.Id) + " · " + SofWords.Kind(kind) + " " + SofRules.CostOf(kind, tgt.Exploit) + " CR" +
                    (kind == MissionKind.Lase ? " · NO ROLL" : " · ~" + odds + " %") + (tgt.Exploit ? " · EXPLOIT" : tgt.Resisted ? " · RESISTED" : "");
                OpsText.Set(infoLine, C2Kit.FitTo(infoLine, text, width - 2f - 2f * Pad));
                infoLine.color = OpsInk.Ink;
            }
            else if (hasPick)
            {
                OpsText.Set(infoLine, "POINT " + TheaterGrid.Kilometres(pickX, pickZ) + " · RECON 25 CR OR DIVERT");
                infoLine.color = OpsInk.Ink;
            }
            else { OpsText.Set(infoLine, "SELECT A TARGET ON THE MAP, OR PICK A POINT"); infoLine.color = OpsInk.Dim; }
            bool tapped = state.TapUntil > now;
            OpsText.Set(tapLine, tapped && full ? "TAP · " + state.TapIntrusions + " ENEMY INTRUSIONS · " + SofWords.Clock(state.TapUntil - now) : "");
            tapLine.color = OpsInk.Word(AvState.Info);
        }

        private void PaintCalls(CapView v)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                CallTile tile = default;
                bool found = false;
                for (int k = 0; k < v.Tiles.Count && !found; k++)
                    if (v.Tiles[k].Id == ids[i]) { tile = v.Tiles[k]; found = true; }
                if (!found) continue;
                bool pinned = CapPage.IsPinned(v, ids[i]);
                CapPage.RowMemo memo = memos[i];
                bool changed = memo.Changed(tile, pinned);
                memos[i] = memo;
                if (changed) CapPage.PaintRow(rows[i], tile, CapPage.IndexOf(ids[i]), pinned, pinActions[i]);
            }
        }

        public void Restyle()
        {
            foreach (Image g in gridLines) if (g != null) g.color = OpsInk.Hairline;
            if (headline != null) headline.color = OpsInk.Muted;
            if (detail != null) detail.color = OpsInk.Dim;
            for (int i = 0; i < Slots; i++) { rowBack[i].color = OpsInk.Inert; rowButtons[i]?.Restyle(); teamMarks[i]?.Restyle(); }
            for (int i = 0; i < targetMarks.Length; i++) targetMarks[i]?.Restyle();
            foreach (AvControl c in new[] { raise, push, hold, divert, exfil, lift, stop, recon, lase, sabot, seize, tap, pick }) c?.Restyle();
            paintedSeq = -1; // repaint with the new palette
        }
    }
}
