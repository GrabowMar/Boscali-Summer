using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Separate membership ring and screen-aligned command brackets without recolouring the
    /// aircraft silhouette.</summary>
    internal static class WingMarkerBadge
    {
        private const string RingObjectName = "WingCommand_MembershipRing";
        private const string SelectionObjectName = "WingCommand_SelectionBadge";
        private const string StatusObjectName = "WingCommand_StatusLabel";

        /// <summary>The badge children of one host, found once (only this class creates them).</summary>
        private sealed class Slots
        {
            public RectTransform Layer;
            public WingMapRingGraphic Ring;
            public Image Selection;
            public TMP_Text Status;
        }

        private static readonly Dictionary<Image, Slots> slots = new Dictionary<Image, Slots>();
        private static readonly List<Image> deadHosts = new List<Image>();

        private static Slots SlotsOf(Image host)
        {
            if (slots.TryGetValue(host, out Slots known)) return known;
            if (slots.Count > 512)
            {
                deadHosts.Clear();
                foreach (Image h in slots.Keys)
                    if (h == null) deadHosts.Add(h);
                foreach (Image h in deadHosts) slots.Remove(h);
            }
            Transform ring = host.transform.Find(RingObjectName);
            Transform selection = host.transform.Find(SelectionObjectName);
            Transform status = host.transform.Find(StatusObjectName);
            return slots[host] = new Slots
            {
                Ring = ring != null ? ring.GetComponent<WingMapRingGraphic>() : null,
                Selection = selection != null ? selection.GetComponent<Image>() : null,
                Status = status != null ? status.GetComponent<TMP_Text>() : null,
            };
        }

        public static void Apply(Image host, WingMapPresentation presentation, Color color, string status = null)
        {
            if (host == null) return;
            Slots known = SlotsOf(host);
            WingMapRingGraphic ring = known.Ring;
            bool outlined = presentation.Outline != WingMapPresentation.OutlineKind.None;
            if (!outlined && !presentation.CommandBrackets)
            {
                if (known.Layer != null) known.Layer.gameObject.SetActive(false);
                if (ring != null) ring.gameObject.SetActive(false);
                if (known.Selection != null) known.Selection.gameObject.SetActive(false);
                if (known.Status != null) known.Status.gameObject.SetActive(false);
                return;
            }

            if (known.Layer == null)
            {
                var go = new GameObject("WingCommand_BadgeLayer", typeof(RectTransform), typeof(WingMapBadgePose));
                known.Layer = go.GetComponent<RectTransform>();
                known.Layer.SetParent(host.rectTransform, false);
                go.GetComponent<WingMapBadgePose>().Host = host;
            }
            known.Layer.gameObject.SetActive(true);
            CancelIconScale(host, known.Layer);

            // Measure rendered icon dimensions because native local scale includes zoom, icon size, and
            // heading rotation.
            Canvas canvas = host.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            Rect rect = host.rectTransform.rect;
            Vector2 bottomLeft = ScreenPoint(host.rectTransform, new Vector2(rect.xMin, rect.yMin), camera);
            Vector2 bottomRight = ScreenPoint(host.rectTransform, new Vector2(rect.xMax, rect.yMin), camera);
            Vector2 topLeft = ScreenPoint(host.rectTransform, new Vector2(rect.xMin, rect.yMax), camera);
            WingMapBadgeGeometry geometry = WingMapBadgeGeometry.ForIcon(
                Vector2.Distance(bottomLeft, bottomRight), Vector2.Distance(bottomLeft, topLeft));

            if (outlined)
            {
                if (ring == null)
                {
                    var go = new GameObject(RingObjectName, typeof(RectTransform), typeof(WingMapRingGraphic));
                    ring = go.GetComponent<WingMapRingGraphic>();
                    ring.rectTransform.SetParent(known.Layer, worldPositionStays: false);
                    known.Ring = ring;
                }
                ring.gameObject.SetActive(true);
                ring.raycastTarget = false;
                AlignToScreen(ring.rectTransform, canvas, camera);
                ring.SetGeometry(geometry);
                if (ring.color != color) ring.color = color;
            }
            else if (ring != null)
            {
                ring.gameObject.SetActive(false);
            }

            ApplyCommandSelection(host, presentation.CommandBrackets, color, geometry, canvas, camera);
            ApplyStatus(host, status, color, geometry, canvas, camera);
        }

        private static void ApplyStatus(Image host, string text, Color color, WingMapBadgeGeometry geometry,
            Canvas canvas, Camera camera)
        {
            Slots known = SlotsOf(host);
            TMP_Text label = known.Status;
            if (string.IsNullOrEmpty(text))
            {
                if (label != null) label.gameObject.SetActive(false);
                return;
            }

            if (label == null)
            {
                var go = new GameObject(StatusObjectName, typeof(RectTransform), typeof(TextMeshProUGUI));
                label = go.GetComponent<TMP_Text>();
                label.rectTransform.SetParent(known.Layer, worldPositionStays: false);
                label.fontSize = 11f;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                known.Status = label;
            }

            label.gameObject.SetActive(true);
            AlignToScreen(label.rectTransform, canvas, camera);
            // Offset in world space: anchoredPosition is in the icon's own space, which carries map zoom and heading.
            // After AlignToScreen one local unit is one pixel, so lossyScale is world units per pixel.
            RectTransform rt = label.rectTransform;
            rt.position = host.rectTransform.position + rt.up * ((geometry.OuterRadiusPixels + 9f) * rt.lossyScale.y);
            rt.sizeDelta = new Vector2(120f, 18f);
            if (label.color != color) label.color = color;
            if (!ReferenceEquals(label.text, text)) label.text = text;
        }

        private static Vector2 ScreenPoint(RectTransform transform, Vector2 point, Camera camera) =>
            RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(point));

        // Cancel nonuniform icon scale before the children cancel heading; doing both on a single
        // transform leaves shear. The native map and relief can change both after Apply's postfix.
        private static void CancelIconScale(Image host, RectTransform layer)
        {
            layer.anchoredPosition = Vector2.zero;
            Vector3 scale = host.transform.localScale;
            layer.localScale = new Vector3(1f / Mathf.Max(.0001f, scale.x),
                1f / Mathf.Max(.0001f, scale.y), 1f / Mathf.Max(.0001f, scale.z));
        }

        internal static void RefreshPose(Image host)
        {
            if (host == null || !slots.TryGetValue(host, out Slots known) || known.Layer == null) return;
            CancelIconScale(host, known.Layer);
            Canvas canvas = host.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Rect rect = host.rectTransform.rect;
            Vector2 origin = ScreenPoint(host.rectTransform, rect.min, camera);
            WingMapBadgeGeometry geometry = WingMapBadgeGeometry.ForIcon(
                Vector2.Distance(origin, ScreenPoint(host.rectTransform, new Vector2(rect.xMax, rect.yMin), camera)),
                Vector2.Distance(origin, ScreenPoint(host.rectTransform, new Vector2(rect.xMin, rect.yMax), camera)));
            if (known.Ring != null && known.Ring.gameObject.activeSelf)
            {
                AlignToScreen(known.Ring.rectTransform, canvas, camera);
                known.Ring.SetGeometry(geometry);
            }
            if (known.Selection != null && known.Selection.gameObject.activeSelf)
            {
                AlignToScreen(known.Selection.rectTransform, canvas, camera);
                known.Selection.rectTransform.sizeDelta = Vector2.one * (geometry.CommandHalfExtentPixels * 2f);
            }
            if (known.Status != null && known.Status.gameObject.activeSelf)
            {
                RectTransform rt = known.Status.rectTransform;
                AlignToScreen(rt, canvas, camera);
                rt.position = host.rectTransform.position + rt.up * ((geometry.OuterRadiusPixels + 9f) * rt.lossyScale.y);
            }
        }

        /// <summary>Centre badge geometry on the icon in screen-pixel coordinates.</summary>
        private static void AlignToScreen(RectTransform badge, Canvas canvas, Camera camera)
        {
            badge.anchorMin = badge.anchorMax = new Vector2(0.5f, 0.5f);
            badge.pivot = new Vector2(0.5f, 0.5f);
            badge.anchoredPosition = Vector2.zero;
            badge.localScale = Vector3.one;
            badge.rotation = canvas != null ? canvas.transform.rotation : Quaternion.identity;

            Vector2 origin = ScreenPoint(badge, Vector2.zero, camera);
            float scaleX = Vector2.Distance(origin, ScreenPoint(badge, Vector2.right, camera));
            float scaleY = Vector2.Distance(origin, ScreenPoint(badge, Vector2.up, camera));
            badge.localScale = new Vector3(1f / Mathf.Max(0.0001f, scaleX),
                                          1f / Mathf.Max(0.0001f, scaleY), 1f);
        }

        private static void ApplyCommandSelection(Image host, bool selected, Color color,
            WingMapBadgeGeometry geometry, Canvas canvas, Camera camera)
        {
            Slots known = SlotsOf(host);
            Image badge = known.Selection;
            if (!selected)
            {
                if (badge != null) badge.gameObject.SetActive(false);
                return;
            }

            if (badge == null)
            {
                var go = new GameObject(SelectionObjectName, typeof(RectTransform), typeof(Image));
                badge = go.GetComponent<Image>();
                badge.rectTransform.SetParent(known.Layer, worldPositionStays: false);
                badge.sprite = IconFactory.Get("selection");
                badge.preserveAspect = true;
                known.Selection = badge;
            }

            AlignToScreen(badge.rectTransform, canvas, camera);
            badge.rectTransform.sizeDelta = Vector2.one * (geometry.CommandHalfExtentPixels * 2f);
            badge.raycastTarget = false;
            badge.gameObject.SetActive(true);

            badge.color = new Color(Mathf.Clamp01(color.r + 0.65f),
                Mathf.Clamp01(color.g + 0.65f), Mathf.Clamp01(color.b + 0.65f), 1f);
        }
    }

    /// <summary>Size against the final map pose, after native refresh, symbol fitting and relief projection.</summary>
    internal sealed class WingMapBadgePose : MonoBehaviour
    {
        internal Image Host;
        private void OnEnable() => Canvas.preWillRenderCanvases += Refresh;
        private void OnDisable() => Canvas.preWillRenderCanvases -= Refresh;
        private void Refresh() => WingMarkerBadge.RefreshPose(Host);
    }

    /// <summary>Unfilled UI ring with feathered edges, avoiding overlapping silhouette copies from Unity
    /// Outline.</summary>
    internal sealed class WingMapRingGraphic : MaskableGraphic
    {
        private WingMapBadgeGeometry geometry;

        public void SetGeometry(WingMapBadgeGeometry next)
        {
            if (Mathf.Abs(geometry.InnerRadiusPixels - next.InnerRadiusPixels) < 0.05f) return;
            geometry = next;
            rectTransform.sizeDelta = Vector2.one *
                ((geometry.OuterRadiusPixels + WingMapBadgeGeometry.FeatherPixels) * 2f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (geometry.InnerRadiusPixels <= 0f) return;
            const int segments = 64;
            Color opaque = color;
            Color transparent = new Color(color.r, color.g, color.b, 0f);
            Vector2 center = rectTransform.rect.center;

            // Build transparent-to-solid edge bands around a 1 px stroke; leave the aircraft interior
            // unfilled.
            for (int row = 0; row < 4; row++)
            {
                float radius = row == 0 ? geometry.InnerRadiusPixels - WingMapBadgeGeometry.FeatherPixels
                    : row == 1 ? geometry.InnerRadiusPixels
                    : row == 2 ? geometry.OuterRadiusPixels
                    : geometry.OuterRadiusPixels + WingMapBadgeGeometry.FeatherPixels;
                Color vertexColor = row == 0 || row == 3 ? transparent : opaque;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = 2f * Mathf.PI * i / segments;
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                        vertexColor, Vector2.zero);
                    if (row == 0 || i == 0) continue;
                    int outer = row * (segments + 1) + i;
                    int inner = outer - segments - 1;
                    vh.AddTriangle(inner - 1, inner, outer);
                    vh.AddTriangle(inner - 1, outer, outer - 1);
                }
            }
        }
    }
}
