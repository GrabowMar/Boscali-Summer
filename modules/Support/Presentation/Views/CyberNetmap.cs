using System;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Presentation.Board;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>
    /// The CYBER netmap, drawn in the terminal's own language on a <see cref="BoardSurface"/>: a dotted
    /// grid and the map edge, the frontline as a thin trace, nodes as hex glyphs by kind with a
    /// short access timer ring, links as orthogonal circuit traces from Cyber Command, in-reach
    /// targets with dashed pulsing outlines, the selected node's reach bubble, a packet stream on the
    /// breach link and incidents walking toward their targets. Pooled at build; repositioned only when
    /// the frame or the network changes; packets, pulses and incidents move every frame.
    /// </summary>
    internal sealed class CyberNetmap
    {
        private const int Slots = CyberNetwork.SlotCount;
        private const int Incidents = CyberNetwork.IncidentSlots;
        private const int Strokes = 280;
        private const int GridLines = 32;
        private const int Packets = 6;
        private const float Hex = 38f;
        private const float TagHeight = 31f;
        private const float MinimumSpan = 24000f;
        private static readonly float[] GridSteps = { 2000f, 5000f, 10000f, 20000f, 50000f };

        private sealed class Node
        {
            public RoomControl Control;
            public Image Fill, Line, Glyph, Reach, Select;
            public Image[] Ring;
            public Image LinkA, LinkB;
            public Image Tag;
            public Image TagRail;
            public TMP_Text Name;
            public string Shown;
            public float Width;
        }

        private sealed class Threat
        {
            public Image Mark, Line;
            public RingLine Sector;
        }

        public BoardSurface Board { get; private set; }
        public bool TerrainAvailable => terrain != null && terrain.Available;
        public Func<NodeKind, float, float, string> ResolveName { private get; set; }

        private RectTransform layer;
        private BoardTerrain terrain;
        private BoardSectorOverlay sectors;
        internal Sprite TerrainSource => terrain.Source;
        internal Vector2 TerrainMetres => terrain.Metres;
        private readonly Image[] gridX = new Image[GridLines];
        private readonly Image[] gridZ = new Image[GridLines];
        private readonly Image[] edge = new Image[4];
        private readonly Image[] front = new Image[Strokes];
        private readonly Node[] nodes = new Node[Slots];
        private readonly Threat[] threats = new Threat[Incidents];
        private readonly Image[] packets = new Image[Packets];
        private RingLine bubble;
        private Image breachA, breachB;
        private Image routeA, routeB, routeHaloA, routeHaloB;
        private readonly float[] fitX = new float[Slots];
        private readonly float[] fitZ = new float[Slots];
        private readonly Vector2[] anchors = new Vector2[LabelPlacer.Maximum];
        private readonly Vector2[] sizes = new Vector2[LabelPlacer.Maximum];
        private readonly int[] priorities = new int[LabelPlacer.Maximum];
        private readonly float[] radii = new float[LabelPlacer.Maximum];
        private readonly int[] labelSlot = new int[LabelPlacer.Maximum];
        private readonly PlacedLabel[] placed = new PlacedLabel[LabelPlacer.Maximum];
        private readonly bool[] inReach = new bool[Slots];
        private int frontRevision = -1;
        private int shownRevision = -1;
        private Vector2 packetFrom, packetTo;
        private bool packetsOn;
        private bool motionAllowed = true;
        private readonly Rect[] mapObstacles = new Rect[1];
        private Action<int> select;
        private float nextAnimation;

        internal Rect[] PlacedRects { get; } = new Rect[LabelPlacer.Maximum];
        internal int PlacedCount { get; private set; }

        public void Build(RectTransform pane, Rect view, Action<int> onSelect)
        {
            select = onSelect;
            OpsSprites.Ensure();
            Board = new BoardSurface(pane, view, view, true, true);
            var layerObject = new GameObject("Wire", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            layer = (RectTransform)layerObject.transform;
            layer.SetParent(Board.InputLayer, false);
            Chrome.Place(layer, new Rect(-view.x, -view.y, view.x + view.width, view.height - view.y));
            terrain = new BoardTerrain(layer, Board);
            // Terrain is the navigation substrate; keep the room tint light enough that
            // roads, coastlines and objective texture remain legible beneath the network.
            terrain.SetTint(Color.white);
            Chrome.Panel(layer, view, CyberStyle.Surface.WithAlpha(0.26f));
            sectors = new BoardSectorOverlay(layer, Board, CyberStyle.Lattice, CyberStyle.Title, CyberStyle.Accent);
            for (int i = 0; i < GridLines; i++)
            {
                gridX[i] = Stroke(layer, CyberStyle.Lattice.WithAlpha(0.35f), OpsSprites.Dash);
                gridZ[i] = Stroke(layer, CyberStyle.Lattice.WithAlpha(0.35f), OpsSprites.Dash);
            }
            for (int i = 0; i < edge.Length; i++) edge[i] = Stroke(layer, CyberStyle.Lattice, null);
            for (int i = 0; i < Strokes; i++) front[i] = Stroke(layer, CyberStyle.Dim.WithAlpha(0.6f), null);

            bubble = new RingLine(layer, 64, CyberStyle.Accent, true);
            routeHaloA = Stroke(layer, CyberStyle.Surface.WithAlpha(0.92f), null);
            routeHaloB = Stroke(layer, CyberStyle.Surface.WithAlpha(0.92f), null);
            routeA = Stroke(layer, CyberStyle.Title, null);
            routeB = Stroke(layer, CyberStyle.Title, null);
            for (int i = 0; i < Slots; i++) nodes[i] = BuildLinks();
            breachA = Stroke(layer, CyberStyle.Accent, OpsSprites.Dash);
            breachB = Stroke(layer, CyberStyle.Accent, OpsSprites.Dash);
            for (int i = 0; i < Packets; i++)
            {
                packets[i] = Chrome.Panel(layer, new Rect(0f, 0f, 7f, 7f), CyberStyle.Accent, OpsSprites.Dot);
                packets[i].type = Image.Type.Simple;
                packets[i].enabled = false;
            }
            for (int i = 0; i < Slots; i++) BuildNode(nodes[i], i);
            for (int i = 0; i < Incidents; i++)
            {
                var threat = new Threat
                {
                    Line = Lines.Make(layer, AvTheme.RailDanger.WithAlpha(0.8f), null, "IncidentPath"),
                    Mark = Chrome.Panel(layer, new Rect(0f, 0f, 20f, 20f), AvTheme.RailDanger)
                };
                threat.Mark.sprite = OpsSprites.Glyph(OpsSprites.G.Alert);
                threat.Sector = new RingLine(layer, 48, AvTheme.RailDanger, true);
                threat.Mark.gameObject.SetActive(false);
                threat.Line.enabled = false;
                threats[i] = threat;
            }
            for (int i = 0; i < Slots; i++) BuildTag(nodes[i]);
            BuildFrame(pane, view);
            Rect legend = new Rect(view.x + 12, view.y - 12, Mathf.Min(310, view.width - 28), 28);
            Chrome.Panel(pane, legend, CyberStyle.Surface.WithAlpha(.92f));
            Chrome.Rule(pane, new Rect(legend.x, legend.y, legend.width, 1), CyberStyle.Title.WithAlpha(.6f));
            CyberStyle.Type(CyberStyle.Line(pane, new Rect(legend.x + 8, legend.y - 3, legend.width - 16, 23), 12, CyberStyle.Ink),
                "NATIVE SITES / 10 KM ACCESS CELLS");
            mapObstacles[0] = new Rect(legend.x - 4, legend.y + 4, legend.width + 8, legend.height + 8);
            Board.SetObstacles(mapObstacles, mapObstacles.Length);
        }

        /// <summary>Portal frame over the map: corner brackets and a tick ruler along the top edge. Decoration only.</summary>
        private static void BuildFrame(RectTransform pane, Rect view)
        {
            CyberStyle.Corners(pane, view, CyberStyle.Title.WithAlpha(0.7f));
            int ticks = Mathf.Min(48, Mathf.FloorToInt(view.width / 40f));
            for (int i = 1; i < ticks; i++)
            {
                bool major = i % 5 == 0;
                Chrome.Rule(pane, new Rect(view.x + i * 40f, view.y, 1f, major ? 7f : 4f), CyberStyle.Title.WithAlpha(major ? 0.55f : 0.3f));
            }
        }

        private static Image Stroke(RectTransform parent, Color color, Sprite sprite)
        {
            Image line = Lines.Make(parent, color, sprite);
            line.enabled = false;
            return line;
        }

        private Node BuildLinks() => new Node
        {
            LinkA = Stroke(layer, CyberStyle.Ink.WithAlpha(0.45f), null),
            LinkB = Stroke(layer, CyberStyle.Ink.WithAlpha(0.45f), null)
        };

        private void BuildNode(Node node, int slot)
        {
            node.Reach = Chrome.Panel(layer, new Rect(0f, 0f, 10f, 10f), CyberStyle.Title, OpsSprites.DottedRing);
            node.Reach.type = Image.Type.Simple;
            node.Control = RoomControl.Create(layer, new Rect(0f, 0f, Hex + 12f, Hex + 12f), () => select?.Invoke(slot), "Node");
            RectTransform host = node.Control.Rect;
            node.Select = Chrome.Panel(host, new Rect(-6f, 6f, Hex + 24f, Hex + 24f), CyberStyle.Accent, OpsSprites.HexLine);
            node.Select.type = Image.Type.Simple;
            node.Ring = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                Image ring = Chrome.Panel(host, new Rect(0f, -Hex - 15f, Hex + 12f, 4f), CyberStyle.Lattice);
                ring.type = Image.Type.Filled;
                ring.fillMethod = Image.FillMethod.Horizontal;
                ring.fillOrigin = (int)Image.OriginHorizontal.Left;
                ring.fillAmount = 1f;
                node.Ring[i] = ring;
            }
            node.Fill = Chrome.Panel(host, new Rect(6f, -6f, Hex, Hex), CyberStyle.Pane, OpsSprites.HexFill);
            node.Fill.type = Image.Type.Simple;
            node.Line = Chrome.Panel(host, new Rect(6f, -6f, Hex, Hex), CyberStyle.Ink, OpsSprites.HexLine);
            node.Line.type = Image.Type.Simple;
            node.Glyph = Chrome.Panel(host, new Rect(10f, -10f, Hex - 8f, Hex - 8f), CyberStyle.Ink);
            node.Control.Changed = c => node.Fill.color = c.Hovered || c.Focused ? CyberStyle.Bar : CyberStyle.Pane;
            node.Control.gameObject.SetActive(false);
            node.Reach.gameObject.SetActive(false);
        }

        private void BuildTag(Node node)
        {
            node.Tag = Chrome.Panel(layer, new Rect(0f, 0f, 80f, TagHeight), CyberStyle.Surface.WithAlpha(0.94f));
            node.Tag.raycastTarget = false;
            node.TagRail = Chrome.Rule(node.Tag.rectTransform, new Rect(0, 0, 2, TagHeight), CyberStyle.Title.WithAlpha(.7f));
            node.Name = CyberStyle.Line(node.Tag.rectTransform, new Rect(7f, 0f, 72f, TagHeight), CyberStyle.Micro, CyberStyle.Ink);
            node.Name.raycastTarget = false;
            node.Tag.gameObject.SetActive(false);
        }

        // ---- Layout -----------------------------------------------------------------------------

        public bool FrameChanged => shownRevision != Board.Revision;

        public void FrontChanged() => frontRevision = -1;

        /// <summary>Reposition everything from the network. Call on a text tick or a frame change.</summary>
        public void Layout(CyberNetwork network, double now, int selected, int breachTarget, bool reducedMotion = false)
        {
            motionAllowed = !reducedMotion && !AvFxDriver.ReducedMotion;
            if (!motionAllowed) for (int i = 0; i < Packets; i++) packets[i].enabled = false;
            int points = 0;
            for (int i = 0; i < Slots && network != null; i++)
            {
                if (!network.Exists(i)) continue;
                CyberNode n = network.Node(i);
                fitX[points] = n.X;
                fitZ[points++] = n.Z;
            }
            // A network overview must include every selectable node, including distant command bases.
            Board.Fit(fitX, fitZ, points, MinimumSpan, 0f, 62f);
            terrain.Refresh();
            sectors.Begin();
            for (int i = 0; i < Slots && network != null; i++)
            {
                if (!network.Exists(i)) continue;
                CyberNode n = network.Node(i);
                sectors.Add(n.X, n.Z, network.ControlsSector(n.X, n.Z, now), i == selected);
            }
            sectors.Paint();
            PaintGrid();
            if (frontRevision != Board.Revision) PaintFront();

            int command = network != null ? network.CommandSlot : -1;
            Vector2 c2 = command >= 0 ? Board.Project(network.Node(command).X, network.Node(command).Z) : default;
            Board.ClearMarkers();
            for (int i = 0; i < Slots; i++)
            {
                Node node = nodes[i];
                bool show = network != null && network.Exists(i);
                if (node.Control.gameObject.activeSelf != show) node.Control.gameObject.SetActive(show);
                if (!show)
                {
                    node.LinkA.enabled = node.LinkB.enabled = false;
                    if (node.Reach.gameObject.activeSelf) node.Reach.gameObject.SetActive(false);
                    inReach[i] = false;
                    continue;
                }
                CyberNode n = network.Node(i);
                Vector2 p = Board.Project(n.X, n.Z);
                Lines.Centre(node.Control.Rect, p.x, p.y, Hex + 12f);
                Board.AddMarker(i, p, Hex);
                bool home = n.Static;
                bool mine = home || n.Hacked;
                bool bad = n.Compromised || (home && n.Down);
                Color ink = bad ? AvTheme.RailDanger : n.Isolated ? CyberStyle.Dim : home ? CyberStyle.Ink : n.Hacked ? CyberStyle.Accent : CyberStyle.Title;
                node.Line.color = ink;
                node.Glyph.color = ink;
                node.Glyph.sprite = OpsSprites.Glyph(Glyph(n.Kind));
                node.Fill.color = mine ? CyberStyle.Pane : CyberStyle.Surface;
                float lease = network.AccessSlot == i ? network.AccessRemaining(now) : 0f;
                for (int s = 0; s < 4; s++)
                {
                    node.Ring[s].enabled = !home && s == 0 && lease > 0f;
                    node.Ring[s].color = CyberStyle.Accent;
                    if (s == 0) node.Ring[s].fillAmount = Mathf.Clamp01(lease / CyberLocations.AccessSeconds);
                }
                node.Select.enabled = i == selected;
                node.Select.color = network.ControlsSector(n.X, n.Z, now) ? CyberStyle.Accent : CyberStyle.Title;
                // Reach is geography, not affordability or whether another session occupies the console.
                inReach[i] = !home && !n.Hacked && network.ReachCovers(n.X, n.Z);
                bool reach = inReach[i];
                if (node.Reach.gameObject.activeSelf != reach) node.Reach.gameObject.SetActive(reach);
                if (reach) Lines.Centre(node.Reach.rectTransform, p.x, p.y, Hex + 26f);
                // Circuit traces: every live link runs from Cyber Command, horizontal first, then vertical.
                bool linked = mine && i != command && command >= 0 && network.Online(i) && network.Online(command);
                if (linked)
                {
                    Lines.Set(node.LinkA, c2.x, c2.y, p.x, c2.y, 1.5f);
                    Lines.Set(node.LinkB, p.x, c2.y, p.x, p.y, 1.5f);
                    Color trace = bad ? AvTheme.RailDanger.WithAlpha(0.6f) : home ? CyberStyle.Ink.WithAlpha(0.45f) : CyberStyle.Accent.WithAlpha(0.55f);
                    node.LinkA.color = node.LinkB.color = trace;
                }
                else node.LinkA.enabled = node.LinkB.enabled = false;
                string site = ResolveName?.Invoke(n.Kind, n.X, n.Z);
                node.Control.WithTooltip((string.IsNullOrWhiteSpace(site) ? CyberWords.Callsign(network, i) : site + " / " + CyberWords.Callsign(network, i)) + " — " + CyberLocations.NodeName(n.Kind) + ", " +
                                         CyberWords.NodeState(network, i, now).ToLowerInvariant() +
                                         ". Sector " + OpsSectors.Code(n.X, n.Z) + (network.ControlsSector(n.X, n.Z, now) ? " controlled / faction ability access." : " locked / release intrusion access to control it.") +
                                         (inReach[i] ? " Within network reach. Select to inspect breach cost and readiness." : " Select to inspect this node in the breach workspace."));
            }

            bool hasSelection = network != null && network.Exists(selected);
            if (hasSelection)
            {
                CyberNode n = network.Node(selected);
                float radius = network.Online(selected) ? network.Reach : 0f;
                if (radius > 0f) bubble.Set(Board.Project(n.X, n.Z), Board.Pixels(radius), 1.5f, CyberStyle.Accent.WithAlpha(0.5f));
                else bubble.Hide();
                if (command >= 0 && selected != command && network.Online(selected) && network.Online(command))
                {
                    Vector2 target = Board.Project(n.X, n.Z);
                    Lines.Set(routeHaloA, c2.x, c2.y, target.x, c2.y, 5f);
                    Lines.Set(routeHaloB, target.x, c2.y, target.x, target.y, 5f);
                    Lines.Set(routeA, c2.x, c2.y, target.x, c2.y, 2.5f);
                    Lines.Set(routeB, target.x, c2.y, target.x, target.y, 2.5f);
                }
                else routeHaloA.enabled = routeHaloB.enabled = routeA.enabled = routeB.enabled = false;
            }
            else
            {
                bubble.Hide();
                routeHaloA.enabled = routeHaloB.enabled = routeA.enabled = routeB.enabled = false;
            }

            packetsOn = network != null && breachTarget >= 0 && network.Exists(breachTarget) && command >= 0;
            if (packetsOn)
            {
                packetFrom = c2;
                packetTo = Board.Project(network.Node(breachTarget).X, network.Node(breachTarget).Z);
                Lines.Set(breachA, packetFrom.x, packetFrom.y, packetTo.x, packetFrom.y, 1.5f);
                Lines.Set(breachB, packetTo.x, packetFrom.y, packetTo.x, packetTo.y, 1.5f);
            }
            else breachA.enabled = breachB.enabled = false;
            PlaceTags(network, now, selected);
            PaintIncidents(network);
            shownRevision = Board.Revision;
        }

        private void PlaceTags(CyberNetwork network, double now, int selected)
        {
            int n = 0;
            for (int i = 0; i < Slots && network != null && n < LabelPlacer.Maximum; i++)
            {
                if (!network.Exists(i)) continue;
                Node node = nodes[i];
                CyberNode value = network.Node(i);
                string site = ResolveName?.Invoke(value.Kind, value.X, value.Z);
                string name = (string.IsNullOrWhiteSpace(site) ? CyberWords.Callsign(network, i) : PlaceNames.Shorten(site, 18)) +
                    "\n" + OpsSectors.Code(value.X, value.Z) + (network.ControlsSector(value.X, value.Z, now) ? " / ACCESS" : " / LOCKED");
                if (node.Shown != name)
                {
                    node.Shown = name;
                    CyberStyle.Type(node.Name, name.ToUpperInvariant());
                    node.Width = Mathf.Ceil(node.Name.GetPreferredValues(node.Name.text).x) + 15f;
                    node.Tag.rectTransform.sizeDelta = new Vector2(node.Width, TagHeight);
                    node.Name.rectTransform.sizeDelta = new Vector2(node.Width - 10f, TagHeight);
                }
                anchors[n] = Board.Project(network.Node(i).X, network.Node(i).Z);
                sizes[n] = new Vector2(node.Width, TagHeight);
                priorities[n] = i == selected ? 1000 : network.Node(i).Static ? 300 : network.IsHacked(i) ? 200 : inReach[i] ? 150 : 50;
                radii[n] = Hex * 0.5f + 7f;
                labelSlot[n] = i;
                n++;
            }
            Board.PlaceLabels(anchors, sizes, priorities, radii, n, placed);
            PlacedCount = 0;
            int k = 0;
            for (int i = 0; i < Slots; i++)
            {
                Node node = nodes[i];
                bool show = k < n && labelSlot[k] == i && placed[k].Visible;
                if (k < n && labelSlot[k] == i)
                {
                    if (show)
                    {
                        var rect = new Rect(placed[k].X, placed[k].Y, placed[k].Width, placed[k].Height);
                        Chrome.Place(node.Tag.rectTransform, rect);
                        PlacedRects[PlacedCount++] = rect;
                        node.Name.color = i == selected ? CyberStyle.Accent : CyberStyle.Ink;
                        CyberNode state = network.Node(i);
                        node.TagRail.color = state.Down || state.Compromised ? AvTheme.RailDanger : network.ControlsSector(state.X, state.Z, now) ? CyberStyle.Accent : CyberStyle.Title.WithAlpha(.65f);
                    }
                    k++;
                }
                if (node.Tag.gameObject.activeSelf != show) node.Tag.gameObject.SetActive(show);
            }
        }

        private void PaintGrid()
        {
            float mpp = Board.MetresPerPixel;
            float step = GridSteps[GridSteps.Length - 1];
            for (int i = 0; i < GridSteps.Length; i++)
            {
                if (GridSteps[i] / mpp < 80f) continue;
                step = GridSteps[i];
                break;
            }
            Rect view = Board.View;
            Board.Unproject(new Vector2(view.x, view.y), out float left, out float top);
            float first = Mathf.Ceil(left / step) * step;
            float firstZ = Mathf.Floor(top / step) * step;
            for (int i = 0; i < GridLines; i++)
            {
                Vector2 px = Board.Project(first + i * step, 0f);
                if (px.x < view.x + view.width) Lines.Set(gridX[i], px.x, view.y, px.x, view.y - view.height, 1f);
                else gridX[i].enabled = false;
                Vector2 pz = Board.Project(0f, firstZ - i * step);
                if (pz.y > view.y - view.height) Lines.Set(gridZ[i], view.x, pz.y, view.x + view.width, pz.y, 1f);
                else gridZ[i].enabled = false;
            }
            float half = Board.MapHalf;
            Vector2 a = Board.Project(-half, half), b = Board.Project(half, -half);
            SetClipped(edge[0], a.x, a.y, b.x, a.y);
            SetClipped(edge[1], a.x, b.y, b.x, b.y);
            SetClipped(edge[2], a.x, a.y, a.x, b.y);
            SetClipped(edge[3], b.x, a.y, b.x, b.y);
        }

        /// <summary>A map-edge stroke, dropped when it lies outside the pane rather than drawn over the column.</summary>
        private void SetClipped(Image line, float x1, float y1, float x2, float y2)
        {
            Rect v = Board.View;
            bool inside = Mathf.Max(x1, x2) >= v.x && Mathf.Min(x1, x2) <= v.x + v.width &&
                          Mathf.Max(y1, y2) >= v.y - v.height && Mathf.Min(y1, y2) <= v.y;
            if (!inside) { line.enabled = false; return; }
            x1 = Mathf.Clamp(x1, v.x, v.x + v.width);
            x2 = Mathf.Clamp(x2, v.x, v.x + v.width);
            y1 = Mathf.Clamp(y1, v.y - v.height, v.y);
            y2 = Mathf.Clamp(y2, v.y - v.height, v.y);
            Lines.Set(line, x1, y1, x2, y2, 1f);
        }

        private void PaintFront()
        {
            frontRevision = Board.Revision;
            int used = 0, offset = 0;
            for (int t = 0; t < Board.TraceCount && used < Strokes; t++)
            {
                int length = Board.TraceLengths[t];
                Vector2 last = default;
                bool have = false;
                for (int k = 0; k < length && used < Strokes; k++)
                {
                    Vector2 p = Board.Project(Board.TracePoints[offset + k].X, Board.TracePoints[offset + k].Z);
                    if (!have) { last = p; have = true; continue; }
                    if ((p - last).sqrMagnitude < 64f && k != length - 1) continue;
                    if (Board.InView(p) && Board.InView(last)) Lines.Set(front[used++], last.x, last.y, p.x, p.y, 1.5f);
                    last = p;
                }
                offset += length;
            }
            for (int i = used; i < Strokes; i++) front[i].enabled = false;
        }

        // ---- Motion -----------------------------------------------------------------------------

        /// <summary>Updates the active packet and incident overlays at a bounded cadence.</summary>
        public void Animate(CyberNetwork network, double now, float time)
        {
            // Moving packets and incident geometry are decoration; ten updates per second is enough.
            if (time < nextAnimation) return;
            nextAnimation = time + 0.1f;
            for (int i = 0; i < Packets; i++)
            {
                if (!packetsOn || !motionAllowed) { packets[i].enabled = false; continue; }
                float t = Mathf.Repeat(time * 0.55f + i / (float)Packets, 1f);
                // Along the same L-shaped trace the link draws.
                var corner = new Vector2(packetTo.x, packetFrom.y);
                float first = Vector2.Distance(packetFrom, corner), second = Vector2.Distance(corner, packetTo);
                float along = t * (first + second);
                Vector2 p = along <= first ? Vector2.Lerp(packetFrom, corner, first > 0f ? along / first : 1f)
                    : Vector2.Lerp(corner, packetTo, second > 0f ? (along - first) / second : 1f);
                packets[i].enabled = true;
                Lines.Centre(packets[i].rectTransform, p.x, p.y, 7f);
            }
            PaintIncidents(network);
        }

        /// <summary>Incident markers represent replicated state and remain visible when decorative motion is disabled.</summary>
        private void PaintIncidents(CyberNetwork network)
        {
            for (int i = 0; i < Incidents; i++)
            {
                Threat threat = threats[i];
                bool active = network != null && network.IncidentActive(i);
                if (threat.Mark.gameObject.activeSelf != active) threat.Mark.gameObject.SetActive(active);
                if (!active)
                {
                    threat.Line.enabled = false;
                    threat.Sector.Hide();
                    continue;
                }
                CyberIncident incident = network.Incident(i);
                Vector2 at = Board.Project(incident.X, incident.Z);
                // Beside the node it sits on, so both stay readable.
                Lines.Centre(threat.Mark.rectTransform, at.x + 16f, at.y + 16f, 20f);
                threat.Mark.color = AvTheme.RailDanger.WithAlpha(0.88f);
                int command = network.CommandSlot;
                if (incident.Kind == IncidentKind.Intrusion && command >= 0 && incident.Site != command)
                {
                    // An intrusion walks the network toward Cyber Command.
                    Vector2 goal = Board.Project(network.Node(command).X, network.Node(command).Z);
                    Lines.Set(threat.Line, at.x, at.y, goal.x, goal.y, 2f);
                }
                else threat.Line.enabled = false;
                if (incident.Kind == IncidentKind.Raid)
                    threat.Sector.Set(at, Board.Pixels(CyberNetwork.RaidRadius), 1.5f, AvTheme.RailDanger.WithAlpha(0.7f));
                else threat.Sector.Hide();
            }
        }

        public static int Glyph(NodeKind kind)
        {
            switch (kind)
            {
                case NodeKind.Command: return OpsSprites.G.NodeCommand;
                case NodeKind.Base: return OpsSprites.G.NodeBase;
                case NodeKind.Airfield: return OpsSprites.G.NodeAirfield;
                case NodeKind.City: return OpsSprites.G.NodeCity;
                default: return OpsSprites.G.Port;
            }
        }
    }
}
