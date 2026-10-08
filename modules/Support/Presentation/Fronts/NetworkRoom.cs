using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The CYBER front's room (R4, Uplink-style node graph). Top: the theatre with every enemy node, own anchor and the links between
    /// them, the trace gauge and the threat to our own network. Bottom: the selected node's dossier with HOP / BURN / DROP, and a
    /// scrolling terminal. Paints from <see cref="NetworkRoomView"/> only.
    /// </summary>
    internal sealed class NetworkRoom : IFrontRoom
    {
        private const float TopH = 540f, Gap = 8f, ColW = 270f, LinkKm = 12f;
        private const int Cols = 12, Rows = 8, MaxNodes = 16, MaxAnchors = 4, MaxHops = 3, TermLines = 16;
        private static readonly string[] KindName = { "RADAR", "SAM C2", "RELAY", "UPLINK", "DATA CENTER" };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private float heroW, colX, termW;
        private FrontMapPlate plate;

        private FrontBox graph, dossier, terminal;
        private FrontVector side, dossierGlyph;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] nodeLabels = new TMP_Text[MaxNodes], anchorLabels = new TMP_Text[MaxAnchors], hopLabels = new TMP_Text[MaxHops];
        private readonly Image[] nodeHits = new Image[MaxNodes];
        private readonly int[] hitIds = new int[MaxNodes];
        private TMP_Text traceHead, traceValue, traceWord, traceNote, traceLo, traceHi, threatHead, threatWord, threatNote, ownHead, keyHead;
        private readonly TMP_Text[] infocon = new TMP_Text[5], ownKey = new TMP_Text[3], ownVal = new TMP_Text[3], keyText = new TMP_Text[6];

        private TMP_Text dosTitle, dosHint;
        private readonly TMP_Text[] dosKey = new TMP_Text[6], dosVal = new TMP_Text[6];
        private AvControl hop, burn, drop;
        private TMP_Text actionNote;
        private Image termWell;
        private readonly TMP_Text[] term = new TMP_Text[TermLines];
        private int selected = -1;

        public NetworkRoom(IFrontActions actions, FrontSkin skin)
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
            BuildGraph(hero);
            float by = TopH + Gap, bh = h - by, dw = 560f;
            BuildDossier(hero, 0f, by, dw, bh);
            BuildTerminal(hero, dw + Gap, by, w - dw - Gap, bh);
        }

        // ---- Graph -----------------------------------------------------------------------------------------------------

        private TMP_Text Head(RectTransform b, string name, float x, float y, float w, string text)
        {
            TMP_Text t = FrontKit.Mono(b, name, x, y, w, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            t.text = text;
            return t;
        }

        private const float TraceY = 34f, KeyY = 436f;
        private static readonly Rect TraceCard = new Rect(0f, 29f, ColW + 12f, 238f), NetCard = new Rect(8f, 432f, 560f, 80f);

        private void BuildGraph(RectTransform hero)
        {
            graph = new FrontBox(hero, skin, "Network", 0f, 0f, heroW, TopH, "CYBER SITUATION · THEATRE NODE GRAPH", AvIcon.ShieldLock);
            RectTransform b = graph.Rect;
            // The plate fills the whole box body and clips its own vector layer: an EW truck's reach disc can be bigger than the plate.
            plate = new FrontMapPlate(b, skin, heroW, TopH, Cols, Rows);
            side = FrontVector.Add(b, "Side"); // the overlay cards (trace, threat, own network, key) sit on the map
            AvLay.Place(side.rectTransform, 0f, 0f, heroW, TopH);
            plate.Labels.Fixed(new Rect(colX - 6f, TraceCard.y, TraceCard.width, TraceCard.height));
            plate.Labels.Fixed(NetCard);
            airbases = new FrontMap.AirbaseLayer(b);
            for (int i = 0; i < MaxNodes; i++)
            {
                nodeLabels[i] = FrontKit.Mono(b, "NodeLabel" + i, 0f, 0f, 150f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
                nodeLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxAnchors; i++)
            {
                anchorLabels[i] = FrontKit.Mono(b, "AnchorLabel" + i, 0f, 0f, 150f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
                anchorLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxHops; i++)
            {
                hopLabels[i] = FrontKit.Mono(b, "HopLabel" + i, 0f, 0f, 50f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                hopLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxNodes; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "NodeHit" + i, 0f, 0f, 30f, 30f, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => actions.SelectNode(hitIds[slot])); };
                hit.gameObject.SetActive(false);
                nodeHits[i] = hit;
            }

            traceHead = Head(b, "TraceHead", colX, TraceY, ColW, "INTRUSION TRACE");
            traceValue = FrontKit.Mono(b, "TraceValue", colX + ColW * 0.5f - 65f, 84f, 130f, 34f, 26f, TextAlignmentOptions.Midline, true);
            traceWord = FrontKit.Mono(b, "TraceWord", colX, 126f, ColW, 24f, 16f, TextAlignmentOptions.Midline, true, 2f);
            traceNote = FrontKit.Mono(b, "TraceNote", colX, 152f, ColW, 16f, 10.5f, TextAlignmentOptions.Midline);
            traceLo = FrontKit.Mono(b, "TraceLo", colX + 6f, 110f, 40f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            traceHi = FrontKit.Mono(b, "TraceHi", colX + ColW - 46f, 110f, 40f, 14f, 10f, TextAlignmentOptions.MidlineRight);
            traceLo.text = "0"; traceHi.text = "100";
            threatHead = Head(b, "ThreatHead", colX, 178f, ColW, "NETWORK THREAT · INFOCON");
            for (int i = 0; i < 5; i++)
            {
                infocon[i] = FrontKit.Mono(b, "Infocon" + i, colX + i * 55f, 196f, 50f, 28f, 14f, TextAlignmentOptions.Midline, true);
                infocon[i].text = (5 - i).ToString();
            }
            threatWord = FrontKit.Mono(b, "ThreatWord", colX, 228f, ColW, 18f, 12.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            threatNote = FrontKit.Mono(b, "ThreatNote", colX, 246f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            ownHead = Head(b, "OwnHead", 16f, KeyY, 250f, "OWN NETWORK");
            string[] keys = { "HELD NODES", "EW TRUCKS", "DATA CENTER" };
            for (int i = 0; i < 3; i++)
            {
                ownKey[i] = FrontKit.Mono(b, "OwnKey" + i, 16f, KeyY + 20f + i * 17f, 120f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                ownKey[i].text = keys[i];
                ownVal[i] = FrontKit.Mono(b, "OwnVal" + i, 126f, KeyY + 20f + i * 17f, 140f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            }
            keyHead = Head(b, "KeyHead", 300f, KeyY, 200f, "KEY");
            string[] words = { "HELD", "HOPPING", "IN REACH", "OUT OF REACH", "TRACED", "OWN ANCHOR" };
            for (int i = 0; i < 6; i++)
            {
                keyText[i] = FrontKit.Mono(b, "Key" + i, 300f + 26f + (i / 3) * 130f, KeyY + 20f + (i % 3) * 17f, 100f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                keyText[i].text = words[i];
            }
            skin.Add(() =>
            {
                traceHead.color = AvInk.Dim; threatHead.color = AvInk.Dim; ownHead.color = AvInk.Dim; keyHead.color = AvInk.Dim;
                traceLo.color = AvInk.Dim; traceHi.color = AvInk.Dim;
                foreach (TMP_Text t in ownKey) t.color = AvInk.Dim;
                foreach (TMP_Text t in keyText) t.color = AvInk.Dim;
            });
        }

        // ---- Dossier and terminal ----------------------------------------------------------------------------------------

        private void BuildDossier(RectTransform hero, float x, float y, float w, float h)
        {
            dossier = new FrontBox(hero, skin, "Dossier", x, y, w, h, "NODE DOSSIER", AvIcon.ListDetails);
            RectTransform b = dossier.Rect;
            dossierGlyph = FrontVector.Add(b, "Glyph");
            AvLay.Place(dossierGlyph.rectTransform, 0f, 0f, w, h);
            dosTitle = FrontKit.Cond(b, "DosTitle", 60f, 30f, w - 72f, 26f, 18f, TextAlignmentOptions.MidlineLeft);
            dosHint = FrontKit.Mono(b, "DosHint", 60f, 56f, w - 72f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            string[] keys = { "KIND", "GRID", "DEFENSES", "HOLDING IT", "EXPLOIT", "STATUS" };
            for (int i = 0; i < 6; i++)
            {
                float ry = 84f + i * 30f;
                dosKey[i] = FrontKit.Mono(b, "DosKey" + i, 12f, ry, 100f, 26f, 10.5f, TextAlignmentOptions.MidlineLeft, false, 1f);
                dosKey[i].text = keys[i];
                dosVal[i] = FrontKit.Mono(b, "DosVal" + i, 116f, ry, w - 128f, 26f, 11f, TextAlignmentOptions.MidlineLeft);
            }
            float bw = (w - 16f - 12f) / 3f, byy = h - 74f;
            hop = Button(b, "HOP", 8f, byy, bw, AvButtonStyle.Primary, () => Do(() => actions.Hop(selected)), "Hop the intrusion onto this node. Free, but the trace climbs while you hold nodes.");
            burn = Button(b, "BURN", 14f + bw, byy, bw, AvButtonStyle.Danger, () => Do(() => actions.Burn(selected)), "Burn this node: cash in what holding it gives, once. The intrusion ends.");
            drop = Button(b, "DROP", 20f + 2f * bw, byy, bw, AvButtonStyle.Default, () => Do(() => actions.Drop(selected)), "Let go of this node. The trace stops climbing for it.");
            actionNote = FrontKit.Mono(b, "ActionNote", 10f, h - 32f, w - 20f, 20f, 10.5f, TextAlignmentOptions.MidlineLeft);
            skin.Add(() => { dosHint.color = AvInk.Dim; foreach (TMP_Text t in dosKey) t.color = AvInk.Dim; });
        }

        private AvControl Button(RectTransform p, string label, float x, float y, float w, AvButtonStyle style, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, style));
            AvLay.Place(c.Rect, x, y, w, 34f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        private void BuildTerminal(RectTransform hero, float x, float y, float w, float h)
        {
            terminal = new FrontBox(hero, skin, "Terminal", x, y, w, h, "TERMINAL", AvIcon.Typography);
            termW = w;
            RectTransform b = terminal.Rect;
            termWell = FrontKit.Solid(b, "Well", 8f, 30f, w - 16f, h - 38f, Color.clear);
            for (int i = 0; i < TermLines; i++)
                term[i] = FrontKit.Mono(b, "Term" + i, 14f, 34f + i * 19f, w - 28f, 18f, 11f, TextAlignmentOptions.MidlineLeft);
            skin.Add(() => termWell.color = AvInk.Ground.WithAlpha(0.92f));
        }

        // ---- Paint -----------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            NetworkRoomView n = v.Network;
            selected = n.SelectedId;
            PaintGraph(v, n);
            PaintDossier(n);
            PaintTerminal(n);
        }

        private Vector2 P(float x, float y) => plate.P(x, y);

        private static Color StateInk(NetNodeState s) =>
            s == NetNodeState.Held ? Own : s == NetNodeState.Hopping ? AvInk.Key : s == NetNodeState.InReach ? AvInk.State(AvState.Caution)
            : s == NetNodeState.Traced ? AvInk.State(AvState.Danger) : AvInk.Dim;

        private static string StateWord(in NetNodeView n) =>
            n.State == NetNodeState.Held ? "HELD" : n.State == NetNodeState.Hopping ? "HOPPING " + n.HopPct + " %" : n.State == NetNodeState.InReach ? "IN REACH"
            : n.State == NetNodeState.Traced ? "TRACED" : "OUT OF REACH";

        private static int IndexOf(List<NetNodeView> l, int id)
        {
            for (int i = 0; i < l.Count; i++) if (l[i].Id == id) return i;
            return -1;
        }

        private void PaintGraph(FrontRoomView v, NetworkRoomView n)
        {
            FrontMapView mv = plate.View;
            Vector2 sum = Vector2.zero;
            int na = 0;
            for (int i = 0; i < n.Anchors.Count; i++) { sum += new Vector2(n.Anchors[i].X, n.Anchors[i].Y); na++; }
            if (na == 0) for (int i = 0; i < n.Nodes.Count; i++) { sum += new Vector2(n.Nodes[i].X, n.Nodes[i].Y); na++; }
            if (na > 0) mv.AutoCenter(sum / na);
            plate.Begin();
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Color own = Own, amber = AvInk.State(AvState.Caution), danger = AvInk.State(AvState.Danger), key = AvInk.Key, dim = AvInk.Dim;
            int count = Math.Min(n.Nodes.Count, MaxNodes);
            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, P);

            // Edges: nearby nodes first, then each truck's reach, then the intrusion's own path on top.
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    NetNodeView a = n.Nodes[i], b = n.Nodes[j];
                    float km = new Vector2((a.X - b.X) * n.MapKmW, (a.Y - b.Y) * n.MapKmH).magnitude;
                    if (km > LinkKm) continue;
                    bool both = a.State == NetNodeState.Held && b.State == NetNodeState.Held, burned = a.State == NetNodeState.Traced || b.State == NetNodeState.Traced;
                    if (both) g.Line(P(a.X, a.Y), P(b.X, b.Y), 2f, own.WithAlpha(0.8f));
                    else if (burned) g.Dashed(P(a.X, a.Y), P(b.X, b.Y), 1.1f, danger.WithAlpha(0.5f), 4f, 4f);
                    else g.Line(P(a.X, a.Y), P(b.X, b.Y), 1f, dim.WithAlpha(0.38f));
                }
            for (int t = 0; t < n.Anchors.Count && t < MaxAnchors; t++)
            {
                NetAnchorView a = n.Anchors[t];
                if (a.Kind != AnchorKind.EwTruck || a.Health == AnchorHealth.Down) continue;
                for (int i = 0; i < count; i++)
                {
                    NetNodeView nd = n.Nodes[i];
                    if (nd.State != NetNodeState.InReach) continue;
                    if ((P(nd.X, nd.Y) - P(a.X, a.Y)).magnitude <= a.Reach * mv.Scale) g.Dashed(P(a.X, a.Y), P(nd.X, nd.Y), 1.1f, amber.WithAlpha(0.55f), 4f, 4f);
                }
            }
            int hl = 0;
            for (int i = 0; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                if (nd.State != NetNodeState.Held && nd.State != NetNodeState.Hopping) continue;
                Vector2 to = P(nd.X, nd.Y), from;
                int s = nd.FromNode >= 0 ? IndexOf(n.Nodes, nd.FromNode) : -1;
                if (s >= 0) from = P(n.Nodes[s].X, n.Nodes[s].Y);
                else if (nd.FromAnchor >= 0 && nd.FromAnchor < n.Anchors.Count) from = P(n.Anchors[nd.FromAnchor].X, n.Anchors[nd.FromAnchor].Y);
                else continue;
                if (nd.State == NetNodeState.Held)
                {
                    g.Line(from, to, 6f, own.WithAlpha(0.12f));
                    g.Line(from, to, 2.2f, own);
                    continue;
                }
                float f = Mathf.Clamp01(nd.HopPct / 100f);
                Vector2 lead = Vector2.Lerp(from, to, f), u = (to - from).normalized, nrm = new Vector2(-u.y, u.x);
                g.Dashed(from, to, 1.6f, key.WithAlpha(0.5f), 7f, 5f);
                g.Line(from, lead, 2.6f, key);
                for (int k = 0; k < 3; k++)   // packets crawling along the opened part
                {
                    float tt = (k / 3f + (v.Now * 0.45f) % 1f) % 1f;
                    g.Disc(Vector2.Lerp(from, lead, tt), 2.4f, key, 10);
                }
                g.Tri(lead + u * 8f, lead - u * 3f + nrm * 5f, lead - u * 3f - nrm * 5f, key);
                if (hl < MaxHops)
                {
                    TMP_Text t = hopLabels[hl++];
                    t.gameObject.SetActive(true);
                    FrontKit.Set(t, nd.HopPct + " %", key);
                    Vector2 mid = (from + to) * 0.5f + nrm * 10f;
                    AvLay.Place(t.rectTransform, mid.x - 2f, mid.y - 7f, 44f, 14f);
                    labels.Take(new Rect(mid.x - 6f, mid.y - 8f, 42f, 16f));
                    g.Rect(mid.x - 6f, mid.y - 8f, 38f, 16f, AvInk.Ground.WithAlpha(0.78f));
                }
            }
            for (int i = hl; i < MaxHops; i++) hopLabels[i].gameObject.SetActive(false);

            // Glyphs.
            for (int t = 0; t < n.Anchors.Count && t < MaxAnchors; t++)
            {
                NetAnchorView a = n.Anchors[t];
                Vector2 p = P(a.X, a.Y);
                Color k = a.Health == AnchorHealth.Live ? own : a.Health == AnchorHealth.Damaged ? amber : danger;
                if (a.Kind == AnchorKind.EwTruck && a.Reach > 0f)
                {
                    g.Disc(p, a.Reach * mv.Scale, k.WithAlpha(a.Health == AnchorHealth.Down ? 0.02f : 0.05f), 64);
                    g.Ring(p, a.Reach * mv.Scale, 1.2f, k.WithAlpha(a.Health == AnchorHealth.Down ? 0.2f : 0.6f), 72, 1f, 1f);
                }
                g.Disc(p, 13f, AvInk.Ground.WithAlpha(0.9f), 20);
                if (a.Kind == AnchorKind.EwTruck) Truck(g, p, k);
                else
                {
                    g.Frame(p.x - 11f, p.y - 11f, 22f, 22f, 1.4f, k);
                    g.Frame(p.x - 8f, p.y - 8f, 16f, 16f, 1.2f, k);
                    for (int r = -1; r <= 1; r += 2) g.Line(p + new Vector2(-4f, r * 3f), p + new Vector2(4f, r * 3f), 1.4f, k);
                }
                if (a.Health == AnchorHealth.Down) Cross(g, p, 9f, danger);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }
            for (int i = 0; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                Vector2 p = P(nd.X, nd.Y);
                Color k = StateInk(nd.State);
                if (nd.State == NetNodeState.Held) g.Disc(p, 15f, own.WithAlpha(0.2f), 24);
                g.Disc(p, 12f, AvInk.Ground.WithAlpha(0.88f), 20);
                Glyph(g, nd.Kind, p, 9f, k, nd.State == NetNodeState.Held ? 2f : 1.5f);
                if (nd.State == NetNodeState.Traced)
                {
                    Cross(g, p, 10f, danger);
                    g.Ring(p, 17f, 1.2f, danger.WithAlpha(0.8f), 28, 1f, 1f);
                }
                if (nd.Id == n.SelectedId) Brackets(g, p, 18f, AvInk.Select);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }
            for (int i = 0; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                Vector2 p = P(nd.X, nd.Y);
                TMP_Text t = nodeLabels[i];
                if (!plate.Shown(p, 12f)) { t.gameObject.SetActive(false); nodeHits[i].gameObject.SetActive(false); continue; }
                t.gameObject.SetActive(true);
                Color k = StateInk(nd.State);
                t.text = C2Kit.Tint("<b>" + nd.Label + "</b> · " + KindName[(int)nd.Kind], nd.State == NetNodeState.OutOfReach ? dim : k) + "\n" + C2Kit.Tint(StateWord(nd), nd.State == NetNodeState.Held || nd.State == NetNodeState.Hopping ? k : dim);
                t.color = AvInk.Ink;
                FrontMap.Plate(g, labels.Place(t, p, 16f, false));
                Image hit = nodeHits[i];
                hit.gameObject.SetActive(true);
                hitIds[i] = nd.Id;
                AvLay.Place(hit.rectTransform, p.x - 15f, p.y - 15f, 30f, 30f);
            }
            for (int i = count; i < MaxNodes; i++) { nodeLabels[i].gameObject.SetActive(false); nodeHits[i].gameObject.SetActive(false); }
            for (int t = 0; t < MaxAnchors; t++)
            {
                TMP_Text lt = anchorLabels[t];
                if (t >= n.Anchors.Count) { lt.gameObject.SetActive(false); continue; }
                NetAnchorView a = n.Anchors[t];
                if (!plate.Shown(P(a.X, a.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                Color k = a.Health == AnchorHealth.Live ? own : a.Health == AnchorHealth.Damaged ? amber : danger;
                string state = a.Health == AnchorHealth.Down ? "DOWN · REBUILD " + a.Rebuild + " %" : (a.Health == AnchorHealth.Damaged ? "DAMAGED" : "LIVE") + (a.Reach > 0f ? " · REACH " + Mathf.RoundToInt(a.Reach * n.MapKmW) + " KM" : "");
                lt.text = C2Kit.Tint("<b>" + a.Label + "</b>", k) + "\n" + C2Kit.Tint(state, dim);
                lt.color = AvInk.Ink;
                FrontMap.Plate(g, labels.Place(lt, P(a.X, a.Y), 16f, false));
            }
            airbases.Names(g, v.Airbases, P, mv, (t, a) => labels.Place(t, a, 9f, true, true));

            int inReach = 0, held = 0;
            for (int i = 0; i < count; i++) { if (n.Nodes[i].State == NetNodeState.InReach) inReach++; if (n.Nodes[i].State == NetNodeState.Held) held++; }
            for (int i = 0; i < n.Anchors.Count; i++) plate.Dot(n.Anchors[i].X, n.Anchors[i].Y, own);
            for (int i = 0; i < count; i++) plate.Dot(n.Nodes[i].X, n.Nodes[i].Y, StateInk(n.Nodes[i].State));
            plate.End();

            side.Clear();
            PaintSide(side, n);
            side.Flush();
            graph.SetMeta(n.Active ? count + " NODES MAPPED · " + inReach + " IN REACH · " + held + " HELD" : n.Meta, held > 0 ? FrontKit.Tone(AvState.Ready) : dim);
        }

        public bool Tick(Vector2 mouse) => plate.Tick(mouse);

        private void PaintSide(FrontVector g, NetworkRoomView n)
        {
            FrontMapPlate.Card(g, colX - 6f, TraceCard.y, TraceCard.width, TraceCard.height);
            FrontMapPlate.Card(g, NetCard.x, NetCard.y, NetCard.width, NetCard.height);

            // Trace gauge: a half dial of ticks, lit up to the trace, coloured by zone.
            Vector2 c = new Vector2(colX + ColW * 0.5f, 118f);
            const float r = 70f;
            float trace = n.HasIntrusion ? n.Trace : 0f;
            const int ticks = 36;
            for (int i = 0; i < ticks; i++)
            {
                float a = Mathf.PI + Mathf.PI * (i + 0.5f) / ticks, pct = (i + 0.5f) / ticks * 100f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                bool lit = n.HasIntrusion && pct <= trace;
                g.Line(c + d * (r - 14f), c + d * r, 4f, lit ? Zone(pct) : AvInk.Hairline.WithAlpha(0.8f));
            }
            Vector2 pa = c + new Vector2(-r - 4f, 0f);
            for (int i = 1; i <= 60; i++)
            {
                float a = Mathf.PI + Mathf.PI * i / 60f;
                Vector2 pb = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + 4f);
                g.Line(pa, pb, 1f, AvInk.Frame.WithAlpha(0.7f));
                pa = pb;
            }
            float na = Mathf.PI + Mathf.PI * Mathf.Clamp01(trace / 100f);
            Vector2 nd = new Vector2(Mathf.Cos(na), Mathf.Sin(na));
            Color zone = n.HasIntrusion ? Zone(trace) : AvInk.Muted;
            g.Line(c + nd * 22f, c + nd * (r - 18f), 2f, zone);
            g.Disc(c, 3.5f, zone, 14);
            string word = !n.HasIntrusion ? "NO INTRUSION" : trace < 25f ? "CLEAN" : trace < 60f ? "WARM" : trace < 85f ? "HOT" : "BURN NOW";
            FrontKit.Set(traceValue, n.HasIntrusion ? n.Trace + " %" : "--", n.HasIntrusion ? zone : AvInk.Dim);
            FrontKit.Set(traceWord, word, n.HasIntrusion ? zone : AvInk.Dim);
            FrontKit.Set(traceNote, C2Kit.FitTo(traceNote, n.TraceNote, ColW), AvInk.Dim);
            if (n.HasIntrusion && trace >= 85f) g.Frame(colX + 50f, 126f, ColW - 100f, 24f, 1.6f, zone);

            // Threat to our own network: five blocks, lit from calm (5) down to the current level.
            byte level = (byte)Mathf.Clamp(n.Infocon, 1, 5);
            Color tone = level >= 4 ? AvInk.State(AvState.Ready) : level == 3 ? AvInk.State(AvState.Caution) : AvInk.State(AvState.Danger);
            for (int i = 0; i < 5; i++)
            {
                int lv = 5 - i;
                bool lit = lv >= level;
                float bx = colX + i * 55f;
                g.Rect(bx, 196f, 50f, 28f, lit ? tone.WithAlpha(0.28f) : AvInk.Ground.WithAlpha(0.9f));
                g.Frame(bx, 196f, 50f, 28f, 1.2f, lit ? tone : AvInk.Hairline);
                FrontKit.Set(infocon[i], lv.ToString(), lit ? AvInk.Ink : AvInk.Muted);
            }
            FrontKit.Set(threatWord, "INFOCON " + level + " · " + n.ThreatWord, FrontKit.Tone(level >= 4 ? AvState.Ready : level == 3 ? AvState.Caution : AvState.Danger));
            FrontKit.Set(threatNote, C2Kit.FitTo(threatNote, n.ThreatNote, ColW), AvInk.Dim);

            // Own network.
            int trucksUp = 0, trucks = 0, dc = 0, dcUp = 0;
            for (int i = 0; i < n.Anchors.Count; i++)
            {
                NetAnchorView a = n.Anchors[i];
                if (a.Kind == AnchorKind.EwTruck) { trucks++; if (a.Health != AnchorHealth.Down) trucksUp++; }
                else { dc++; if (a.Health != AnchorHealth.Down) dcUp++; }
            }
            FrontKit.Set(ownVal[0], n.HeldTotal + " / " + n.IntrusionCap, n.HeldTotal > 0 ? FrontKit.Tone(AvState.Ready) : AvInk.Dim);
            FrontKit.Set(ownVal[1], trucksUp + " / " + trucks + " UP", trucksUp == trucks && trucks > 0 ? FrontKit.Tone(AvState.Ready) : FrontKit.Tone(AvState.Caution));
            FrontKit.Set(ownVal[2], dc == 0 ? "NONE BUILT" : dcUp > 0 ? "UP" : "DOWN", dcUp > 0 ? FrontKit.Tone(AvState.Ready) : dc == 0 ? AvInk.Dim : FrontKit.Tone(AvState.Danger));

            // Key swatches.
            Color[] ink = { Own, AvInk.Key, AvInk.State(AvState.Caution), AvInk.Dim, AvInk.State(AvState.Danger), Own };
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = new Vector2(300f + 12f + (i / 3) * 130f, KeyY + 28f + (i % 3) * 17f);
                if (i == 0) { g.Line(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 2.2f, ink[i]); g.Disc(p, 3.5f, ink[i], 10); }
                else if (i == 1) g.Dashed(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 2f, ink[i], 4f, 3f);
                else if (i == 2) g.Dashed(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 1.2f, ink[i], 3f, 3f);
                else if (i == 3) g.Line(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 1f, ink[i].WithAlpha(0.5f));
                else if (i == 4) Cross(g, p, 5f, ink[i]);
                else { g.Ring(p, 7f, 1.2f, ink[i].WithAlpha(0.7f), 18, 1f, 1f); g.Disc(p, 2.5f, ink[i], 8); }
            }
        }

        private static Color Zone(float pct) =>
            pct < 25f ? AvInk.State(AvState.Ready) : pct < 60f ? AvInk.State(AvState.Caution) : pct < 85f ? AvInk.State(AvState.Danger) : AvInk.Hostile;

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

        private static void Truck(FrontVector g, Vector2 p, Color k)
        {
            g.Frame(p.x - 9f, p.y - 2f, 18f, 8f, 1.4f, k);
            g.Rect(p.x + 3f, p.y - 2f, 6f, 8f, k.WithAlpha(0.5f));
            g.Disc(p + new Vector2(-5f, 7f), 2f, k, 8);
            g.Disc(p + new Vector2(5f, 7f), 2f, k, 8);
            g.Line(p + new Vector2(-3f, -2f), p + new Vector2(-3f, -8f), 1.4f, k);
            g.Line(p + new Vector2(-8f, -9f), p + new Vector2(2f, -9f), 2f, k);
        }

        /// <summary>One glyph per node kind, centred on <paramref name="p"/>.</summary>
        internal static void Glyph(FrontVector g, NodeKind kind, Vector2 p, float r, Color k, float w)
        {
            switch (kind)
            {
                case NodeKind.Radar:
                    g.Ring(p, r, w, k, 24);
                    g.Line(p, p + new Vector2(r * 0.85f, -r * 0.55f), w, k);
                    g.Disc(p, 2f, k, 8);
                    break;
                case NodeKind.SamC2:
                    g.Polyline(new[] { p + new Vector2(0f, -r), p + new Vector2(r * 0.95f, r * 0.8f), p + new Vector2(-r * 0.95f, r * 0.8f) }, w, k, true);
                    g.Line(p + new Vector2(0f, -r * 0.2f), p + new Vector2(0f, r * 0.45f), w, k);
                    break;
                case NodeKind.Relay:
                    var hex = new Vector2[6];
                    for (int i = 0; i < 6; i++) { float a = Mathf.PI / 3f * i; hex[i] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
                    g.Polyline(hex, w, k, true);
                    g.Disc(p, 2.4f, k, 8);
                    break;
                case NodeKind.Uplink:
                    g.Polyline(new[] { p + new Vector2(0f, r * 0.2f - r), p + new Vector2(r, r * 0.2f), p + new Vector2(0f, r * 1.1f), p + new Vector2(-r, r * 0.2f) }, w, k, true);
                    g.Line(p + new Vector2(0f, -r), p + new Vector2(0f, -r - 4f), w, k);
                    break;
                default:
                    g.Frame(p.x - r * 0.85f, p.y - r * 0.85f, r * 1.7f, r * 1.7f, w, k);
                    for (int i = -1; i <= 1; i += 2) g.Line(p + new Vector2(-r * 0.5f, i * r * 0.4f), p + new Vector2(r * 0.5f, i * r * 0.4f), w * 0.8f, k);
                    break;
            }
        }

        private void PaintDossier(NetworkRoomView n)
        {
            FrontVector g = dossierGlyph;
            g.Clear();
            NetDossierView d = n.Dossier;
            dossier.SetMeta(d.Has ? "SELECTED" : "NO NODE SELECTED", d.Has ? AvInk.Key : AvInk.Dim);
            if (d.Has)
            {
                Color k = Own;
                Vector2 p = new Vector2(32f, 56f);
                g.Frame(p.x - 20f, p.y - 20f, 40f, 40f, 1.2f, AvInk.Hairline);
                for (int i = 0; i < n.Nodes.Count; i++)
                    if (n.Nodes[i].Id == n.SelectedId) { k = StateInk(n.Nodes[i].State); Glyph(g, n.Nodes[i].Kind, p, 13f, k, 2.2f); }
                FrontKit.Set(dosTitle, d.Title, AvInk.Ink);
                FrontKit.Set(dosHint, C2Kit.FitTo(dosHint, d.Kind, dossier.Width - 72f), AvInk.Dim);
                string[] vals = { d.Kind, d.Grid, d.Defenses, d.Effect, d.Exploit, d.Status };
                for (int i = 0; i < 6; i++)
                    FrontKit.Set(dosVal[i], C2Kit.FitTo(dosVal[i], vals[i], dossier.Width - 128f), i == 5 ? FrontKit.Tone(d.StatusTone) : i == 4 && d.Exploit.StartsWith("ARMED") ? FrontKit.Tone(AvState.Caution) : AvInk.Ink);
            }
            else
            {
                FrontKit.Set(dosTitle, "SELECT A NODE", AvInk.Dim);
                FrontKit.Set(dosHint, "Click a node on the graph.", AvInk.Dim);
                for (int i = 0; i < 6; i++) FrontKit.Set(dosVal[i], "--", AvInk.Muted);
            }
            g.Flush();
            hop.Interactable = n.CanHop; burn.Interactable = n.CanBurn; drop.Interactable = n.CanDrop;
            FrontKit.Set(actionNote, C2Kit.FitTo(actionNote, n.ActionNote, dossier.Width - 20f), AvInk.Dim);
        }

        private void PaintTerminal(NetworkRoomView n)
        {
            terminal.SetMeta(n.Session, AvInk.Dim);
            int rows = TermLines - 1, from = Math.Max(0, n.Terminal.Count - rows), row = 0;
            for (int i = from; i < n.Terminal.Count; i++, row++)
            {
                FrontLogLine l = n.Terminal[i];
                float tw = termW - 28f - 56f;
                term[row].text = C2Kit.Tint(l.Stamp, AvInk.Muted) + "  " + C2Kit.Tint(C2Kit.FitTo(term[row], l.Text, tw), l.Tone == AvState.Inert ? AvInk.Dim : FrontKit.Tone(l.Tone));
                term[row].color = AvInk.Ink;
            }
            for (; row < rows; row++) term[row].text = "";
            FrontKit.Set(term[TermLines - 1], ">> _", FrontKit.Tone(AvState.Ready));
        }
    }
}
