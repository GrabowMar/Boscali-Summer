using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The CYBER front's room (R4, an Uplink-style intrusion terminal). The map is the theatre node graph: every node is a hex, ours (data centers, EW trucks)
    /// cyan with their reach rings, queued ones as dashed ghosts with their timers, a breach into one of ours flagged, the live route drawn as links. The column
    /// has two tabs: INFRASTRUCTURE (data centers, trucks, standing orders: NetworkInfraPanel.cs) and NODES (the session strip, one filterable list of every node
    /// with the picked one acting in place, ZERO-DAY: NetworkNodesPanel.cs). Three local map picks, one at a time: BUILD DATA CENTER (within 8 km of a held land
    /// airbase), DEPLOY TRUCK (within 20 km of a standing or queued data center) and RELOCATE (within 20 km of a standing data center); a miss says why and stays
    /// armed, Esc ends it, and no node takes a click meanwhile. Paints from <see cref="NetworkRoomView"/> only.
    /// </summary>
    internal sealed partial class NetworkRoom : IFrontRoom
    {
        private const float TopH = 540f, LinkKm = 12f, Adv = 6.3f, DragSlop = 6f, FootX = 560f, FootRight = 190f;
        private const int Cols = 12, Rows = 8, MaxHops = 3;
        /// <summary>Enemy nodes then our own anchors; then the ghost points (queued builds and a relocating truck's destination) share the spread.</summary>
        private const int MaxNodes = CyberWire.MaxNodes + CyberWire.MaxAnchors, MaxGhosts = FrontRules.MaxQueue + 1, MaxPts = MaxNodes + MaxGhosts;
        private static readonly string[] KindName = { "RADAR", "SAM C2", "RELAY", "UPLINK", "DATA CENTER" };

        // The terminal's own fixed inks (a different console in a different room: they sit on top of the pilot's scheme). Keep text on them at 4.5:1 over the glass.
        private static readonly Color Phos = RoomInk.Phosphor, PhosDim = RoomInk.PhosphorDim, TraceRed = RoomInk.Trace,
            OutInk = RoomInk.Rgb(0x5E8795), Bright = RoomInk.PhosphorInk, Ally = RoomInk.Rgb(0x8FA8FF),
            HopInk = RoomInk.Hop, Amber = RoomInk.Rgb(0xFFB020), Glass = RoomInk.Rgb(0x02090F, 0.95f);
        private static readonly Vector2[] hexPts = new Vector2[6];

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private float heroW;
        private FrontMapPlate plate;

        private FrontBox graph;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] nodeLabels = new TMP_Text[MaxNodes], hopLabels = new TMP_Text[MaxHops], ghostLabels = new TMP_Text[MaxGhosts];
        private TMP_Text footLabel;
        private readonly Image[] nodeHits = new Image[MaxNodes];
        private readonly int[] hitIds = new int[MaxNodes];
        // Screen points for one paint (nodes, then ghosts): true map points, and the drawn ones (things sharing a spot fanned apart) with their cluster.
        private readonly Vector2[] nodeTrue = new Vector2[MaxPts], nodeAt = new Vector2[MaxPts];
        private readonly int[] nodeCluster = new int[MaxPts];
        private const float StackGap = 22f;
        // The ghost points of one paint: queued builds with a point, and a packing truck's destination (room memory).
        private readonly Vector2[] ghostUv = new Vector2[MaxGhosts], ghostFrom = new Vector2[MaxGhosts];
        private readonly string[] ghostWord = new string[MaxGhosts];
        private readonly byte[] ghostKind = new byte[MaxGhosts];

        // The three local map picks (one at a time) and what a miss said; the room's own pick of one of our anchors (a negative node id, never sent).
        private enum PickMode : byte { None, Center, Truck, Relocate }
        private PickMode pick;
        private string pickMiss = "";
        private int moveTruck = -1, selOwn;
        private FrontRoomView last;
        private readonly List<Vector2> rings = new List<Vector2>(FrontRoomView.MaxAirbases);
        // A map pick is a press and a release that stay together: a pan, a zoom or a drag in between places nothing.
        private bool pressed;
        private Vector2 pressAt, pressCenter;
        private int pressZoom;
        // RELOCATE memory per truck slot (the mirror says only PACKING): when this room first saw it packing, when it sent the move and where to.
        private readonly float[] packSince = { -1f, -1f, -1f, -1f }, movedAt = { -100f, -100f, -100f, -100f };
        private readonly Vector2[] packTo = new Vector2[4];

        private int selected = -1, locateId;

        public NetworkRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        private static Color Own => Phos;

        private void Do(Action a) { actions.Touch(); a(); }

        /// <summary>The room's column panels in tab order; the window adds the FRONT tab after them.</summary>
        public IReadOnlyList<ColumnTab> Column => columnTabs;
        /// <summary>The column tab a map pick asked for since the last call, or -1.</summary>
        public int TakeTab() { int t = wantTab; wantTab = -1; return t; }
        private ColumnTab[] columnTabs = new ColumnTab[0];
        private int wantTab = -1;
        private float mapH = TopH;

        /// <summary>The NODES column tab's index (INFRASTRUCTURE is 0).</summary>
        private const int NodesTab = 1;

        public void Build(RectTransform hero, float w, float h, RectTransform column, float cw, float ch)
        {
            heroW = w; mapH = h;
            BuildGraph(hero);
            BuildInfra(column, cw, ch);
            BuildNodes(column, cw, ch);
            columnTabs = new[] { new ColumnTab("INFRASTRUCTURE", AvIcon.Database, infra.Rect), new ColumnTab("NODES", AvIcon.Link, nodesBox.Rect) };
        }

        // ---- Graph ---------------------------------------------------------------------------------------------------------

        /// <summary>A wrapping mono block (two or three lines) pinned to the top-left of its rect.</summary>
        private static TMP_Text Wrapped(RectTransform p, string name, float x, float y, float w, float h, float size, bool strong = false)
        {
            TMP_Text t = AvText.Make(p, name, strong ? AvTextRole.DataStrong : AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
            AvText.Size(t, size);
            t.richText = true;
            t.raycastTarget = false;
            t.color = Bright;
            AvLay.Place(t.rectTransform, x, y, w, h);
            return t;
        }

        private void BuildGraph(RectTransform hero)
        {
            graph = new FrontBox(hero, skin, "Network", 0f, 0f, heroW, mapH, "CYBER SITUATION · THEATRE NODE GRAPH", AvIcon.ShieldLock, FrontLook.Terminal).Tagged("tty1");
            RectTransform b = graph.Rect;
            // The plate fills the box body and clips its own vector layer: the whole room is map, the column carries the rest.
            plate = new FrontMapPlate(b, skin, heroW, mapH, Cols, Rows);
            plate.Tint = RoomInk.Rgba(0.6f, 0.88f, 1f, 1f);
            airbases = new FrontMap.AirbaseLayer(b);
            for (int i = 0; i < MaxNodes; i++)
            {
                nodeLabels[i] = FrontKit.Mono(b, "NodeLabel" + i, 0f, 0f, 150f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
                nodeLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxGhosts; i++)
            {
                ghostLabels[i] = FrontKit.Mono(b, "GhostLabel" + i, 0f, 0f, 200f, 14f, 10f, TextAlignmentOptions.TopLeft);
                ghostLabels[i].gameObject.SetActive(false);
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
                // Any node opens NODES on its row (one of ours is a room-local pick: the host never hears of it).
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => PickNode(hitIds[slot])); };
                hit.gameObject.SetActive(false);
                nodeHits[i] = hit;
            }
            // The foot of the plate, right of the cursor readout: what the armed pick wants, or why the last click missed.
            footLabel = FrontKit.Mono(b, "FootLabel", plate.X + FootX, plate.Y + plate.H - 20f, plate.W - FootX - FootRight, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
        }

        /// <summary>A node clicked on the map (never while a pick is armed: the hits are off then): NODES opens on its row, this frame.</summary>
        private void PickNode(int id)
        {
            wantTab = NodesTab;
            Pick(id, false);
        }

        /// <summary>The own pick, the filter or a map pick changed: the map, INFRASTRUCTURE and NODES all show it.</summary>
        private void Repaint()
        {
            if (last == null) return;
            PaintGraph(last, last.Network);
            PaintInfra(last);
            PaintNodes(last);
        }

        // ---- Paint -----------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            last = v;
            NetworkRoomView n = v.Network;
            selected = n.SelectedId;
            Remember(v);
            PaintGraph(v, n);
            PaintInfra(v);
            PaintNodes(v);
        }

        /// <summary>The room's memory against a new view: when each truck was first seen packing; an own pick whose anchor left, or a RELOCATE whose truck cannot move now, ends.</summary>
        private void Remember(FrontRoomView v)
        {
            NetworkRoomView n = v.Network;
            for (int s = 0; s < packSince.Length; s++)
            {
                bool packing = false;
                foreach (InfraTruckView t in n.Trucks) packing |= t.Ordinal == s && t.State == InfraState.Packing;
                if (!packing) packSince[s] = -1f;
                else if (packSince[s] < 0f) packSince[s] = v.Now;
            }
            if (selOwn != 0 && IndexOf(n.Nodes, selOwn) < 0) selOwn = 0;
            AutoPickOnce(n);
            if (pick == PickMode.Relocate && !Movable(n, moveTruck)) EndPicks();
        }

        private static bool Movable(NetworkRoomView n, int ordinal)
        {
            foreach (InfraTruckView t in n.Trucks) if (t.Ordinal == ordinal) return t.State == InfraState.Up || t.State == InfraState.Damaged;
            return false;
        }

        /// <summary>Seconds a packing truck has left as this room counts them (from when it first saw it packing: the mirror carries no clock), or -1.</summary>
        private int PackLeft(int ordinal, float now) =>
            ordinal >= 0 && ordinal < packSince.Length && packSince[ordinal] >= 0f ? Mathf.Max(0, Mathf.CeilToInt(FrontRules.TruckPackSeconds - (now - packSince[ordinal]))) : -1;

        /// <summary>Where a packing truck redeploys: known only when this room sent its RELOCATE just before it started packing.</summary>
        private bool PackDest(int ordinal, out Vector2 uv)
        {
            uv = default;
            if (ordinal < 0 || ordinal >= packSince.Length || packSince[ordinal] < 0f) return false;
            float lag = packSince[ordinal] - movedAt[ordinal];
            if (lag < -1f || lag > 10f) return false;
            uv = packTo[ordinal];
            return true;
        }

        private Vector2 P(float x, float y) => plate.P(x, y);

        private static Color StateInk(NetNodeState s) =>
            s == NetNodeState.Held ? Own : s == NetNodeState.Hopping ? HopInk : s == NetNodeState.InReach ? Amber
            : s == NetNodeState.Traced ? TraceRed : OutInk;

        private static bool AllyHeld(in NetNodeView n) => n.State == NetNodeState.Held && n.Owner != "YOU";

        /// <summary>A node held by an ally wears the cool cyan and a dashed ring: ownership is a shape and a word, never colour alone.</summary>
        private static Color NodeInk(in NetNodeView n) => AllyHeld(n) ? Ally : StateInk(n.State);

        /// <summary>The terminal's reading ink of a state word (ready phosphor, caution amber, danger trace red, info the in-flight white).</summary>
        private static Color Tone(AvState s) =>
            s == AvState.Ready ? Phos : s == AvState.Caution ? Amber : s == AvState.Danger ? TraceRed : s == AvState.Info ? HopInk : PhosDim;

        private static string StateWord(in NetNodeView n) =>
            n.State == NetNodeState.Held ? (n.Owner == "YOU" || n.Owner.Length == 0 ? "HELD" : "HELD · " + n.Owner)
            : n.State == NetNodeState.Hopping ? "HOPPING " + n.HopPct + " %" + (n.Owner == "YOU" || n.Owner.Length == 0 ? "" : " · " + n.Owner)
            : n.State == NetNodeState.InReach ? "IN REACH" : n.State == NetNodeState.Traced ? "TRACED" : "OUT OF REACH";

        private static int IndexOf(List<NetNodeView> l, int id)
        {
            for (int i = 0; i < l.Count; i++) if (l[i].Id == id) return i;
            return -1;
        }

        /// <summary>The enemy rows: they come first, our own anchors as nodes after them.</summary>
        private static int Enemies(NetworkRoomView n)
        {
            int k = 0;
            while (k < n.Nodes.Count && !n.Nodes[k].Own) k++;
            return k;
        }

        /// <summary>A link with a wide faint underlay and a bright core: phosphor on glass.</summary>
        private static void Glow(FrontVector g, Vector2 a, Vector2 b, Color k, float core)
        {
            g.Line(a, b, core + 9f, k.WithAlpha(0.06f));
            g.Line(a, b, core + 5f, k.WithAlpha(0.13f));
            g.Line(a, b, core, k);
        }

        /// <summary>Data packets running along a live link.</summary>
        private static void Packets(FrontVector g, Vector2 a, Vector2 b, float time, float phase, Color k, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float f = (i / (float)count + time * 0.3f + phase) % 1f;
                Vector2 p = Vector2.Lerp(a, b, f);
                g.Disc(p, 5f, k.WithAlpha(0.16f), 12);
                g.Disc(p, 2.4f, k, 10);
            }
        }

        /// <summary>
        /// The screen points of this paint: the nodes (ours after the enemy's), then the ghost points. Things that share a spot (closer than
        /// <see cref="StackGap"/> on screen) are fanned onto a small ring around their common point, so each keeps its own glyph, label and click; the glyph
        /// pass draws a tick back to the true point.
        /// </summary>
        private void Spread(NetworkRoomView n, int count, int ghosts)
        {
            int total = count + ghosts;
            for (int i = 0; i < total; i++)
            {
                nodeTrue[i] = nodeAt[i] = i < count ? P(n.Nodes[i].X, n.Nodes[i].Y) : P(ghostUv[i - count].x, ghostUv[i - count].y);
                nodeCluster[i] = -1;
            }
            for (int i = 0; i < total; i++)
            {
                if (nodeCluster[i] >= 0) continue;
                int m = 0;
                Vector2 mid = Vector2.zero;
                for (int j = i; j < total; j++)
                    if (nodeCluster[j] < 0 && (nodeTrue[j] - nodeTrue[i]).sqrMagnitude < StackGap * StackGap) { nodeCluster[j] = i; mid += nodeTrue[j]; m++; }
                if (m < 2) continue;
                mid /= m;
                float r = StackGap * (m == 2 ? 0.55f : 0.75f);
                for (int j = i, k = 0; j < total; j++)
                {
                    if (nodeCluster[j] != i) continue;
                    float a = Mathf.PI * (2f * k++ / m - (m == 2 ? 0f : 0.5f));
                    nodeAt[j] = mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
            }
        }

        /// <summary>The ghost points of this paint: every queued center or truck with a point, then a packing truck's destination (this room's memory) with its road.</summary>
        private int Ghosts(FrontRoomView v)
        {
            NetworkRoomView n = v.Network;
            int k = 0;
            for (int i = 0; i < n.Ghosts.Count && k < MaxGhosts; i++, k++)
            {
                ghostUv[k] = new Vector2(n.Ghosts[i].U, n.Ghosts[i].V); ghostWord[k] = n.Ghosts[i].Label; ghostKind[k] = n.Ghosts[i].Kind; ghostFrom[k] = new Vector2(-1f, -1f);
            }
            foreach (InfraTruckView t in n.Trucks)
            {
                if (k >= MaxGhosts || t.State != InfraState.Packing || !PackDest(t.Ordinal, out Vector2 to)) continue;
                ghostUv[k] = to; ghostKind[k] = (byte)AnchorKind.EwTruck; ghostFrom[k] = new Vector2(t.U, t.V);
                ghostWord[k++] = t.Name + " · REDEPLOYS ~" + FrontKit.Clock(Mathf.Max(0, PackLeft(t.Ordinal, v.Now)));
            }
            return k;
        }

        /// <summary>The drawn point of own anchor <paramref name="t"/> (its node row follows the enemy rows, one per anchor, in anchor order).</summary>
        private Vector2 AnchorPoint(NetworkRoomView n, int t, int enemies, int count) => enemies + t < count ? nodeAt[enemies + t] : P(n.Anchors[t].X, n.Anchors[t].Y);

        /// <summary>Our anchor's ink: cyan standing, amber damaged or packing up, trace red down (the word and the shape say the same).</summary>
        private static Color OwnInk(in NetAnchorView a) =>
            a.Health == AnchorHealth.Down ? TraceRed : a.Health == AnchorHealth.Damaged || a.Packing ? Amber : Phos;

        private void PaintGraph(FrontRoomView v, NetworkRoomView n)
        {
            FrontMapView mv = plate.View;
            // Centre on our own anchors, else on our airbases (where the first data center goes), else on the enemy's nodes; a pan or zoom takes over.
            Vector2 sum = Vector2.zero;
            int na = 0;
            for (int i = 0; i < n.Anchors.Count; i++) { sum += new Vector2(n.Anchors[i].X, n.Anchors[i].Y); na++; }
            if (na == 0) for (int i = 0; i < v.Airbases.Count; i++) if (v.Airbases[i].Side == AirbaseSide.Own) { sum += new Vector2(v.Airbases[i].U, v.Airbases[i].V); na++; }
            if (na == 0) for (int i = 0; i < n.Nodes.Count; i++) { sum += new Vector2(n.Nodes[i].X, n.Nodes[i].Y); na++; }
            if (na > 0) mv.AutoCenter(sum / na);
            if (locateId != 0)
            {
                int li = IndexOf(n.Nodes, locateId);
                if (li >= 0 && !mv.Visible(mv.P(n.Nodes[li].X, n.Nodes[li].Y), 24f)) mv.CenterOn(new Vector2(n.Nodes[li].X, n.Nodes[li].Y));
                locateId = 0;
            }
            plate.Begin();
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Color own = Own, amber = Amber, danger = TraceRed, key = HopInk, dim = OutInk;
            int count = Math.Min(n.Nodes.Count, MaxNodes), enemies = Math.Min(Enemies(n), count), ghosts = Ghosts(v);
            Spread(n, count, ghosts);
            if (pick != PickMode.None) labels.Take(new Rect(plate.X + FootX, plate.Y + plate.H - 24f, plate.W - FootX - FootRight, 24f)); // the foot words

            // The glass: a teal-black tint over the satellite picture and a vignette. Every layer is under 0.2 alpha, so it reads as a tint.
            float px = mv.X, py = mv.Y, pw = mv.W, ph = mv.H;
            g.Rect(px, py, pw, ph, RoomInk.Rgb(0x02101A, 0.19f));
            g.Rect(px, py, pw, ph, RoomInk.Rgb(0x0B3A4A, 0.11f));
            Color v0 = RoomInk.Crt.WithAlpha(0.19f), v1 = RoomInk.Crt.WithAlpha(0f);
            const float e = 110f;
            g.Grad(px, py, e, ph, v0, v1, v1, v0);
            g.Grad(px + pw - e, py, e, ph, v1, v0, v0, v1);
            g.Grad(px, py, pw, e * 0.55f, v0, v0, v1, v1);
            g.Grad(px, py + ph - e * 0.55f, pw, e * 0.55f, v1, v1, v0, v0);

            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, P);

            // The armed pick's rings: where a click is legal (held land airbases, 8 km; standing data centers, and for DEPLOY TRUCK the queued ones, 20 km).
            if (pick != PickMode.None)
            {
                float r = Mathf.Max(10f, RingMetres(pick) / FrontController.Frame().W * mv.Scale);
                foreach (Vector2 c in Rings(v, pick))
                {
                    Vector2 p = P(c.x, c.y);
                    g.Disc(p, r, own.WithAlpha(0.1f), 48);
                    g.Ring(p, r, 1.8f, own.WithAlpha(0.95f), 64);
                    g.Ring(p, r + 4f, 1f, own.WithAlpha(0.4f), 64, 1f, 1f);
                }
            }

            // Edges between enemy nodes: nearby ones first, then the reach of every node you can hop to (from the source the host would hop from), then the intrusions' own paths on top.
            for (int i = 0; i < enemies; i++)
                for (int j = i + 1; j < enemies; j++)
                {
                    NetNodeView a = n.Nodes[i], b = n.Nodes[j];
                    float km = new Vector2((a.X - b.X) * n.MapKmW, (a.Y - b.Y) * n.MapKmH).magnitude;
                    if (km > LinkKm) continue;
                    bool both = a.State == NetNodeState.Held && b.State == NetNodeState.Held, burned = a.State == NetNodeState.Traced || b.State == NetNodeState.Traced;
                    if (both) Glow(g, nodeAt[i], nodeAt[j], own, 2f);
                    else if (burned) g.Dashed(nodeAt[i], nodeAt[j], 1.1f, danger.WithAlpha(0.55f), 4f, 4f);
                    else g.Line(nodeAt[i], nodeAt[j], 1f, dim.WithAlpha(0.42f));
                }
            for (int i = 0; i < enemies; i++)
            {
                NetNodeView nd = n.Nodes[i];
                if (nd.State != NetNodeState.InReach) continue;
                Vector2 to = nodeAt[i], from;
                int s = nd.FromNode >= 0 ? IndexOf(n.Nodes, nd.FromNode) : -1;
                if (s >= 0) from = s < count ? nodeAt[s] : P(n.Nodes[s].X, n.Nodes[s].Y);
                else if (nd.FromAnchor >= 0 && nd.FromAnchor < n.Anchors.Count) from = AnchorPoint(n, nd.FromAnchor, enemies, count);
                else continue;
                bool picked = nd.Id == n.SelectedId;
                g.Dashed(from, to, picked ? 2f : 1.1f, picked ? AvInk.Select : amber.WithAlpha(0.6f), 4f, 4f);
            }
            int hl = 0;
            for (int i = 0; i < enemies; i++)
            {
                NetNodeView nd = n.Nodes[i];
                if (nd.State != NetNodeState.Held && nd.State != NetNodeState.Hopping) continue;
                Vector2 to = nodeAt[i], from;
                int s = nd.FromNode >= 0 ? IndexOf(n.Nodes, nd.FromNode) : -1;
                if (s >= 0) from = s < count ? nodeAt[s] : P(n.Nodes[s].X, n.Nodes[s].Y);
                else if (nd.FromAnchor >= 0 && nd.FromAnchor < n.Anchors.Count) from = AnchorPoint(n, nd.FromAnchor, enemies, count);
                else continue;
                if (nd.State == NetNodeState.Held)
                {
                    Color path = NodeInk(nd);
                    Glow(g, from, to, path, 2.2f);
                    Packets(g, from, to, v.Now, i * 0.37f, path, 2);
                    continue;
                }
                float f = Mathf.Clamp01(nd.HopPct / 100f);
                Vector2 lead = Vector2.Lerp(from, to, f), u = (to - from).normalized, nrm = new Vector2(-u.y, u.x);
                g.Dashed(from, to, 1.6f, key.WithAlpha(0.5f), 7f, 5f);
                g.Line(from, lead, 9f, key.WithAlpha(0.07f));
                g.Line(from, lead, 5f, key.WithAlpha(0.14f));
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

            // Our trucks' reach rings (a packing or down truck has none: offline); an armed pick's rings stand alone.
            for (int t = 0; pick == PickMode.None && t < n.Anchors.Count; t++)
            {
                NetAnchorView a = n.Anchors[t];
                if (a.Kind != AnchorKind.EwTruck || a.Reach <= 0f) continue;
                Vector2 p = AnchorPoint(n, t, enemies, count);
                Color k = OwnInk(a);
                g.Disc(p, a.Reach * mv.Scale, k.WithAlpha(0.03f), 64);
                g.Ring(p, a.Reach * mv.Scale, 1.2f, k.WithAlpha(0.6f), 72, 1f, 1f);
            }

            // Queued builds and a packing truck's destination: dashed hexes (a packing truck's road dashed to its ghost).
            for (int i = 0; i < ghosts; i++)
            {
                Vector2 p = nodeAt[count + i];
                if (ghostFrom[i].x >= 0f) g.Dashed(P(ghostFrom[i].x, ghostFrom[i].y), p, 1.4f, amber.WithAlpha(0.8f), 6f, 4f);
                g.Disc(p, 13f, Glass.WithAlpha(0.6f), 6);
                DashedHex(g, p, 13f, own.WithAlpha(0.85f));
                if (ghostKind[i] == (byte)AnchorKind.EwTruck) Truck(g, p, own.WithAlpha(0.55f));
                else Glyph(g, NodeKind.DataCenter, p, 8f, own.WithAlpha(0.55f), 1.2f);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }

            // Enemy nodes.
            for (int i = 0; i < enemies; i++)
            {
                NetNodeView nd = n.Nodes[i];
                Vector2 p = nodeAt[i];
                Color k = NodeInk(nd);
                if ((p - nodeTrue[i]).sqrMagnitude > 4f) { g.Line(nodeTrue[i], p, 1f, k.WithAlpha(0.5f)); g.Disc(nodeTrue[i], 2f, k.WithAlpha(0.8f), 8); } // fanned out of a shared spot: a tick to where it really stands
                if (nd.State == NetNodeState.Held) { g.Disc(p, 19f, k.WithAlpha(0.07f), 24); g.Disc(p, 15f, k.WithAlpha(0.18f), 24); }
                g.Disc(p, 13f, Glass, 6); // every node sits on a hex plate (the ICE look)
                Hex(g, p, 13f, k.WithAlpha(0.45f), 1f);
                Glyph(g, nd.Kind, p, 9f, k, nd.State == NetNodeState.Held ? 2f : 1.5f);
                if (AllyHeld(nd)) g.Ring(p, 17f, 1.2f, k.WithAlpha(0.9f), 28, 1f, 1f);
                if (nd.State == NetNodeState.Traced)
                {
                    FrontKit.Cross(g, p, 10f, danger);
                    g.Ring(p, 17f, 1.2f, danger.WithAlpha(0.8f), 28, 1f, 1f);
                }
                if (selOwn == 0 && nd.Id == n.SelectedId) FrontKit.Brackets(g, p, 18f, AvInk.Select); // one pick on the map: one of ours wins
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }
            // Our own nodes: a bright hex with the truck or data center inside; packing dashed, down crossed, a trace lock ringed red, a breach flagged.
            for (int i = enemies; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                NetAnchorView a = n.Anchors[nd.AnchorAt];
                Vector2 p = nodeAt[i];
                Color k = OwnInk(a);
                if ((p - nodeTrue[i]).sqrMagnitude > 4f) { g.Line(nodeTrue[i], p, 1f, k.WithAlpha(0.5f)); g.Disc(nodeTrue[i], 2f, k.WithAlpha(0.8f), 8); }
                g.Disc(p, 16f, k.WithAlpha(0.1f), 24);
                g.Disc(p, 13f, Glass, 6);
                if (a.Packing) DashedHex(g, p, 13f, k); else Hex(g, p, 13f, k, 1.8f);
                if (a.Kind == AnchorKind.EwTruck) Truck(g, p, k); else Glyph(g, NodeKind.DataCenter, p, 8f, k, 1.4f);
                if (a.Health == AnchorHealth.Down) FrontKit.Cross(g, p, 9f, danger);
                if (a.LockLeft > 0) g.Ring(p, 17f, 1.4f, danger.WithAlpha(0.9f), 24, 1f, 1f);
                if (nd.Breached) Flag(g, p + new Vector2(11f, -8f), 16f, danger); // top right of the hex
                if (nd.Id == selOwn || pick == PickMode.Relocate && a.Kind == AnchorKind.EwTruck && a.Slot == moveTruck) FrontKit.Brackets(g, p, 18f, AvInk.Select);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }

            // Words: ours name themselves first (the session starts from them), then the enemy's where there is room (the node list names every one).
            bool hits = pick == PickMode.None; // an armed pick owns the click: no node takes one
            for (int pass = 0; pass < 2; pass++)
                for (int i = pass == 0 ? enemies : 0; i < (pass == 0 ? count : enemies); i++)
                {
                    NetNodeView nd = n.Nodes[i];
                    Vector2 p = nodeAt[i];
                    TMP_Text t = nodeLabels[i];
                    Image hit = nodeHits[i];
                    if (!plate.Shown(p, 12f)) { t.gameObject.SetActive(false); hit.gameObject.SetActive(false); continue; }
                    t.gameObject.SetActive(true);
                    bool optional;
                    if (nd.Own)
                    {
                        NetAnchorView a = n.Anchors[nd.AnchorAt];
                        Color k = OwnInk(a);
                        int left = a.Packing ? PackLeft(a.Slot, v.Now) : -1;
                        string state = a.Health == AnchorHealth.Down ? "DOWN" : a.Packing ? "PACKING UP" + (left >= 0 ? " · ~" + FrontKit.Clock(left) : "")
                            : a.LockLeft > 0 ? "TRACED · LOCKED " + FrontKit.Clock(a.LockLeft)
                            : (a.Health == AnchorHealth.Damaged ? "DAMAGED" : "LIVE") + (a.Reach > 0f ? " · REACH " + Mathf.RoundToInt(a.Reach * n.MapKmW) + " KM" : "");
                        if (nd.Breached) state = "BREACHED · " + state;
                        t.text = C2Kit.Tint("<b>" + a.Label + "</b> · " + (a.Kind == AnchorKind.EwTruck ? "EW TRUCK" : "DATA CENTER"), k) + "\n"
                            + C2Kit.Tint(state, nd.Breached || a.LockLeft > 0 || a.Health == AnchorHealth.Down ? danger : PhosDim);
                        optional = false;
                    }
                    else
                    {
                        Color k = NodeInk(nd);
                        bool live = nd.State == NetNodeState.Held || nd.State == NetNodeState.Hopping;
                        t.text = C2Kit.Tint("<b>" + nd.Label + "</b> · " + KindName[(int)nd.Kind], nd.State == NetNodeState.OutOfReach ? dim : k) + "\n" + C2Kit.Tint(StateWord(nd), live ? k : dim);
                        optional = !(live || nd.Id == n.SelectedId); // the picked node and the nodes in play are always named; the others take a clear spot or stay unlabelled
                    }
                    t.color = Bright;
                    Rect spot = labels.Place(t, p, 16f, false, optional);
                    if (spot.width <= 0f) t.gameObject.SetActive(false); else FrontMap.Plate(g, spot);
                    if (hit.gameObject.activeSelf != hits) hit.gameObject.SetActive(hits);
                    hitIds[i] = nd.Id;
                    AvLay.Place(hit.rectTransform, p.x - 15f, p.y - 15f, 30f, 30f);
                }
            for (int i = count; i < MaxNodes; i++) { nodeLabels[i].gameObject.SetActive(false); nodeHits[i].gameObject.SetActive(false); }
            // Ghost words: "DC-2 · OPAL · 0:48" where there is room.
            int gw = 0;
            for (int i = 0; i < ghosts && gw < ghostLabels.Length; i++)
            {
                Vector2 p = nodeAt[count + i];
                if (!plate.Shown(p, 16f)) continue;
                TMP_Text t = ghostLabels[gw];
                t.gameObject.SetActive(true);
                FrontKit.Set(t, ghostWord[i], ghostFrom[i].x >= 0f ? amber : own);
                Rect spot = labels.Place(t, p, 16f, true, true);
                if (spot.width <= 0f) { t.gameObject.SetActive(false); continue; }
                FrontMap.Plate(g, spot);
                gw++;
            }
            for (int i = gw; i < ghostLabels.Length; i++) ghostLabels[i].gameObject.SetActive(false);
            airbases.Names(g, v.Airbases, P, mv, (t, a) => labels.Place(t, a, 9f, true, true));

            // The foot: what the armed pick wants, or why the last click missed.
            footLabel.gameObject.SetActive(pick != PickMode.None);
            if (pick != PickMode.None) FrontKit.Set(footLabel, FrontKit.Fit(footLabel, PickWords(true), 10.5f), pickMiss.Length > 0 ? danger : amber);

            int inReach = 0, held = 0;
            for (int i = 0; i < enemies; i++) { if (n.Nodes[i].State == NetNodeState.InReach) inReach++; if (n.Nodes[i].State == NetNodeState.Held) held++; }
            for (int i = 0; i < n.Anchors.Count; i++) plate.Dot(n.Anchors[i].X, n.Anchors[i].Y, OwnInk(n.Anchors[i]));
            for (int i = 0; i < enemies; i++) plate.Dot(n.Nodes[i].X, n.Nodes[i].Y, NodeInk(n.Nodes[i]));
            plate.End();

            graph.SetMeta(n.Active ? (count - enemies) + " MINE · " + enemies + " ENEMY · " + inReach + " IN REACH · " + held + " HELD" : n.Meta, held > 0 ? Phos : PhosDim);
        }

        // ---- Map picks -----------------------------------------------------------------------------------------------

        /// <summary>BUILD DATA CENTER, DEPLOY TRUCK or a truck's RELOCATE: arms that pick (the same press again disarms it; one pick at a time).</summary>
        private void Arm(PickMode mode, int truck = -1)
        {
            actions.Touch();
            bool same = pick == mode && (mode != PickMode.Relocate || moveTruck == truck);
            pick = same ? PickMode.None : mode;
            moveTruck = truck;
            pickMiss = "";
            Repaint();
        }

        /// <summary>The window closed, switched rooms or left INFRASTRUCTURE: the armed pick ends (nothing was sent).</summary>
        public void EndPicks() { pick = PickMode.None; pickMiss = ""; }

        public bool CancelMode()
        {
            if (pick == PickMode.None) return false;
            actions.Touch();
            EndPicks();
            return true;
        }

        private static float RingMetres(PickMode mode) => mode == PickMode.Center ? FrontRules.PadPickMetres : FrontRules.TruckReachMetres;

        /// <summary>The centres a pick's rings stand on: held land airbases (DATA CENTER), else standing data centers, with the queued ones that have a point for DEPLOY TRUCK.</summary>
        private List<Vector2> Rings(FrontRoomView v, PickMode mode)
        {
            rings.Clear();
            if (mode == PickMode.Center)
            {
                foreach (MapAirbaseView a in v.Airbases) if (a.Side == AirbaseSide.Own) rings.Add(new Vector2(a.U, a.V));
                return rings;
            }
            foreach (InfraCenterView c in v.Network.Centers)
                if (c.U >= 0f && (c.State == InfraState.Up || c.State == InfraState.Damaged || mode == PickMode.Truck && c.State == InfraState.Building)) rings.Add(new Vector2(c.U, c.V));
            return rings;
        }

        /// <summary>What the armed pick wants (or why the last click missed), short for the map's foot or long for the INFRASTRUCTURE hint.</summary>
        private string PickWords(bool foot)
        {
            if (pickMiss.Length > 0) return pickMiss + " · CLICK INSIDE A RING" + (foot ? "" : " ON THE MAP") + " · ESC CANCELS";
            string what = pick == PickMode.Center ? "BUILD DATA CENTER" : pick == PickMode.Truck ? "DEPLOY TRUCK" : "RELOCATE EW-" + (moveTruck + 1);
            string where = pick == PickMode.Center ? "8 KM OF A HELD AIRBASE" : pick == PickMode.Truck ? "20 KM OF A DATA CENTER" : "20 KM OF A LIVE DATA CENTER";
            return what + " · CLICK WITHIN " + where + (foot ? "" : " (RINGED ON THE MAP)") + " · ESC CANCELS";
        }

        /// <summary>
        /// The armed pick's click: inside a ring it queues the build at the point (or sends the truck's RELOCATE there) and disarms; anywhere else it says why and
        /// stays armed. The host judges the place again.
        /// </summary>
        private void PickAt(Vector2 uv)
        {
            actions.Touch();
            MapFrame f = FrontController.Frame();
            float reach = RingMetres(pick);
            bool inside = false;
            foreach (Vector2 c in Rings(last, pick))
            {
                float dx = (uv.x - c.x) * f.W, dz = (uv.y - c.y) * f.H;
                if (dx * dx + dz * dz <= reach * reach) { inside = true; break; }
            }
            if (!inside) { pickMiss = FrontWords.Refusal(pick == PickMode.Center ? FrontOutcome.NotHeld : FrontOutcome.TooFar, Front.Cyber); return; }
            PickMode was = pick;
            EndPicks();
            if (was == PickMode.Center) actions.Queue(Front.Cyber, ProgrammeId.DataCenter, 0, true, uv.x, uv.y);
            else if (was == PickMode.Truck) actions.Queue(Front.Cyber, ProgrammeId.EwTruck, 0, true, uv.x, uv.y);
            else
            {
                if (moveTruck >= 0 && moveTruck < packTo.Length) { packTo[moveTruck] = uv; movedAt[moveTruck] = last.Now; }
                actions.MoveTruck(moveTruck, f.X(uv.x), f.Z(uv.y));
            }
        }

        private bool Local(Vector2 mouse, out Vector2 p)
        {
            p = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(graph.Rect, mouse, null, out Vector2 lp)) return false;
            p = new Vector2(lp.x - graph.Rect.rect.xMin, graph.Rect.rect.yMax - lp.y);
            return true;
        }

        /// <summary>On the map plate. A word or glyph under the click does not matter: a pick never misses in silence.</summary>
        private bool OnMap(Vector2 p)
        {
            FrontMapView mv = plate.View;
            return p.x >= mv.X && p.x <= mv.X + mv.W && p.y >= mv.Y && p.y <= mv.Y + mv.H;
        }

        /// <summary>The window closed: the next open (a new mission, maybe) lets NODES auto-pick again.</summary>
        internal void ForgetPick() => handPicked = false;

        /// <summary>
        /// One frame of input. A pan, zoom or inset move repaints only the map and a wheel over the node list only the list; both return false (the column does not
        /// depend on the view). True only for a pick's click, which changes the column too. Node clicks arrive through their own hits.
        /// </summary>
        public bool Tick(Vector2 mouse)
        {
            if (last == null) { pressed = false; return false; }
            FrontMapView mv = plate.View;
            Vector2 center = mv.Center;   // the view as the press found it: a press on the inset overview moves it, and that is no click on the map
            int zoomStep = mv.Zoom;
            bool changed = plate.Tick(mouse);
            float wheel = FrontPointer.Wheel;
            if (Mathf.Abs(wheel) > 0.01f && nodesBox.Rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(nodesBox.Rect, mouse, null))
            {
                int to = Mathf.Clamp(scroll + (wheel > 0f ? -1 : 1), 0, Math.Max(0, listCount - 1));
                if (to != scroll && (wheel > 0f || listMoreBelow)) { scroll = to; PaintNodes(last); }
            }
            if (FrontPointer.Down(0))
            {
                pressed = pick != PickMode.None && Local(mouse, out pressAt) && OnMap(pressAt);
                pressCenter = center; pressZoom = zoomStep;
            }
            if (pressed && (FrontPointer.Held(1) || FrontPointer.Held(2))) pressed = false;   // a pan gesture is no click
            if (!pressed || !FrontPointer.Up(0)) return MapOnly(changed);
            pressed = false;
            // Only a click that stayed put on an unmoved, unzoomed map picks anything.
            if (pick == PickMode.None || !Local(mouse, out Vector2 p) || !OnMap(p) || (p - pressAt).sqrMagnitude > DragSlop * DragSlop || mv.Zoom != pressZoom ||
                (mv.Center - pressCenter).sqrMagnitude > 1e-8f) return MapOnly(changed);
            PickAt(mv.UV(p));
            return true;
        }

        /// <summary>The view moved: the map repaints now, the window repaints nothing else.</summary>
        private bool MapOnly(bool changed)
        {
            if (changed) PaintGraph(last, last.Network);
            return false;
        }

        /// <summary>A hex outline in dashes: a ghost, or one of our trucks packing up.</summary>
        private static void DashedHex(FrontVector g, Vector2 p, float r, Color k)
        {
            for (int i = 0; i < 6; i++)
            {
                float a0 = Mathf.PI / 3f * i, a1 = Mathf.PI / 3f * (i + 1);
                g.Dashed(p + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, 1.6f, k, 3f, 2f);
            }
        }

        /// <summary>A breach flag: a pole of height <paramref name="h"/> standing on <paramref name="foot"/> and a pennant at its top (the word BREACHED rides the label).</summary>
        private static void Flag(FrontVector g, Vector2 foot, float h, Color k)
        {
            Vector2 top = foot + new Vector2(0f, -h);
            g.Line(foot, top, 1.4f, k);
            g.Tri(top, top + new Vector2(h * 0.7f, h * 0.25f), top + new Vector2(0f, h * 0.5f), k);
        }

        /// <summary>Truncates to the width only when the text could overflow it (7.5 px a mono glyph is the ceiling at 10.5 px), so a repaint measures few strings.</summary>
        private static string Fit(TMP_Text t, string s, float width) => s.Length * 7.5f <= width ? s : C2Kit.FitTo(t, s, width);


        private static void Hex(FrontVector g, Vector2 p, float r, Color k, float w)
        {
            for (int i = 0; i < 6; i++) { float a = Mathf.PI / 3f * i; hexPts[i] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
            g.Polyline(hexPts, w, k, true);
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
                    Hex(g, p, r, k, w);
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
    }
}
