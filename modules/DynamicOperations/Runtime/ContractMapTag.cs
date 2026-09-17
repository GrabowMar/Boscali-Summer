using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// One contract's tag on the tactical map, styled entirely from the game's own objective
    /// marker: the family's vanilla icon, a legacy label in the vanilla map font and, when the
    /// contract owns an area, the vanilla radius-ring idiom. The tag root is counter-scaled
    /// against the map zoom so the icon and label keep their screen size, while the ring is
    /// scaled back with the map so it hugs the ground the contract covers.
    /// </summary>
    internal sealed class ContractMapTag
    {
        private const float LabelWidth = 400f;
        private const float LabelHeight = 24f;

        private readonly RectTransform root;
        private readonly Image icon;
        private readonly Text label;
        private readonly Image ring;

        public ContractMapTag(RectTransform parent, string name, in VanillaHudStyle.MapStyle style, Sprite ringSprite)
        {
            root = Rect(parent, name, Vector2.zero);
            ring = Picture(root, "Area Ring", ringSprite);
            icon = Picture(root, "Icon", null);
            label = Caption(root, "Label", style);
            SetRing(false, 0f, Color.clear);
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        /// <summary>Where the tag sits, in map-local units, and the zoom it counter-scales against.</summary>
        public void Place(float x, float y, float inverseZoom, float zoom)
        {
            root.anchoredPosition = new Vector2(x, y);
            float scale = OperationMarkerCopy.Finite(inverseZoom) && inverseZoom > 0f ? inverseZoom : 1f;
            root.localScale = new Vector3(scale, scale, 1f);
            float map = OperationMarkerCopy.Finite(zoom) && zoom > 0f ? zoom : 1f;
            ring.rectTransform.localScale = new Vector3(map, map, 1f);
        }

        /// <summary>The icon and label a marker shows.</summary>
        public void Apply(in ContractCard card, in VanillaHudStyle.MapStyle style, Color labelColour)
        {
            MarkerIconKind kind = card.Icon;
            icon.sprite = Icon(style, kind);
            float size = ContractMarkerLook.IconSize(kind);
            icon.rectTransform.sizeDelta = new Vector2(size, size);
            icon.color = Color.white;
            label.enabled = true;
            if (label.text != card.TitleLine) label.text = card.TitleLine;
            label.rectTransform.anchoredPosition = new Vector2(0f, size);
            label.color = labelColour;
        }

        /// <summary>The area ring in map-local diameter, or hidden when the area is too small to read.</summary>
        public void SetRing(bool visible, float diameter, Color colour)
        {
            if (ring.gameObject.activeSelf != visible) ring.gameObject.SetActive(visible);
            if (!visible) return;
            ring.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            ring.color = colour;
        }

        private static Sprite Icon(in VanillaHudStyle.MapStyle style, MarkerIconKind kind) => kind switch
        {
            MarkerIconKind.Destroy => style.DestroyIcon,
            MarkerIconKind.Recon => style.ReconIcon,
            MarkerIconKind.Capture => style.CaptureIcon,
            _ => style.WaypointIcon
        };

        private static RectTransform Rect(RectTransform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return rect;
        }

        private static Image Picture(RectTransform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static Text Caption(RectTransform parent, string name, in VanillaHudStyle.MapStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(LabelWidth, LabelHeight);
            Text text = go.GetComponent<Text>();
            text.font = style.LabelFont;
            if (style.LabelMaterial != null) text.material = style.LabelMaterial;
            text.fontSize = Mathf.RoundToInt(style.LabelSize);
            text.color = style.LabelColour;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
