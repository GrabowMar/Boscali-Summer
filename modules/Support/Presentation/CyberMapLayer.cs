using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The CYBER network on the maximised map: a labelled marker per site (call sign and state
    /// word; airbase nodes as larger squares, field trucks as diamonds), link lines coloured by
    /// health, the backbone as faint blue spokes from each gateway to Cyber Command, jammer
    /// umbrellas and SIGINT ears as rings, jamming raids as amber rings, intrusions as pulsing
    /// red rings, and — while a site order is armed — a preview of the new site's link reach
    /// under the cursor. Pooled, map-local, client-only.
    /// </summary>
    internal sealed class CyberMapLayer
    {
        private const int Slots = CyberNetwork.SlotCount;
        private const int LinkCount = Slots * (Slots - 1) / 2;
        private static readonly Color Healthy = new Color(0.3f, 0.95f, 0.75f, 1f);
        private static readonly Color Warning = new Color(1f, 0.72f, 0.22f, 1f);
        private static readonly Color Hostile = new Color(1f, 0.3f, 0.26f, 1f);
        private static readonly Color Pending = new Color(0.45f, 0.7f, 1f, 1f);

        private sealed class SiteMark
        {
            public GameObject Root;
            public Image Icon;
            public TextMeshProUGUI Label;
            public Image Cover;
            public string LastLabel;
        }

        private readonly SiteMark[] sites = new SiteMark[Slots];
        private readonly Image[] links = new Image[LinkCount];
        private readonly Image[] incidents = new Image[CyberNetwork.IncidentSlots];
        private readonly Image preview;
        private readonly Image previewCore;

        public CyberMapLayer(Transform parent, TMP_FontAsset font)
        {
            for (int i = 0; i < LinkCount; i++)
            {
                links[i] = Make(parent, "CyberLink", null);
                links[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }
            for (int i = 0; i < incidents.Length; i++)
                incidents[i] = Make(parent, "CyberIncident", SupportTacticalIcons.RingSprite);
            for (int i = 0; i < Slots; i++) sites[i] = BuildSite(parent, font);
            preview = Make(parent, "CyberPreview", SupportTacticalIcons.DottedRingSprite);
            previewCore = Make(parent, "CyberPreviewCore", SupportTacticalIcons.CrosshairSprite);
        }

        public void Update(SupportManager support, DynamicMap map, float mapFactor, float invZoom)
        {
            CyberNetwork network = support != null ? support.LocalCyber : null;
            double now = support != null ? support.OrbitNow : 0.0;
            float time = Time.unscaledTime;
            bool any = network != null && network.SiteCount > 0 && support.CyberEnabled;

            for (int i = 0; i < Slots; i++) PaintSite(network, i, now, time, any, mapFactor, invZoom);

            int index = 0;
            for (int a = 0; a < Slots; a++)
            {
                for (int b = a + 1; b < Slots; b++, index++)
                {
                    Image line = links[index];
                    bool linked = any && network.Linked(a, b);
                    // The backbone is a full mesh; drawn as spokes to Cyber Command it reads without clutter.
                    bool backbone = linked && network.Backbone(a, b);
                    if (backbone && network.Site(a).Kind != CyberSiteKind.Command &&
                        network.Site(b).Kind != CyberSiteKind.Command)
                        linked = false;
                    Show(line, linked);
                    if (!linked) continue;
                    CyberSite from = network.Site(a), to = network.Site(b);
                    bool hot = from.Compromised || to.Compromised;
                    bool live = network.OnNet(a) && network.OnNet(b);
                    var pa = new Vector2(from.X * mapFactor, from.Z * mapFactor);
                    var pb = new Vector2(to.X * mapFactor, to.Z * mapFactor);
                    Vector2 d = pb - pa;
                    RectTransform rt = line.rectTransform;
                    rt.localPosition = (pa + pb) * 0.5f;
                    rt.sizeDelta = new Vector2(d.magnitude, (backbone ? 1.5f : 2.5f) * invZoom);
                    rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    Color colour = hot ? Hostile : !live ? Warning : backbone ? Pending : Healthy;
                    line.color = colour.WithAlpha(hot ? 0.5f + 0.4f * Mathf.PingPong(time * 3f, 1f)
                        : !live ? 0.4f : backbone ? 0.3f : 0.75f);
                }
            }

            for (int i = 0; i < incidents.Length; i++) PaintIncident(network, i, now, time, any, mapFactor, invZoom);
            PaintPreview(support, map, network, mapFactor, invZoom, time);
        }

        private SiteMark BuildSite(Transform parent, TMP_FontAsset font)
        {
            var mark = new SiteMark
            {
                Cover = Make(parent, "CyberCover", SupportTacticalIcons.RingSprite),
                Root = new GameObject("CyberSite", typeof(RectTransform))
            };
            mark.Root.transform.SetParent(parent, false);
            mark.Icon = Make(mark.Root.transform, "Icon", null);
            mark.Icon.rectTransform.sizeDelta = new Vector2(12f, 12f);
            mark.Icon.gameObject.SetActive(true);
            mark.Icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);

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

        private void PaintSite(CyberNetwork network, int slot, double now, float time, bool any, float mapFactor,
                               float invZoom)
        {
            SiteMark mark = sites[slot];
            bool exists = any && network.Exists(slot);
            if (mark.Root.activeSelf != exists) mark.Root.SetActive(exists);
            if (!exists)
            {
                Show(mark.Cover, false);
                return;
            }
            CyberSite site = network.Site(slot);
            var position = new Vector3(site.X * mapFactor, site.Z * mapFactor, 0f);
            mark.Root.transform.localPosition = position;
            mark.Root.transform.localScale = Vector3.one * invZoom;

            Color colour = site.Lost || site.Compromised || site.Down ? Hostile
                : site.Deploying ? Pending
                : site.Isolated || !network.OnNet(slot) || network.Jammed(slot, now) ? Warning
                : Healthy;
            bool blink = site.Compromised || site.Deploying || site.Down;
            mark.Icon.color = colour.WithAlpha(blink ? 0.5f + 0.5f * Mathf.PingPong(time * 2f, 1f) : 1f);
            // Airbase nodes are squares a size up; field trucks stay diamonds.
            mark.Icon.rectTransform.sizeDelta = site.Static ? new Vector2(15f, 15f) : new Vector2(12f, 12f);
            mark.Icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, site.Static ? 0f : 45f);
            string state = CyberWords.SiteState(network, slot, now);
            string label = "<b>" + CyberWords.Callsign(network, slot) + "</b>\n" +
                           (site.Kind == CyberSiteKind.Jammer && state == "ONLINE" ? CyberWords.Mode(site.Mode) : state);
            if (mark.LastLabel != label) mark.Label.text = mark.LastLabel = label;
            mark.Label.color = colour;

            float radius = network.EffectRadius(slot, now);
            bool cover = network.Working(slot) && radius > 0f &&
                         (site.Kind != CyberSiteKind.Jammer || EwPostures.Umbrella(site.Mode) > 0f);
            Show(mark.Cover, cover);
            if (!cover) return;
            mark.Cover.transform.localPosition = position;
            mark.Cover.rectTransform.sizeDelta = Vector2.one * radius * 2f * mapFactor;
            mark.Cover.sprite = site.Kind == CyberSiteKind.Sigint ? SupportTacticalIcons.DottedRingSprite : SupportTacticalIcons.RingSprite;
            mark.Cover.color = colour.WithAlpha(site.Kind == CyberSiteKind.Jammer ? 0.35f : 0.2f);
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
                x = network.Site(incident.Site).X;
                z = network.Site(incident.Site).Z;
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

        private void PaintPreview(SupportManager support, DynamicMap map, CyberNetwork network, float mapFactor,
                                  float invZoom, float time)
        {
            bool armed = support != null && support.CommandArmed &&
                         (support.ArmedCommand == OpsCommand.CyberBuild || support.ArmedCommand == OpsCommand.CyberMove);
            GlobalPosition cursor = default;
            bool show = armed && map != null && map.TryGetCursorCoordinates(out cursor);
            Show(preview, show);
            Show(previewCore, show);
            if (!show) return;

            CyberSiteKind kind = support.ArmedCommand == OpsCommand.CyberBuild
                ? (CyberSiteKind)support.ArmedCommandArg
                : network != null ? network.Site(support.ArmedCommandArg).Kind : CyberSiteKind.None;
            float reach = CyberSites.Known((byte)kind) ? CyberSites.Info(kind).LinkRange : CyberSites.DefaultLinkRange;
            bool linked = LinksAt(network, cursor, reach, support.ArmedCommand == OpsCommand.CyberMove ? support.ArmedCommandArg : -1);
            Color colour = linked ? Healthy : Warning;

            var local = new Vector3(cursor.x * mapFactor, cursor.z * mapFactor, 0f);
            preview.transform.localPosition = local;
            preview.rectTransform.sizeDelta = Vector2.one * reach * 2f * mapFactor;
            preview.color = colour.WithAlpha(0.45f + 0.2f * Mathf.PingPong(time, 1f));
            previewCore.transform.localPosition = local;
            previewCore.rectTransform.sizeDelta = new Vector2(26f, 26f) * invZoom;
            previewCore.color = colour;
        }

        /// <summary>Would a site at the cursor link to anything already on the net?</summary>
        private static bool LinksAt(CyberNetwork network, GlobalPosition at, float reach, int moving)
        {
            if (network == null) return false;
            for (int i = 0; i < Slots; i++)
            {
                if (i == moving || !network.OnNet(i)) continue;
                CyberSite site = network.Site(i);
                float range = Mathf.Max(reach, CyberSites.Info(site.Kind).LinkRange);
                float dx = site.X - at.x, dz = site.Z - at.z;
                if (dx * dx + dz * dz <= range * range) return true;
            }
            return false;
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
