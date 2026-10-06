using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
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
    internal sealed class CyberNetPage : C2MapPage
    {
        private const float Gap = 6f, Pad = 8f, ButtonH = 26f;
        private const int Marks = CyberWire.MaxNodes, Lines = 24, Notes = 4;
        private readonly Action<int> hop, burn, drop;
        private readonly C2Box intrusion, anchors;
        private readonly AvLineGraphic[] edges = new AvLineGraphic[Lines];
        private readonly AvLineGraphic[] reach = new AvLineGraphic[AnchorRules.MaxTrucks];
        private readonly AvControl[] marks = new AvControl[Marks];
        private readonly int[] markIds = new int[Marks];
        private readonly Image[] markSelect = new Image[Marks];
        private readonly Image[] anchorDots = new Image[CyberWire.MaxAnchors];
        private readonly TMP_Text[] anchorTags = new TMP_Text[CyberWire.MaxAnchors];
        private readonly TMP_Text targetLine, traceText, heldLine, hintLine;
        private readonly Image hopBack, hopFill, traceBack, traceFill;
        private readonly AvControl hopButton, burnButton, dropButton;
        private readonly TMP_Text[] anchorRows = new TMP_Text[Notes];
        private readonly List<EwSource> sources = new List<EwSource>(2);
        private readonly List<CyberNode> graphNodes = new List<CyberNode>(CyberWire.MaxNodes);
        private readonly List<CyberEdge> graphEdges = new List<CyberEdge>(CyberGraph.MaxEdges);
        private readonly HashSet<int> heldIds = new HashSet<int>();
        private int selected, ownHeldLast;
        // Pooled paint arrays (AvLineGraphic.SetPoints copies them), so a repaint allocates nothing.
        private const int CirclePoints = 64;
        private readonly float[] circleX = new float[CirclePoints + 1], circleY = new float[CirclePoints + 1];
        private readonly float[] edgeX = new float[2], edgeY = new float[2];
        private int paintedSelected = -1;
        private bool hasOwn;

        /// <summary>The footer words of this page: the standing hint, replaced by the CYBER verdict or a refusal.</summary>
        public string Words { get; private set; } = "SELECT A NODE ON THE MAP · HOP STARTS AN INTRUSION";

        public CyberNetPage(RectTransform parent, float width, float height, CallsController calls, Action<AvPart> register,
            Action<int> hop, Action<int> burn, Action<int> drop, OpsBoxActions opsActions = null)
            : base(width, height, register, ButtonH, CallFamily.Cyber)
        {
            this.hop = hop; this.burn = burn; this.drop = drop;

            float gap = full ? Gap : 4f;
            int consoleLines = full ? 4 : 2;
            float consoleH = C2ConsoleView.HeightFor(consoleLines);
            float rowH = full ? 40f : 26f;
            float callsBody = CallsBody(rowH);
            float intrusionBody = full ? 124f : 96f;
            float anchorsBody = full ? Notes * 16f + 8f : 0f;
            float fixedH = gap + consoleH + gap + (C2Box.HeaderH + intrusionBody) + gap + OpsBox.HeightFor(full) + gap + (full ? C2Box.HeaderH + anchorsBody + gap : 0f) +
                (C2Box.HeaderH + callsBody) + gap + C2Box.HeaderH + 2f;
            float mapBody = Mathf.Max(110f, height - fixedH);

            float y = gap;
            BuildConsole(parent, consoleLines, y, consoleH);
            y += consoleH + gap;

            BuildMap(parent, "INTRUSION MAP", "NO EW ASSETS ONLINE", y, mapBody);
            y += C2Box.HeaderH + mapBody + gap;
            for (int i = 0; i < reach.Length; i++) reach[i] = Line(map.Body, "Reach" + i, 1.2f);
            for (int i = 0; i < edges.Length; i++) edges[i] = Line(map.Body, "Edge" + i, 1.4f);
            for (int i = 0; i < anchorDots.Length; i++)
            {
                anchorDots[i] = Dot(map.Body, "Anchor" + i);
                anchorTags[i] = Tag(map.Body, "AnchorTag" + i);
            }
            for (int i = 0; i < marks.Length; i++)
            {
                int slot = i;
                markSelect[i] = Dot(map.Body, "Select" + i);
                marks[i] = AvControl.Make(map.Body, new AvControl.Spec("", () => Pick(markIds[slot]), AvButtonStyle.Quiet));
                marks[i].SingleLine();
                marks[i].Rect.gameObject.SetActive(false);
            }
            BuildHeadline();

            intrusion = Make(new C2Box(parent, "ACTIVE INTRUSION"));
            intrusion.BodyHeight = intrusionBody;
            intrusion.SetMeta("NONE");
            intrusion.Place(new AvSlot(0f, y, width, C2Box.HeaderH + intrusionBody));
            y += C2Box.HeaderH + intrusionBody + gap;
            float iw = width - 2f;
            targetLine = C2Kit.Mono(intrusion.Body, "Target", 11f, TextAlignmentOptions.MidlineLeft, true, 1f);
            AvLay.Place(targetLine, Pad, 3f, iw - 2f * Pad, 16f);
            hopBack = AvLay.Solid(intrusion.Body, "HopBack", Color.clear);
            hopFill = AvLay.Solid(intrusion.Body, "HopFill", Color.clear);
            AvLay.Place(hopBack.rectTransform, Pad, 21f, iw - 2f * Pad, 6f);
            AvLay.Place(hopFill.rectTransform, Pad, 21f, 0f, 6f);
            traceText = C2Kit.Mono(intrusion.Body, "TraceText", 10f, TextAlignmentOptions.MidlineLeft, true, 1f);
            AvLay.Place(traceText, Pad, 30f, iw - 2f * Pad, 14f);
            traceBack = AvLay.Solid(intrusion.Body, "TraceBack", Color.clear);
            traceFill = AvLay.Solid(intrusion.Body, "TraceFill", Color.clear);
            AvLay.Place(traceBack.rectTransform, Pad, 45f, iw - 2f * Pad, 8f);
            AvLay.Place(traceFill.rectTransform, Pad, 45f, 0f, 8f);
            heldLine = C2Kit.Mono(intrusion.Body, "Held", 10f, TextAlignmentOptions.MidlineLeft);
            AvLay.Place(heldLine, Pad, 57f, iw - 2f * Pad, 14f);
            hintLine = C2Kit.Mono(intrusion.Body, "Hint", 10f, TextAlignmentOptions.MidlineLeft);
            AvLay.Place(hintLine, Pad, 71f, iw - 2f * Pad, 14f);
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

            BuildOps(parent, y, OpDomain.Cyber, new[] { OpKind.Asat, OpKind.ZeroDay }, opsActions);
            y += OpsBox.HeightFor(full) + gap;

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
                    AvLay.Place(anchorRows[i], Pad, 4f + i * 16f, iw - 2f * Pad, 16f);
                }
            }

            BuildCalls(parent, "CYBER CALLS", y, rowH, calls);
            Restyle();
        }

        // ---- Selection and presses -------------------------------------------------------------------------------

        private void Pick(int nodeId) { selected = selected == nodeId ? 0 : nodeId; paintedSelected = -1; }

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
            int opsSeq = OpsSeq(v);
            if (seq != paintedSeq || selected != paintedSelected || second != paintedSecond || opsSeq != paintedOps)
            {
                paintedSeq = seq; paintedSelected = selected; paintedSecond = second; paintedOps = opsSeq;
                ops.Paint(v.Ops, v.OpsKnown, (kind, list) => Choices(s, kind, list), now);
                PaintMap(s, now);
                PaintIntrusion(s, now);
                PaintAnchors(s, now);
            }
            Words = StandingWords(s, v.Words);
            PaintCalls(v);
        }

        /// <summary>What a CYBER operation may aim at: the three satellite classes, or a SAM C2 node the faction can see (ids as the host listed them).</summary>
        private static void Choices(CyberStateData s, OpKind kind, List<OpChoice> into)
        {
            if (kind == OpKind.Asat)
            {
                for (int i = 0; i < 3; i++) into.Add(new OpChoice(i, OpsWords.Bird(i)));
                return;
            }
            if (kind != OpKind.ZeroDay || s == null || !s.Active) return;
            foreach (CyberNodeRow n in s.Nodes) if (n.Kind == NodeKind.SamC2 && into.Count < 8) into.Add(new OpChoice(n.Id, OpsWords.TargetWord(OpKind.ZeroDay, n.Id)));
        }

        private string StandingWords(CyberStateData s, string said)
        {
            if (!string.IsNullOrEmpty(said) && C2Cap.StartsNegative(said)) return said;
            if (!string.IsNullOrEmpty(ops.Words)) return ops.Words;
            if (s == null) return "WAITING FOR THE HOST · CYBER LINK";
            if (!s.Active) return "NEGATIVE: CYBER OFFLINE — NO EW TRUCK STANDING, CALLS STILL LIVE";
            return hasOwn ? "INTRUSION RUNNING · WATCH THE TRACE BAR, BURN BEFORE IT FILLS" : "SELECT A NODE ON THE MAP · HOP STARTS AN INTRUSION";
        }

        private void PaintMap(CyberStateData s, float now)
        {
            bool active = s != null && s.Active;
            if (selected != 0 && !(active && NodeListed(s, selected))) selected = 0; // the selected node left the list (fogged, lost): nothing stays selected
            headline.gameObject.SetActive(!active || (s.Nodes.Count == 0 && s.Anchors.Count == 0));
            detail.gameObject.SetActive(headline.gameObject.activeSelf);
            if (headline.gameObject.activeSelf)
            {
                AvText.Set(headline, s == null ? "NO LINK" : "NO EW ASSETS ONLINE");
                AvText.Set(detail, s == null ? "> waiting for the host CYBER state" : "> no EW truck standing — restore one or use the CALLS below");
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
            placed.Clear();
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
                AvText.Set(anchorTags[slot], tag);
                anchorTags[slot].color = OpsInk.Word(tone);
                Vector2 tagAt = Free(Mathf.Min(mapW - 26f, p.X + 7f), p.Y - 7f, 26f, 14f);
                AvLay.Place(anchorTags[slot], tagAt.x, tagAt.y, 26f, 14f);
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
                Vector2 markAt = Free(p.X - 17f, p.Y - 9f, 34f, 18f);
                float bx = markAt.x, by = markAt.y;
                markIds[i] = n.Id;
                AvControl m = marks[i];
                m.Rect.gameObject.SetActive(true);
                m.Label = CyberNetWords.Code(n.Kind) + (n.Exploit ? "+" : "");
                m.SetStyle(n.Hopping ? AvButtonStyle.Primary : n.Held ? AvButtonStyle.Default : AvButtonStyle.Quiet);
                m.Latched = n.Held;
                m.Help = CyberNetWords.Node(n.Kind, n.Id) + (n.Held ? " · HELD" : n.Hopping ? " · HOP IN PROGRESS" : "") + (n.Exploit ? " · EXPLOIT: CYBER beats SPACE (-25 % hop time and cost, x1.5 package duration)" : "");
                AvLay.Place(m.Rect, bx, by, 34f, 18f);
                markSelect[i].gameObject.SetActive(selected == n.Id);
                markSelect[i].color = AvInk.Select;
                AvLay.Place(markSelect[i].rectTransform, bx - 2f, by - 2f, 38f, 22f);
            }
            CyberGraph.Edges(sources, graphNodes, heldIds, graphEdges);
            for (int i = 0; i < graphEdges.Count && i < edges.Length; i++)
            {
                CyberEdge e = graphEdges[i];
                if (!TryPoint(s, e.From, out MapPoint a) || !TryPoint(s, e.To, out MapPoint b)) continue;
                bool chain = e.From > 0;
                edgeX[0] = Mathf.Clamp01(a.X / mapW); edgeX[1] = Mathf.Clamp01(b.X / mapW);
                edgeY[0] = Mathf.Clamp01(1f - a.Y / mapH); edgeY[1] = Mathf.Clamp01(1f - b.Y / mapH);
                edges[i].LineColor = chain ? OpsInk.Word(AvState.Caution) : AvInk.Hairline;
                edges[i].SetPoints(edgeX, edgeY, 2);
            }
            foreach (CyberIntrusionRow x in s.Intrusions)
            {
                if (!x.Own) continue;
                hasOwn = true;
                if (x.Held != null && x.Held.Length > 0) ownHeldLast = x.Held[x.Held.Length - 1];
            }
        }

        private static bool NodeListed(CyberStateData s, int id)
        {
            for (int i = 0; i < s.Nodes.Count; i++) if (s.Nodes[i].Id == id) return true;
            return false;
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
            const int N = CirclePoints;
            for (int i = 0; i <= N; i++)
            {
                float a = Mathf.PI * 2f * i / N;
                circleX[i] = Mathf.Clamp01((center.X + radius * Mathf.Cos(a)) / mapW);
                circleY[i] = Mathf.Clamp01(1f - (center.Y + radius * Mathf.Sin(a)) / mapH);
            }
            g.LineColor = AvInk.Hairline;
            g.SetPoints(circleX, circleY, N + 1);
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
                AvText.Set(targetLine, active ? "NO INTRUSION · SELECT A NODE, THEN HOP" : "CYBER OFFLINE");
                AvText.Set(traceText, active ? "TRACE 0 % · CAP " + s.IntrusionCap + " INTRUSIONS · " + s.HeldTotal + "/" + CyberRules.MaxHeldPerFaction + " NODES" : "TRACE 0 %");
                AvText.Set(heldLine, active ? "HELD: NONE" : "");
                AvText.Set(hintLine, active ? "A + ON A NODE = EXPLOIT: CYBER BEATS SPACE (CHEAPER, FASTER, LONGER)" : "");
                SetBar(hopFill, hopBack, 0f, AvState.Ready, iw());
                SetBar(traceFill, traceBack, 0f, AvState.Ready, iw());
                TintTexts(AvState.Inert);
                return;
            }
            AvText.Set(targetLine, CyberNetWords.IntrusionLine(mine, s, now));
            int tone = CyberNetWords.TraceTone(mine.Trace);
            AvState traceState = tone == 2 ? AvState.Danger : tone == 1 ? AvState.Caution : AvState.Ready;
            string traceWords = "TRACE " + mine.Trace + " %" + (tone == 2 ? " · BURN OR DROP NOW" : "");
            string held = "";
            if (mine.Held != null)
                for (int i = 0; i < mine.Held.Length; i++)
                    foreach (CyberNodeRow n in s.Nodes) if (n.Id == mine.Held[i]) held += (held.Length > 0 ? " · " : "") + CyberNetWords.Code(n.Kind) + " #" + n.Id;
            AvText.Set(heldLine, "HELD: " + (held.Length > 0 ? held : "NONE YET"));
            AvText.Set(hintLine, "HOLD EFFECTS RUN WHILE HELD · UPKEEP " + CyberRules.Upkeep(mine.Held != null ? mine.Held.Length : 0, s.DataCenterUp) + " CR PER 10 S");
            AvText.Set(traceText, full ? traceWords : C2Kit.FitTo(traceText, traceWords + " · " + (held.Length > 0 ? held : "NOTHING HELD YET"), iw()));
            SetBar(hopFill, hopBack, CyberNetWords.HopProgress(mine, now), AvState.Info, iw());
            SetBar(traceFill, traceBack, mine.Trace / 100f, traceState, iw());
            TintTexts(traceState);
        }

        private float iw() => width - 2f - 2f * Pad;

        private static void SetBar(Image fill, Image back, float fraction, AvState tone, float w)
        {
            fill.color = OpsInk.Rail(tone);
            back.color = AvInk.Inert;
            Vector2 size = fill.rectTransform.sizeDelta;
            fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, w * Mathf.Clamp01(fraction)), size.y);
        }

        private void TintTexts(AvState traceState)
        {
            targetLine.color = AvInk.Ink;
            traceText.color = traceState == AvState.Inert ? AvInk.Dim : OpsInk.Word(traceState);
            heldLine.color = AvInk.Muted;
            hintLine.color = AvInk.Dim;
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
                    AvText.Set(anchorRows[row], C2Kit.FitTo(anchorRows[row], CyberNetWords.AnchorLine(a, ordinal, now), width - 2f - 2f * Pad));
                    anchorRows[row].color = OpsInk.Word(a.Health == AnchorHealth.Live ? AvState.Ready : a.Health == AnchorHealth.Damaged ? AvState.Caution : AvState.Danger);
                    row++;
                }
            for (int i = 0; i < anchorRows.Length; i++)
            {
                if (i >= row) { AvText.Set(anchorRows[i], i == 0 ? "NO EW TRUCK OR DATA CENTER STANDING" : ""); anchorRows[i].color = AvInk.Muted; }
            }
        }

        public void Restyle()
        {
            RestyleMap();
            hopBack.color = AvInk.Inert; traceBack.color = AvInk.Inert;
            for (int i = 0; i < marks.Length; i++) marks[i]?.Restyle();
            hopButton?.Restyle(); burnButton?.Restyle(); dropButton?.Restyle();
        }
    }
}
