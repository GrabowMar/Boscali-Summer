using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// The [3] NET tab (spec §6): the INTRUSION MAP (EW trucks with their reach circles, data centers, the visible enemy nodes with kind codes, held and hopping
    /// state, edges), the ACTIVE INTRUSION box (target, hop progress, TRACE bar, HOP / BURN / DROP), the ANCHORS box and the CYBER CALL rows (FLARE BARRAGE and EMP,
    /// bound exactly as the CAP page binds them). Painted only from the faction mirror (<see cref="CapView.Cyber"/>): the page never reads a game object and every
    /// press goes through the three actions it is given. HOLD is a state, not a verb: a node stays held until it is burned or dropped.
    /// </summary>
    internal sealed class CyberNetPage
    {
        private const float Gap = 6f, Pad = 8f, ButtonH = 26f;
        private const int Marks = CyberWire.MaxNodes, Lines = 24, Notes = 4;
        private readonly float width;
        private readonly bool full;
        private readonly Action<AvPart> register;
        private readonly Action<int> hop, burn, drop;
        private readonly C2ConsoleView console;
        private readonly C2Box map, intrusion, anchors, callsBox;
        private readonly float mapW, mapH;
        private readonly Image[] gridLines;
        private readonly AvLineGraphic[] edges = new AvLineGraphic[Lines];
        private readonly AvLineGraphic[] reach = new AvLineGraphic[AnchorRules.MaxTrucks];
        private readonly AvControl[] marks = new AvControl[Marks];
        private readonly int[] markIds = new int[Marks];
        private readonly Image[] markSelect = new Image[Marks];
        private readonly Image[] anchorDots = new Image[CyberWire.MaxAnchors];
        private readonly TMP_Text[] anchorTags = new TMP_Text[CyberWire.MaxAnchors];
        private readonly TMP_Text headline, detail, targetLine, traceText, heldLine, hintLine;
        private readonly Image hopBack, hopFill, traceBack, traceFill;
        private readonly AvControl hopButton, burnButton, dropButton;
        private readonly TMP_Text[] anchorRows = new TMP_Text[Notes];
        private readonly List<C2Row> rows = new List<C2Row>(2);
        private readonly List<SupportActionId> ids = new List<SupportActionId>(2);
        private readonly List<Action> pinActions = new List<Action>(2);
        private readonly List<CapPage.RowMemo> memos = new List<CapPage.RowMemo>(2);
        private readonly CyberMapProjection projection = new CyberMapProjection();
        private readonly List<MapPoint> fit = new List<MapPoint>(32);
        private readonly List<EwSource> sources = new List<EwSource>(2);
        private readonly List<CyberNode> graphNodes = new List<CyberNode>(CyberWire.MaxNodes);
        private readonly List<CyberEdge> graphEdges = new List<CyberEdge>(CyberGraph.MaxEdges);
        private readonly HashSet<int> heldIds = new HashSet<int>();
        private int selected, ownHeldLast;
        private int paintedSeq = -1, paintedSelected = -1, paintedSecond = -1;
        private float nextPress;
        private bool hasOwn;

        /// <summary>The footer words of this page: the standing hint, replaced by the CYBER verdict or a refusal.</summary>
        public string Words { get; private set; } = "SELECT A NODE ON THE MAP · HOP STARTS AN INTRUSION";

        public CyberNetPage(RectTransform parent, float width, float height, CallsController calls, Action<AvPart> register,
            Action<int> hop, Action<int> burn, Action<int> drop)
        {
            this.width = width;
            this.register = register ?? (_ => { });
            this.hop = hop; this.burn = burn; this.drop = drop;
            full = height >= 560f;
            for (int i = 0; i < CallSheet.Rows.Count; i++) if (CallSheet.Rows[i].Family == CallFamily.Cyber) ids.Add(CallSheet.Rows[i].Id);

            float gap = full ? Gap : 4f;
            int consoleLines = full ? 4 : 2;
            float consoleH = C2ConsoleView.HeightFor(consoleLines);
            float rowH = full ? 40f : 26f;
            float callsBody = ids.Count * (rowH + 2f) + 2f;
            float intrusionBody = full ? 124f : 96f;
            float anchorsBody = full ? Notes * 16f + 8f : 0f;
            float fixedH = gap + consoleH + gap + (C2Box.HeaderH + intrusionBody) + gap + (full ? C2Box.HeaderH + anchorsBody + gap : 0f) +
                (C2Box.HeaderH + callsBody) + gap + C2Box.HeaderH + 2f;
            float mapBody = Mathf.Max(110f, height - fixedH);
            mapW = width - 2f; mapH = mapBody - 2f;

            float y = gap;
            console = Make(new C2ConsoleView(parent, consoleLines));
            console.Place(new AvSlot(0f, y, width, consoleH));
            y += consoleH + gap;

            map = Make(new C2Box(parent, "INTRUSION MAP"));
            map.BodyHeight = mapBody;
            map.SetMeta("NO EW ASSETS ONLINE");
            map.Place(new AvSlot(0f, y, width, C2Box.HeaderH + mapBody));
            y += C2Box.HeaderH + mapBody + gap;
            gridLines = BuildGrid(map.Body);
            for (int i = 0; i < reach.Length; i++) reach[i] = Line(map.Body, "Reach" + i, 1.2f);
            for (int i = 0; i < edges.Length; i++) edges[i] = Line(map.Body, "Edge" + i, 1.4f);
            for (int i = 0; i < anchorDots.Length; i++)
            {
                anchorDots[i] = AvLay.Solid(map.Body, "Anchor" + i, Color.clear);
                anchorDots[i].raycastTarget = false;
                anchorTags[i] = C2Kit.Mono(map.Body, "AnchorTag" + i, 10f, TextAlignmentOptions.MidlineLeft, true);
                anchorDots[i].gameObject.SetActive(false);
                anchorTags[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < marks.Length; i++)
            {
                int slot = i;
                markSelect[i] = AvLay.Solid(map.Body, "Select" + i, Color.clear);
                markSelect[i].raycastTarget = false;
                markSelect[i].gameObject.SetActive(false);
                marks[i] = AvControl.Make(map.Body, new AvControl.Spec("", () => Pick(markIds[slot]), AvButtonStyle.Quiet));
                marks[i].SingleLine();
                marks[i].Rect.gameObject.SetActive(false);
            }
            headline = C2Kit.Mono(map.Body, "Headline", 12f, TextAlignmentOptions.Center, true, 2f);
            detail = C2Kit.Mono(map.Body, "Detail", 10f, TextAlignmentOptions.Center);
            C2Kit.Place(headline, 4f, mapH * 0.5f - 22f, mapW - 8f, 18f);
            C2Kit.Place(detail, 4f, mapH * 0.5f - 2f, mapW - 8f, 16f);

            intrusion = Make(new C2Box(parent, "ACTIVE INTRUSION"));
            intrusion.BodyHeight = intrusionBody;
            intrusion.SetMeta("NONE");
            intrusion.Place(new AvSlot(0f, y, width, C2Box.HeaderH + intrusionBody));
            y += C2Box.HeaderH + intrusionBody + gap;
            float iw = width - 2f;
            targetLine = C2Kit.Mono(intrusion.Body, "Target", 11f, TextAlignmentOptions.MidlineLeft, true, 1f);
            C2Kit.Place(targetLine, Pad, 3f, iw - 2f * Pad, 16f);
            hopBack = AvLay.Solid(intrusion.Body, "HopBack", Color.clear);
            hopFill = AvLay.Solid(intrusion.Body, "HopFill", Color.clear);
            AvLay.Place(hopBack.rectTransform, Pad, 21f, iw - 2f * Pad, 6f);
            AvLay.Place(hopFill.rectTransform, Pad, 21f, 0f, 6f);
            traceText = C2Kit.Mono(intrusion.Body, "TraceText", 10f, TextAlignmentOptions.MidlineLeft, true, 1f);
            C2Kit.Place(traceText, Pad, 30f, iw - 2f * Pad, 14f);
            traceBack = AvLay.Solid(intrusion.Body, "TraceBack", Color.clear);
            traceFill = AvLay.Solid(intrusion.Body, "TraceFill", Color.clear);
            AvLay.Place(traceBack.rectTransform, Pad, 45f, iw - 2f * Pad, 8f);
            AvLay.Place(traceFill.rectTransform, Pad, 45f, 0f, 8f);
            heldLine = C2Kit.Mono(intrusion.Body, "Held", 10f, TextAlignmentOptions.MidlineLeft);
            C2Kit.Place(heldLine, Pad, 57f, iw - 2f * Pad, 14f);
            hintLine = C2Kit.Mono(intrusion.Body, "Hint", 10f, TextAlignmentOptions.MidlineLeft);
            C2Kit.Place(hintLine, Pad, 71f, iw - 2f * Pad, 14f);
            // The compact 596 page has no room for the held list and the hint: the held list rides the trace line and the hint is the footer's.
            heldLine.gameObject.SetActive(full);
            hintLine.gameObject.SetActive(full);
            float by = intrusionBody - ButtonH - 4f, bw = Mathf.Floor((iw - 2f * Pad - 2f * 6f) / 3f);
            hopButton = Button(intrusion.Body, "HOP", AvButtonStyle.Primary, Pad, by, bw, () => Press(() => hop?.Invoke(Target(false))));
            burnButton = Button(intrusion.Body, "BURN", AvButtonStyle.Default, Pad + bw + 6f, by, bw, () => Press(() => burn?.Invoke(Target(true))));
            dropButton = Button(intrusion.Body, "DROP", AvButtonStyle.Quiet, Pad + 2f * (bw + 6f), by, bw, () => Press(() => drop?.Invoke(DropTarget())));
            hopButton.Help = "Start an intrusion on the selected node (30 CR), or go deeper from a node you hold (free). Hopping raises your TRACE bar.";
            burnButton.Help = "Release the held node and post its BURN package on the TASKED board for any pilot to fire.";
            dropButton.Help = "Release the selected node, or the whole intrusion when none is selected. No package.";

            if (full)
            {
                anchors = Make(new C2Box(parent, "ASSETS"));
                anchors.BodyHeight = anchorsBody;
                anchors.SetMeta("EW TRUCKS · DATA CENTERS");
                anchors.Place(new AvSlot(0f, y, width, C2Box.HeaderH + anchorsBody));
                y += C2Box.HeaderH + anchorsBody + gap;
                for (int i = 0; i < anchorRows.Length; i++)
                {
                    anchorRows[i] = C2Kit.Mono(anchors.Body, "AnchorRow" + i, 10.5f, TextAlignmentOptions.MidlineLeft);
                    C2Kit.Place(anchorRows[i], Pad, 4f + i * 16f, iw - 2f * Pad, 16f);
                }
            }

            callsBox = Make(new C2Box(parent, "CYBER CALLS"));
            callsBox.BodyHeight = callsBody;
            callsBox.SetMeta("LIVE · SAME AUTHORITY AS CAP");
            callsBox.Place(new AvSlot(0f, y, width, C2Box.HeaderH + callsBody));
            for (int i = 0; i < ids.Count; i++)
            {
                C2Row row = Make(new C2Row(callsBox.Body, rowH, full));
                row.Place(new AvSlot(1f, 1f + i * (rowH + 2f), width - 4f, rowH));
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

        // ---- Selection and presses -------------------------------------------------------------------------------

        private void Pick(int nodeId) { selected = selected == nodeId ? 0 : nodeId; paintedSelected = -1; }

        /// <summary>A press is a request, never an effect: the host judges it. A 0.35 s debounce keeps a double click from sending twice.</summary>
        private void Press(Action send)
        {
            if (Time.unscaledTime < nextPress) return;
            nextPress = Time.unscaledTime + 0.35f;
            send?.Invoke();
        }

        private int Target(bool needHeld) => needHeld ? (selected > 0 && IsHeldHere(selected) ? selected : ownHeldLast) : selected;

        private int DropTarget() => selected > 0 && IsHeldHere(selected) ? selected : 0;

        private bool IsHeldHere(int id) => heldIds.Contains(id);

        // ---- Paint -------------------------------------------------------------------------------------------------

        public void Paint(CapView v)
        {
            console.Show(v.Console);
            CyberStateData s = v.CyberKnown ? v.Cyber : null;
            float now = SupportManager.MissionNow();
            int second = Mathf.FloorToInt(now);
            int seq = s != null ? s.Seq : -2;
            if (seq != paintedSeq || selected != paintedSelected || second != paintedSecond)
            {
                paintedSeq = seq; paintedSelected = selected; paintedSecond = second;
                PaintMap(s, now);
                PaintIntrusion(s, now);
                PaintAnchors(s, now);
            }
            Words = StandingWords(s, v.Words);
            PaintCalls(v);
        }

        private string StandingWords(CyberStateData s, string said)
        {
            if (!string.IsNullOrEmpty(said) && C2Cap.StartsNegative(said)) return said;
            if (s == null) return "WAITING FOR THE HOST · CYBER LINK";
            if (!s.Active) return "NEGATIVE: CYBER OFFLINE — NO EW TRUCK STANDING, CALLS STILL LIVE";
            return hasOwn ? "INTRUSION RUNNING · WATCH THE TRACE BAR, BURN BEFORE IT FILLS" : "SELECT A NODE ON THE MAP · HOP STARTS AN INTRUSION";
        }

        private void PaintMap(CyberStateData s, float now)
        {
            bool active = s != null && s.Active;
            headline.gameObject.SetActive(!active || (s.Nodes.Count == 0 && s.Anchors.Count == 0));
            detail.gameObject.SetActive(headline.gameObject.activeSelf);
            if (headline.gameObject.activeSelf)
            {
                OpsText.Set(headline, s == null ? "NO LINK" : "NO EW ASSETS ONLINE");
                OpsText.Set(detail, s == null ? "> waiting for the host CYBER state" : "> no EW truck standing — restore one or use the CALLS below");
            }
            map.SetMeta(CyberNetWords.Sub(s));
            for (int i = 0; i < marks.Length; i++) { marks[i].Rect.gameObject.SetActive(false); markSelect[i].gameObject.SetActive(false); }
            for (int i = 0; i < anchorDots.Length; i++) { anchorDots[i].gameObject.SetActive(false); anchorTags[i].gameObject.SetActive(false); }
            for (int i = 0; i < edges.Length; i++) edges[i].SetPoints(null, null, 0);
            for (int i = 0; i < reach.Length; i++) reach[i].SetPoints(null, null, 0);
            heldIds.Clear();
            ownHeldLast = 0; hasOwn = false;
            if (!active) return;

            fit.Clear();
            sources.Clear();
            foreach (CyberAnchorRow a in s.Anchors)
            {
                fit.Add(new MapPoint(a.X, a.Z));
                if (a.Kind != AnchorKind.EwTruck || a.Health == AnchorHealth.Down) continue;
                float r = AnchorRules.Reach(a.Health); // a truck's reach circle is part of what the map must show
                fit.Add(new MapPoint(a.X - r, a.Z - r)); fit.Add(new MapPoint(a.X + r, a.Z + r));
            }
            foreach (CyberNodeRow n in s.Nodes) { fit.Add(new MapPoint(n.X, n.Z)); if (n.Held) heldIds.Add(n.Id); }
            projection.Fit(fit, mapW, mapH);

            int truckOrdinal = 0, centerOrdinal = 0, slot = 0;
            foreach (CyberAnchorRow a in s.Anchors)
            {
                if (slot >= anchorDots.Length) break;
                MapPoint p = projection.ToScreen(a.X, a.Z);
                bool truck = a.Kind == AnchorKind.EwTruck;
                string tag = truck ? "EW" + (++truckOrdinal) : "DC" + (++centerOrdinal);
                AvState tone = a.Health == AnchorHealth.Live ? AvState.Ready : a.Health == AnchorHealth.Damaged ? AvState.Caution : AvState.Danger;
                anchorDots[slot].gameObject.SetActive(true);
                anchorDots[slot].color = OpsInk.Rail(tone);
                AvLay.Place(anchorDots[slot].rectTransform, p.X - 4f, p.Y - 4f, 8f, 8f);
                anchorTags[slot].gameObject.SetActive(true);
                OpsText.Set(anchorTags[slot], tag);
                anchorTags[slot].color = OpsInk.Word(tone);
                C2Kit.Place(anchorTags[slot], Mathf.Min(mapW - 26f, p.X + 7f), p.Y - 7f, 26f, 14f);
                if (truck && a.Health != AnchorHealth.Down && truckOrdinal <= reach.Length)
                {
                    sources.Add(new EwSource(truckOrdinal - 1, a.X, a.Z, AnchorRules.Reach(a.Health)));
                    DrawCircle(reach[truckOrdinal - 1], p, projection.Pixels(AnchorRules.Reach(a.Health)));
                }
                slot++;
            }

            graphNodes.Clear();
            for (int i = 0; i < s.Nodes.Count && i < Marks; i++)
            {
                CyberNodeRow n = s.Nodes[i];
                graphNodes.Add(new CyberNode(n.Id, n.Kind, n.X, n.Z, 0u, 0f, 0));
                MapPoint p = projection.ToScreen(n.X, n.Z);
                float bx = Mathf.Clamp(p.X - 17f, 0f, mapW - 34f), by = Mathf.Clamp(p.Y - 9f, 0f, mapH - 18f);
                markIds[i] = n.Id;
                AvControl m = marks[i];
                m.Rect.gameObject.SetActive(true);
                m.Label = CyberNetWords.Code(n.Kind) + (n.Exploit ? "+" : "");
                m.SetStyle(n.Hopping ? AvButtonStyle.Primary : n.Held ? AvButtonStyle.Default : AvButtonStyle.Quiet);
                m.Latched = n.Held;
                m.Help = CyberNetWords.Node(n.Kind, n.Id) + (n.Held ? " · HELD" : n.Hopping ? " · HOP IN PROGRESS" : "") + (n.Exploit ? " · EXPLOIT: CYBER beats SPACE (-25 % hop time and cost, x1.5 package duration)" : "");
                AvLay.Place(m.Rect, bx, by, 34f, 18f);
                markSelect[i].gameObject.SetActive(selected == n.Id);
                markSelect[i].color = OpsInk.Select;
                AvLay.Place(markSelect[i].rectTransform, bx - 2f, by - 2f, 38f, 22f);
            }
            CyberGraph.Edges(sources, graphNodes, heldIds, graphEdges);
            for (int i = 0; i < graphEdges.Count && i < edges.Length; i++)
            {
                CyberEdge e = graphEdges[i];
                if (!TryPoint(s, e.From, out MapPoint a) || !TryPoint(s, e.To, out MapPoint b)) continue;
                bool chain = e.From > 0;
                var xs = new[] { Mathf.Clamp01(a.X / mapW), Mathf.Clamp01(b.X / mapW) };
                var ys = new[] { Mathf.Clamp01(1f - a.Y / mapH), Mathf.Clamp01(1f - b.Y / mapH) };
                edges[i].LineColor = chain ? OpsInk.Word(AvState.Caution) : OpsInk.Hairline;
                edges[i].SetPoints(xs, ys, 2);
            }
            foreach (CyberIntrusionRow x in s.Intrusions)
            {
                if (!x.Own) continue;
                hasOwn = true;
                if (x.Held != null && x.Held.Length > 0) ownHeldLast = x.Held[x.Held.Length - 1];
            }
        }

        private bool TryPoint(CyberStateData s, int endpoint, out MapPoint p)
        {
            p = default;
            if (endpoint < 0)
            {
                int index = -endpoint - 1, seen = 0;
                foreach (CyberAnchorRow a in s.Anchors)
                {
                    if (a.Kind != AnchorKind.EwTruck) continue;
                    if (seen++ == index) { p = projection.ToScreen(a.X, a.Z); return true; }
                }
                return false;
            }
            foreach (CyberNodeRow n in s.Nodes) if (n.Id == endpoint) { p = projection.ToScreen(n.X, n.Z); return true; }
            return false;
        }

        private void DrawCircle(AvLineGraphic g, MapPoint center, float radius)
        {
            const int N = 64;
            var xs = new float[N + 1];
            var ys = new float[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float a = Mathf.PI * 2f * i / N;
                xs[i] = Mathf.Clamp01((center.X + radius * Mathf.Cos(a)) / mapW);
                ys[i] = Mathf.Clamp01(1f - (center.Y + radius * Mathf.Sin(a)) / mapH);
            }
            g.LineColor = OpsInk.Hairline;
            g.SetPoints(xs, ys, N + 1);
        }

        private void PaintIntrusion(CyberStateData s, float now)
        {
            CyberIntrusionRow mine = default;
            bool have = false;
            if (s != null && s.Active) foreach (CyberIntrusionRow x in s.Intrusions) if (x.Own) { mine = x; have = true; break; }
            int others = s != null ? s.Intrusions.Count - (have ? 1 : 0) : 0;
            intrusion.SetMeta(have ? "OPERATOR YOU" : others > 0 ? others + " FRIENDLY INTRUSION" + (others == 1 ? "" : "S") : "NONE");
            bool active = s != null && s.Active;
            hopButton.Interactable = active && selected > 0 && !heldIds.Contains(selected) && (!have || mine.Phase != IntrusionPhase.Hopping);
            burnButton.Interactable = have && mine.Held != null && mine.Held.Length > 0;
            dropButton.Interactable = have;
            hopButton.Label = have ? "HOP DEEPER" : "HOP";
            if (!have)
            {
                OpsText.Set(targetLine, active ? "NO INTRUSION · SELECT A NODE, THEN HOP" : "CYBER OFFLINE");
                OpsText.Set(traceText, active ? "TRACE 0 % · CAP " + s.IntrusionCap + " INTRUSIONS · " + s.HeldTotal + "/" + CyberRules.MaxHeldPerFaction + " NODES" : "TRACE 0 %");
                OpsText.Set(heldLine, active ? "HELD: NONE" : "");
                OpsText.Set(hintLine, active ? "A + ON A NODE = EXPLOIT: CYBER BEATS SPACE (CHEAPER, FASTER, LONGER)" : "");
                SetBar(hopFill, hopBack, 0f, AvState.Ready, iw());
                SetBar(traceFill, traceBack, 0f, AvState.Ready, iw());
                TintTexts(AvState.Inert);
                return;
            }
            OpsText.Set(targetLine, CyberNetWords.IntrusionLine(mine, s, now));
            int tone = CyberNetWords.TraceTone(mine.Trace);
            AvState traceState = tone == 2 ? AvState.Danger : tone == 1 ? AvState.Caution : AvState.Ready;
            string traceWords = "TRACE " + mine.Trace + " %" + (tone == 2 ? " · BURN OR DROP NOW" : "");
            string held = "";
            if (mine.Held != null)
                for (int i = 0; i < mine.Held.Length; i++)
                    foreach (CyberNodeRow n in s.Nodes) if (n.Id == mine.Held[i]) held += (held.Length > 0 ? " · " : "") + CyberNetWords.Code(n.Kind) + " #" + n.Id;
            OpsText.Set(heldLine, "HELD: " + (held.Length > 0 ? held : "NONE YET"));
            OpsText.Set(hintLine, "HOLD EFFECTS RUN WHILE HELD · UPKEEP " + CyberRules.Upkeep(mine.Held != null ? mine.Held.Length : 0, s.DataCenterUp) + " CR PER 10 S");
            OpsText.Set(traceText, full ? traceWords : C2Kit.FitTo(traceText, traceWords + " · " + (held.Length > 0 ? held : "NOTHING HELD YET"), iw()));
            SetBar(hopFill, hopBack, CyberNetWords.HopProgress(mine, now), AvState.Info, iw());
            SetBar(traceFill, traceBack, mine.Trace / 100f, traceState, iw());
            TintTexts(traceState);
        }

        private float iw() => width - 2f - 2f * Pad;

        private static void SetBar(Image fill, Image back, float fraction, AvState tone, float w)
        {
            fill.color = OpsInk.Rail(tone);
            back.color = OpsInk.Inert;
            Vector2 size = fill.rectTransform.sizeDelta;
            fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, w * Mathf.Clamp01(fraction)), size.y);
        }

        private void TintTexts(AvState traceState)
        {
            targetLine.color = OpsInk.Ink;
            traceText.color = traceState == AvState.Inert ? OpsInk.Dim : OpsInk.Word(traceState);
            heldLine.color = OpsInk.Muted;
            hintLine.color = OpsInk.Dim;
        }

        private void PaintAnchors(CyberStateData s, float now)
        {
            if (!full) return;
            int row = 0, truck = 0, center = 0;
            if (s != null && s.Active)
                foreach (CyberAnchorRow a in s.Anchors)
                {
                    if (row >= anchorRows.Length) break;
                    int ordinal = a.Kind == AnchorKind.EwTruck ? truck++ : center++;
                    OpsText.Set(anchorRows[row], C2Kit.FitTo(anchorRows[row], CyberNetWords.AnchorLine(a, ordinal, now), width - 2f - 2f * Pad));
                    anchorRows[row].color = OpsInk.Word(a.Health == AnchorHealth.Live ? AvState.Ready : a.Health == AnchorHealth.Damaged ? AvState.Caution : AvState.Danger);
                    row++;
                }
            for (int i = 0; i < anchorRows.Length; i++)
            {
                if (i >= row) { OpsText.Set(anchorRows[i], i == 0 ? "NO EW TRUCK OR DATA CENTER STANDING" : ""); anchorRows[i].color = OpsInk.Muted; }
            }
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
            hopBack.color = OpsInk.Inert; traceBack.color = OpsInk.Inert;
            for (int i = 0; i < marks.Length; i++) marks[i]?.Restyle();
            hopButton?.Restyle(); burnButton?.Restyle(); dropButton?.Restyle();
            paintedSeq = -1; // repaint with the new palette
        }
    }
}
