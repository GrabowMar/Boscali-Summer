using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The CYBER network on the maximised map: a labelled marker per node (call sign and state
    /// word; Cyber Command and airbases as squares, hacked locations as diamonds, untaken
    /// locations as dim hollow targets), the ability radius of every working hacked location as a
    /// ring, the live breach as a pulsing ring with its trace, jamming raids as amber rings and
    /// intrusions as pulsing red rings. Pooled, map-local, client-only.
    /// </summary>
    internal sealed class CyberMapLayer
    {
        private const int Slots = CyberNetwork.SlotCount;
        private static readonly Color Healthy = new Color(0.3f, 0.95f, 0.75f, 1f);
        private static readonly Color Warning = new Color(1f, 0.72f, 0.22f, 1f);
        private static readonly Color Hostile = new Color(1f, 0.3f, 0.26f, 1f);
        private static readonly Color Pending = new Color(0.45f, 0.7f, 1f, 1f);
        private static readonly Color Untaken = new Color(0.6f, 0.6f, 0.65f, 1f);

        private sealed class NodeMark
        {
            public GameObject Root;
            public Image Icon;
            public TextMeshProUGUI Label;
            public Image Cover;
            public string LastLabel;
        }

        private readonly NodeMark[] nodes = new NodeMark[Slots];
        private readonly Image[] incidents = new Image[CyberNetwork.IncidentSlots];
        private readonly Image breach;

        public CyberMapLayer(Transform parent, TMP_FontAsset font)
        {
            for (int i = 0; i < incidents.Length; i++)
                incidents[i] = Make(parent, "CyberIncident", SupportTacticalIcons.RingSprite);
            for (int i = 0; i < Slots; i++) nodes[i] = BuildNode(parent, font);
            breach = Make(parent, "CyberBreach", SupportTacticalIcons.DottedRingSprite);
        }

        public void Update(SupportManager support, DynamicMap map, float mapFactor, float invZoom)
        {
            CyberNetwork network = support != null ? support.LocalCyber : null;
            double now = support != null ? support.OrbitNow : 0.0;
            float time = Time.unscaledTime;
            bool any = network != null && network.Stats().Nodes > 0 && support.CyberEnabled;

            for (int i = 0; i < Slots; i++) PaintNode(network, i, now, time, any, mapFactor, invZoom);
            for (int i = 0; i < incidents.Length; i++) PaintIncident(network, i, now, time, any, mapFactor, invZoom);
            PaintBreach(network, now, time, any, mapFactor, invZoom);
        }

        private NodeMark BuildNode(Transform parent, TMP_FontAsset font)
        {
            var mark = new NodeMark
            {
                Cover = Make(parent, "CyberCover", SupportTacticalIcons.RingSprite),
                Root = new GameObject("CyberNode", typeof(RectTransform))
            };
            mark.Root.transform.SetParent(parent, false);
            mark.Icon = Make(mark.Root.transform, "Icon", null);
            mark.Icon.rectTransform.sizeDelta = new Vector2(12f, 12f);
            mark.Icon.gameObject.SetActive(true);

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(mark.Root.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.sizeDelta = new Vector2(150f, 26f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -10f);
            mark.Label = textObject.GetComponent<TextMeshProUGUI>();
            if (font != null) mark.Label.font = font;
            mark.Label.fontSize = 8.5f;
            mark.Label.alignment = TextAlignmentOptions.Top;
            mark.Label.raycastTarget = false;
            mark.Root.SetActive(false);
            return mark;
        }

        private void PaintNode(CyberNetwork network, int slot, double now, float time, bool any, float mapFactor,
                               float invZoom)
        {
            NodeMark mark = nodes[slot];
            bool exists = any && network.Exists(slot);
            if (mark.Root.activeSelf != exists) mark.Root.SetActive(exists);
            if (!exists)
            {
                Show(mark.Cover, false);
                return;
            }
            CyberNode node = network.Node(slot);
            var position = new Vector3(node.X * mapFactor, node.Z * mapFactor, 0f);
            mark.Root.transform.localPosition = position;
            mark.Root.transform.localScale = Vector3.one * invZoom;

            bool taken = node.Static || node.Hacked;
            Color colour = node.Down || node.Compromised ? Hostile
                : node.Isolated ? Warning
                : !taken ? Untaken
                : network.Jammed(slot, now) ? Warning
                : node.Static ? Pending : Healthy;
            bool blink = node.Compromised || node.Down;
            mark.Icon.color = colour.WithAlpha(blink ? 0.5f + 0.5f * Mathf.PingPong(time * 2f, 1f) : 1f);
            // Home nodes are squares a size up; hacked locations and targets stay diamonds.
            mark.Icon.rectTransform.sizeDelta = node.Static ? new Vector2(15f, 15f) : new Vector2(12f, 12f);
            mark.Icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, node.Static ? 0f : 45f);
            string state = CyberWords.NodeState(network, slot, now);
            if (network.BreachActive && network.BreachTarget == slot)
                state = "BREACH " + Mathf.RoundToInt(network.BreachTrace * 100f) + "%";
            string label = "<b>" + CyberWords.Callsign(network, slot) + "</b>\n" + state;
            if (mark.LastLabel != label) mark.Label.text = mark.LastLabel = label;
            mark.Label.color = colour;

            float radius = network.RadiusOf(slot, now);
            bool cover = network.Working(slot) && radius > 0f;
            Show(mark.Cover, cover);
            if (!cover) return;
            mark.Cover.transform.localPosition = position;
            mark.Cover.rectTransform.sizeDelta = Vector2.one * radius * 2f * mapFactor;
            mark.Cover.sprite = SupportTacticalIcons.RingSprite;
            mark.Cover.color = colour.WithAlpha(0.16f);
        }

        private void PaintIncident(CyberNetwork network, int index, double now, float time, bool any, float mapFactor,
                                   float invZoom)
        {
            Image ring = incidents[index];
            bool active = any && network.IncidentActive(index);
            if (!active)
            {
                Show(ring, false);
                return;
            }
            CyberIncident incident = network.Incident(index);
            float x = incident.X, z = incident.Z;
            if (incident.Kind != IncidentKind.Raid && network.Exists(incident.Site))
            {
                x = network.Node(incident.Site).X;
                z = network.Node(incident.Site).Z;
            }
            Show(ring, true);
            ring.transform.localPosition = new Vector3(x * mapFactor, z * mapFactor, 0f);
            float size = incident.Kind == IncidentKind.Raid
                ? CyberNetwork.RaidRadius * 2f * mapFactor
                : (40f + 12f * Mathf.PingPong(time * 2f, 1f)) * invZoom;
            ring.rectTransform.sizeDelta = new Vector2(size, size);
            ring.color = (incident.Kind == IncidentKind.Raid ? Warning
                : incident.Kind == IncidentKind.HostileOperation ? Pending : Hostile)
                .WithAlpha(0.45f + 0.4f * Mathf.PingPong(time * 1.5f, 1f));
        }

        private void PaintBreach(CyberNetwork network, double now, float time, bool any, float mapFactor, float invZoom)
        {
            bool show = any && network.BreachActive && network.Exists(network.BreachTarget);
            Show(breach, show);
            if (!show) return;
            CyberNode target = network.Node(network.BreachTarget);
            breach.transform.localPosition = new Vector3(target.X * mapFactor, target.Z * mapFactor, 0f);
            float size = (70f + 16f * Mathf.PingPong(time * 2f, 1f)) * invZoom;
            breach.rectTransform.sizeDelta = new Vector2(size, size);
            breach.color = Healthy.WithAlpha(0.5f + 0.4f * Mathf.PingPong(time * 2f, 1f));
        }

        private static Image Make(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }

        private static void Show(Graphic graphic, bool on)
        {
            if (graphic.gameObject.activeSelf != on) graphic.gameObject.SetActive(on);
        }
    }
}
