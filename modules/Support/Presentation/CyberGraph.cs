using System;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The CYBER network drawn at its real geography: sites as lit squares with their call
    /// sign and state word (airbase nodes larger and solid), links as lines (bright on the net,
    /// dim off it, red where an intrusion walks, a steady blue backbone between airbases),
    /// traffic pulses running toward Cyber Command, intrusions as pulsing red frames and
    /// jamming raids as amber sector boxes. Everything is pooled at build time (16 nodes,
    /// 120 links, 6 incident marks) and repainted in place; the same widget serves the NETWORK
    /// page (compact) and the console (large, clickable).
    /// </summary>
    internal sealed class CyberGraph
    {
        private const int Slots = CyberNetwork.SlotCount;
        private const int LinkCount = Slots * (Slots - 1) / 2;
        private const float MinimumSpan = 24000f;
        private const float LegendBand = 16f;

        private sealed class Node
        {
            public RectTransform Root;
            public Image Fill;
            public Image[] Frame;
            public TMP_Text Code;
            public TMP_Text State;
            public Image Halo;
            public AvButton Hit;
            public string LastCode, LastState;
        }

        private readonly RectTransform parent;
        private readonly Rect area;
        private readonly float nodeSize;
        private readonly bool large;
        private readonly Node[] nodes = new Node[Slots];
        private readonly Image[] links = new Image[LinkCount];
        private readonly Image[] packets = new Image[LinkCount];
        private readonly Image[][] marks = new Image[CyberNetwork.IncidentSlots][];
        private readonly TMP_Text[] markLabels = new TMP_Text[CyberNetwork.IncidentSlots];
        private readonly TMP_Text empty;
        private readonly TMP_Text scaleLabel;
        private readonly Vector2[] points = new Vector2[Slots];
        private float centreX, centreZ, scale = 1f;
        private float spanMetres = MinimumSpan;
        private int columns;

        public CyberGraph(RectTransform parent, Rect area, bool large, Action<int> onNode)
        {
            this.parent = parent;
            this.area = area;
            this.large = large;
            nodeSize = large ? 34f : 16f;
            columns = large ? 8 : 6;

            AvKit.Panel(parent, area, AvTheme.Unity(AvTokens.Ground));
            AvKit.Outline(parent, area, AvTheme.Hairline);
            if (large) AvKit.CornerTicks(parent, new Rect(area.x + 8f, area.y - 8f, area.width - 16f, area.height - 16f),
                AvTheme.RailReady.WithAlpha(0.7f), 28f);
            Grid();
            Legend();
            scaleLabel = AvKit.Label(parent, "", new Rect(area.x + AvTokens.Space2, area.y - area.height + LegendBand - 1f,
                320f, 12f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold);

            for (int i = 0; i < LinkCount; i++)
            {
                links[i] = Segment(Color.clear);
                if (large) packets[i] = AvKit.Panel(parent, new Rect(0f, 0f, 6f, 6f), Color.clear, AvSprites.Led);
            }
            for (int i = 0; i < marks.Length; i++)
            {
                marks[i] = AvKit.Outline(parent, new Rect(area.x, area.y, 10f, 10f), Color.clear);
                markLabels[i] = AvKit.Label(parent, "", new Rect(area.x, area.y, 160f, 12f), Color.clear,
                    AvTokens.FontMicro, FontStyles.Bold);
            }
            for (int i = 0; i < Slots; i++) nodes[i] = BuildNode(i, onNode);

            empty = AvKit.Label(parent, "", new Rect(area.x + 12f, area.y - area.height * 0.5f + 10f, area.width - 24f, 20f),
                AvTheme.Dim, large ? AvTokens.FontTitle : AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        /// <summary>Screen point of a slot from the last paint (top-left origin, y down negative).</summary>
        public Vector2 PointOf(int slot) => slot >= 0 && slot < Slots ? points[slot] : Vector2.zero;

        public void Paint(CyberNetwork network, double now, int selected, float time, string emptyText)
        {
            bool any = network != null && network.SiteCount > 0;
            empty.text = any ? "" : emptyText ?? "";
            if (any) Fit(network);

            // The grid has a stated cell size, so the picture has scale without an axis frame.
            int cellKm = Mathf.Max(1, Mathf.RoundToInt(spanMetres / columns / 1000f));
            scaleLabel.text = "GRID " + cellKm + " KM · " + Mathf.RoundToInt(spanMetres / 1000f) + " KM ACROSS";

            for (int i = 0; i < Slots; i++) PaintNode(network, i, now, selected, time, any);

            int index = 0;
            for (int a = 0; a < Slots; a++)
            {
                for (int b = a + 1; b < Slots; b++, index++)
                {
                    Image line = links[index];
                    bool linked = any && network.Linked(a, b);
                    if (!linked)
                    {
                        line.color = Color.clear;
                        if (packets[index] != null) packets[index].color = Color.clear;
                        continue;
                    }
                    bool hot = network.Site(a).Compromised || network.Site(b).Compromised;
                    bool live = network.OnNet(a) && network.OnNet(b);
                    bool backbone = network.Backbone(a, b);
                    Color colour = hot ? AvTheme.RailDanger : !live ? AvTheme.RailCaution
                        : backbone ? AvTheme.RailInfo : AvTheme.RailReady;
                    float pulse = hot ? 0.55f + 0.45f * Mathf.PingPong(time * 3f, 1f)
                        : !live ? 0.35f : backbone ? 0.45f : 0.7f;
                    Place(line, points[a], points[b], backbone ? (large ? 2f : 1f) : large ? 3f : 2f);
                    line.color = colour.WithAlpha(pulse);
                    if (packets[index] != null) PaintPacket(network, packets[index], a, b, colour, time, index);
                }
            }

            for (int i = 0; i < marks.Length; i++) PaintMark(network, i, now, time, any);
        }

        // ---- Build ---------------------------------------------------------------------------

        private void Grid()
        {
            Color grid = AvTheme.Hairline.WithAlpha(0.25f);
            for (int i = 1; i < columns; i++)
            {
                AvKit.Rule(parent, new Rect(area.x + area.width * i / columns, area.y, 1f, area.height), grid);
                AvKit.Rule(parent, new Rect(area.x, area.y - area.height * i / columns, area.width, 1f), grid);
            }
        }

        /// <summary>
        /// Colour never carries a state alone: every series colour is named in the same band
        /// as the grid scale, and every node — compact or large — says its state in words.
        /// </summary>
        private void Legend()
        {
            // Colour never carries a state alone: every colour the graph paints has a name
            // here. The compact band has no room for the two outage colours, and its nodes
            // carry their own state word; the console names all six.
            string[] words = large
                ? new[] { "ON NET", "OFF-NET", "COMPROMISED", "BACKBONE", "LOST", "ISOLATED" }
                : new[] { "ON NET", "OFF-NET", "COMPROMISED", "BACKBONE" };
            Color[] colours = large
                ? new[]
                {
                    AvTheme.RailReady, AvTheme.RailCaution, AvTheme.RailDanger, AvTheme.RailInfo,
                    AvTheme.Dim, AvTheme.Disabled,
                }
                : new[] { AvTheme.RailReady, AvTheme.RailCaution, AvTheme.RailDanger, AvTheme.RailInfo };
            float y = area.y - area.height + LegendBand - 1f;
            float x = area.x + area.width - AvTokens.Space2;
            for (int i = words.Length - 1; i >= 0; i--)
            {
                TMP_Text label = AvKit.Label(parent, words[i], new Rect(x, y, 10f, 12f), colours[i], AvTokens.FontMicro,
                    FontStyles.Bold);
                float width = Mathf.Ceil(label.GetPreferredValues(words[i]).x) + 1f;
                label.rectTransform.sizeDelta = new Vector2(width, 12f);
                label.rectTransform.anchoredPosition = new Vector2(x - width, y);
                x -= width + AvTokens.Space1;
                AvKit.Panel(parent, new Rect(x - 9f, y + 1f, 9f, 9f), colours[i], AvSprites.Led);
                x -= 9f + AvTokens.Space2;
            }
        }

        private Node BuildNode(int slot, Action<int> onNode)
        {
            var go = new GameObject("Site" + slot, typeof(RectTransform));
            var root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            AvKit.Place(root, new Rect(0f, 0f, nodeSize, nodeSize));
            var node = new Node
            {
                Root = root,
                Halo = AvKit.Panel(root, new Rect(-10f, 10f, nodeSize + 20f, nodeSize + 20f), Color.clear, AvSprites.Led),
                Fill = AvKit.Panel(root, new Rect(0f, 0f, nodeSize, nodeSize), Color.clear, AvSprites.Control),
                Frame = AvKit.Outline(root, new Rect(0f, 0f, nodeSize, nodeSize), Color.clear),
                Code = AvKit.Label(root, "", new Rect(-40f, large ? -nodeSize - 2f : -nodeSize, nodeSize + 80f, large ? 16f : 11f),
                    AvTheme.TextPrimary, large ? AvTokens.FontSmall : AvTokens.FontMicro, FontStyles.Bold,
                    TextAlignmentOptions.Center),
                // Every node names its state: a compact NETWORK square is a colour plus its
                // word, never colour alone, exactly like the large console node below it.
                State = AvKit.Label(root, "", new Rect(-40f, large ? -nodeSize - 17f : -nodeSize - 11f,
                        nodeSize + 80f, large ? 13f : 11f),
                    AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center)
            };
            if (onNode != null)
                node.Hit = AvKit.HitButton(root, new Rect(-4f, 4f, nodeSize + 8f, nodeSize + 8f), () => onNode(slot));
            root.gameObject.SetActive(false);
            return node;
        }

        private Image Segment(Color colour)
        {
            Image line = AvKit.Panel(parent, new Rect(0f, 0f, 1f, 1f), colour);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return line;
        }

        // ---- Paint ---------------------------------------------------------------------------

        private void Fit(CyberNetwork network)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < Slots; i++)
            {
                if (!network.Exists(i)) continue;
                CyberSite site = network.Site(i);
                minX = Mathf.Min(minX, site.X);
                maxX = Mathf.Max(maxX, site.X);
                minZ = Mathf.Min(minZ, site.Z);
                maxZ = Mathf.Max(maxZ, site.Z);
            }
            centreX = (minX + maxX) * 0.5f;
            centreZ = (minZ + maxZ) * 0.5f;
            spanMetres = Mathf.Max(MinimumSpan, Mathf.Max(maxX - minX, maxZ - minZ) * 1.25f);
            float usable = Mathf.Min(area.width, area.height - LegendBand) - nodeSize * 2f - (large ? 40f : 16f);
            scale = usable / spanMetres;
            for (int i = 0; i < Slots; i++)
            {
                if (!network.Exists(i)) continue;
                CyberSite site = network.Site(i);
                points[i] = Project(site.X, site.Z);
            }
        }

        private Vector2 Project(float x, float z) =>
            new Vector2(area.x + area.width * 0.5f + (x - centreX) * scale,
                        area.y - area.height * 0.5f + (z - centreZ) * scale);

        private void PaintNode(CyberNetwork network, int slot, double now, int selected, float time, bool any)
        {
            Node node = nodes[slot];
            bool exists = any && network.Exists(slot);
            if (node.Root.gameObject.activeSelf != exists) node.Root.gameObject.SetActive(exists);
            if (!exists) return;

            CyberSite site = network.Site(slot);
            Vector2 at = points[slot];
            // Airbase nodes draw larger; the root pivots at its top-left corner, so centre the scaled box.
            float scale = site.Static ? 1.35f : 1f;
            float half = nodeSize * 0.5f * scale;
            node.Root.anchoredPosition = new Vector2(at.x - half, at.y + half);
            node.Root.localScale = new Vector3(scale, scale, 1f);

            Color colour = SiteColour(network, slot, now);
            bool blink = site.Compromised || site.Lost || site.Deploying || site.Down;
            float alpha = blink ? 0.45f + 0.55f * Mathf.PingPong(time * 2.5f, 1f) : 1f;
            node.Fill.color = colour.WithAlpha((site.Kind == CyberSiteKind.Command ? 0.6f : site.Static ? 0.45f : 0.25f) * alpha);
            Color border = slot == selected ? Color.white : colour.WithAlpha(alpha);
            for (int i = 0; i < node.Frame.Length; i++) node.Frame[i].color = border;
            bool compromised = site.Compromised || site.Lost;
            node.Halo.color = site.HoneypotUntil > now
                ? AvTheme.RailInfo.WithAlpha(0.24f + 0.16f * Mathf.PingPong(time * 1.5f, 1f))
                : compromised
                ? AvTheme.RailDanger.WithAlpha(0.22f + 0.22f * Mathf.PingPong(time * 3f, 1f))
                : slot == selected ? AvTheme.Accent.WithAlpha(0.20f) : Color.clear;

            string code = large ? CyberWords.Callsign(network, slot) : CyberSites.Code(site.Kind);
            if (node.LastCode != code) node.Code.text = node.LastCode = code;
            node.Code.color = site.Lost ? AvTheme.Dim : AvTheme.TextPrimary;
            string state = CyberWords.SiteState(network, slot, now);
            if (site.Kind == CyberSiteKind.Jammer && state == "ONLINE") state = CyberWords.Mode(site.Mode);
            if (node.State != null)
            {
                if (node.LastState != state) node.State.text = node.LastState = state;
                node.State.color = colour;
            }
            if (node.Hit != null) node.Hit.WithTooltip(code + " · " + state + " · CLICK TO SELECT");
        }

        private void PaintPacket(CyberNetwork network, Image packet, int a, int b, Color colour, float time, int index)
        {
            // Traffic flows toward Cyber Command: from the site further out to the one nearer in.
            int from = network.Hops(a) >= network.Hops(b) ? a : b;
            int to = from == a ? b : a;
            float t = Mathf.Repeat(time * 0.45f + index * 0.137f, 1f);
            Vector2 at = Vector2.Lerp(points[from], points[to], t);
            packet.rectTransform.anchoredPosition = new Vector2(at.x - 3f, at.y + 3f);
            packet.color = colour.WithAlpha(0.9f);
        }

        private void PaintMark(CyberNetwork network, int index, double now, float time, bool any)
        {
            Image[] frame = marks[index];
            TMP_Text label = markLabels[index];
            CyberIncident incident = any ? network.Incident(index) : default;
            bool show = any && network.IncidentActive(index);
            if (!show)
            {
                for (int i = 0; i < frame.Length; i++) frame[i].color = Color.clear;
                label.color = Color.clear;
                return;
            }

            Rect box;
            Color colour;
            if (incident.Kind == IncidentKind.Raid)
            {
                float half = Mathf.Max(nodeSize, CyberNetwork.RaidRadius * scale);
                Vector2 c = Project(incident.X, incident.Z);
                box = new Rect(c.x - half, c.y + half, half * 2f, half * 2f);
                colour = AvTheme.RailCaution.WithAlpha(0.35f + 0.25f * Mathf.PingPong(time, 1f));
            }
            else if (network.Exists(incident.Site))
            {
                float half = nodeSize * 0.5f + 8f + 4f * Mathf.PingPong(time * 2f, 1f);
                Vector2 c = points[incident.Site];
                box = new Rect(c.x - half, c.y + half, half * 2f, half * 2f);
                colour = (incident.Kind == IncidentKind.Probe ? AvTheme.RailCaution
                    : incident.Kind == IncidentKind.HostileOperation ? AvTheme.RailInfo
                    : AvTheme.RailDanger).WithAlpha(0.9f);
            }
            else
            {
                for (int i = 0; i < frame.Length; i++) frame[i].color = Color.clear;
                label.color = Color.clear;
                return;
            }

            SetOutline(frame, box);
            for (int i = 0; i < frame.Length; i++) frame[i].color = colour;
            // The incident mark is labelled in both sizes: an amber box alone is not a raid.
            string text = CyberWords.IncidentCode(incident.Kind) +
                          (incident.Tracing ? " · TRACE " + Mathf.RoundToInt(incident.Trace * 100f) + "%" : "");
            label.text = text;
            label.color = colour;
            label.rectTransform.anchoredPosition = new Vector2(box.x, box.y + 14f);
        }

        private static Color SiteColour(CyberNetwork network, int slot, double now)
        {
            CyberSite site = network.Site(slot);
            if (site.Lost) return AvTheme.Dim;
            if (site.Compromised || site.Down) return AvTheme.RailDanger;
            if (site.Deploying) return AvTheme.RailInfo;
            if (site.Isolated) return AvTheme.Disabled;
            if (!network.OnNet(slot) || network.Jammed(slot, now)) return AvTheme.RailCaution;
            return AvTheme.RailReady;
        }

        private static void Place(Image line, Vector2 a, Vector2 b, float thickness)
        {
            RectTransform rt = line.rectTransform;
            Vector2 d = b - a;
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(d.magnitude, thickness);
            rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        /// <summary>Moves the four rules <see cref="AvKit.Outline"/> made onto a new rectangle.</summary>
        private static void SetOutline(Image[] frame, Rect box)
        {
            if (frame.Length < 4) return;
            Set(frame[0], new Rect(box.x, box.y, box.width, 1f));
            Set(frame[1], new Rect(box.x, box.y - box.height + 1f, box.width, 1f));
            Set(frame[2], new Rect(box.x, box.y, 1f, box.height));
            Set(frame[3], new Rect(box.x + box.width - 1f, box.y, 1f, box.height));
        }

        private static void Set(Image image, Rect r)
        {
            image.rectTransform.anchoredPosition = new Vector2(r.x, r.y);
            image.rectTransform.sizeDelta = new Vector2(r.width, r.height);
        }
    }
}
