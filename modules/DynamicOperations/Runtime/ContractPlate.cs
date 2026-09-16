using BoscaliSummer.Features.DynamicOperations.Domain;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// One contract's marker, drawn by the mod on both the tactical map and the cockpit HUD:
    /// a pointer that turns toward the target, a two-line plate above it, and an area ring of
    /// dots. The ring lives in the map's (or the world's) own scale; the plate counter-scales
    /// so it stays the same size at any zoom.
    /// </summary>
    internal sealed class ContractPlate
    {
        /// <summary>The vanilla avionics rails, chosen by contract state.</summary>
        public static Color ToneColor(MarkerTone tone) => tone switch
        {
            MarkerTone.Ready => AvTheme.RailReady,
            MarkerTone.Caution => AvTheme.RailCaution,
            _ => AvTheme.RailInfo
        };

        private const float PlateWidth = 320f;
        private const float TitleHeight = 16f;
        private const float DetailHeight = 14f;
        private const float PointerSize = 13f;
        private const float DotSize = 3f;
        private const float PlateLift = 15f;

        private readonly RectTransform pin;      // holds the ring, in parent scale
        private readonly RectTransform plate;    // counter-scaled, holds pointer and labels
        private readonly RectTransform pointer;
        private readonly Image pointerImage;
        private readonly TMP_Text title;
        private readonly TMP_Text detail;
        private readonly Image[] dots = new Image[ContractMarkerMath.RingDots];
        private string titleCache = string.Empty;
        private string detailCache = string.Empty;

        public ContractPlate(RectTransform parent, string name)
        {
            pin = Centred(parent, name, 1f, 1f, 0f);
            plate = Centred(pin, name + " Plate", PlateWidth, TitleHeight + DetailHeight + PlateLift, 0f);
            pointer = Centred(plate, name + " Pointer", PointerSize, PointerSize, 0f);
            pointerImage = pointer.gameObject.AddComponent<Image>();
            pointerImage.sprite = null;
            pointerImage.raycastTarget = false;
            title = Labelled(plate, name + " Title", PlateLift + TitleHeight, TitleHeight,
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold);
            detail = Labelled(plate, name + " Detail", PlateLift, DetailHeight,
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Normal);
            for (int i = 0; i < dots.Length; i++)
            {
                dots[i] = AvKit.Panel(pin, new Rect(0f, 0f, DotSize, DotSize), AvTheme.RailInfo);
                dots[i].gameObject.name = name + " Ring";
                dots[i].raycastTarget = false;
                RectTransform dot = dots[i].rectTransform;
                dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
                dot.pivot = new Vector2(0.5f, 0.5f);
                dot.sizeDelta = new Vector2(DotSize, DotSize);
            }
            SetRing(false, 0f);
        }

        /// <summary>Where the contract sits, in the parent's own coordinates.</summary>
        public void SetPosition(float x, float y)
        {
            pin.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>1 for a screen overlay, 1/zoom on the map so the plate keeps its size.</summary>
        public void SetScreenScale(float scale)
        {
            float safe = OperationMarkerCopy.Finite(scale) && scale > 0f ? scale : 1f;
            plate.localScale = new Vector3(safe, safe, 1f);
        }

        public void SetVisible(bool visible)
        {
            if (pin.gameObject.activeSelf != visible) pin.gameObject.SetActive(visible);
        }

        public void SetRing(bool visible, float radiusPixels)
        {
            bool draw = visible && OperationMarkerCopy.Finite(radiusPixels) && radiusPixels >= DotSize;
            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] == null) continue;
                if (dots[i].gameObject.activeSelf != draw) dots[i].gameObject.SetActive(draw);
                if (!draw) continue;
                dots[i].rectTransform.anchoredPosition =
                    new Vector2(ContractMarkerMath.DotX(radiusPixels, i), ContractMarkerMath.DotY(radiusPixels, i));
            }
        }

        /// <summary>Points the marker's corner along the bearing to the target.</summary>
        public void SetBearing(float degrees)
        {
            pointer.localRotation = Quaternion.Euler(0f, 0f, degrees - 45f);
        }

        public void SetPointerActive(bool active)
        {
            if (pointer.gameObject.activeSelf != active) pointer.gameObject.SetActive(active);
        }

        public void Refresh(ContractCard card, float distance, MarkerTone tone)
        {
            Color rail = ToneColor(tone);
            pointerImage.color = rail;
            detail.color = rail;
            title.color = AvTheme.TextPrimary;
            for (int i = 0; i < dots.Length; i++)
                if (dots[i] != null) dots[i].color = rail.WithAlpha(0.55f);

            Set(title, ref titleCache, card.TitleLine);
            Set(detail, ref detailCache, card.Detail(distance));
        }

        private static void Set(TMP_Text label, ref string cache, string text)
        {
            if (label == null || cache == text) return;
            cache = text;
            label.text = text;
        }

        private static RectTransform Centred(RectTransform parent, string name, float width, float height, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0f, y);
            return rect;
        }

        private static TMP_Text Labelled(RectTransform parent, string name, float y, float height,
            Color color, float size, FontStyles style)
        {
            TMP_Text label = AvKit.Label(parent, string.Empty, new Rect(0f, y, PlateWidth, height),
                color, size, style, TextAlignmentOptions.Center);
            label.gameObject.name = name;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PlateWidth, height);
            rect.anchoredPosition = new Vector2(0f, y);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }
    }
}
