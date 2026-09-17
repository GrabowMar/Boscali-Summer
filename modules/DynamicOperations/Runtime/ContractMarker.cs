using BoscaliSummer.Features.DynamicOperations.Domain;
using BoscaliSummer.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.DynamicOperations.Runtime
{
    /// <summary>
    /// One contract's cockpit marker, drawn by the mod to look like the game's own cockpit
    /// objective overlay: a pointer (or a dot when the target sits near the nose), an area
    /// ring sized and faded with vanilla's formulas, and a two-line label that glides and
    /// nudges aside when it would overlap a neighbour. Every sprite, font, colour and number
    /// is read from the vanilla objects through <see cref="VanillaHudStyle"/>; the widget is
    /// ours, so no vanilla overlay is borrowed, re-parented or modified. One instance per
    /// pool slot, fixed for the scene: contracts rotate through slots, never the other way.
    /// </summary>
    internal sealed class ContractMarker
    {
        private const float LabelOffsetX = 18f;
        private const float LabelWidth = 620f;
        private const float DetailSizeFactor = 0.66f;
        private const float PointerLift = -0.8f;
        private const float MinRingPixels = 16f;
        private const float MaxRingPixels = 2600f;

        private readonly RectTransform root;
        private readonly RectTransform pointer;
        private readonly RectTransform dot;
        private readonly RectTransform ring;
        private readonly RectTransform label;
        private readonly Image pointerImage;
        private readonly Image dotImage;
        private readonly Image ringImage;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI detail;
        private readonly Color colour;

        private string titleCache = string.Empty;
        private string detailCache;
        private int detailCard = -1;
        private int detailMetres = int.MinValue;
        private int detailSeconds = int.MinValue;
        private bool detailMetric;
        private Color detailTone;
        private float textSize;
        private float labelScale;
        private float labelX, labelY;
        private float targetX, targetY;
        private float nudgeX, nudgeY;
        private bool flipped;
        private bool snap;

        internal ContractMarker(RectTransform parent, string name, VanillaHudStyle.CockpitStyle style)
        {
            colour = style.Colour;
            textSize = VanillaHudStyle.OverlayTextSize;

            root = Child(parent, name);
            root.anchoredPosition = Vector2.zero;

            pointer = Child(root, name + " Pointer");
            pointer.anchorMin = pointer.anchorMax = new Vector2(0.5f, 1f);
            pointer.pivot = new Vector2(0.5f, 1f);
            pointer.sizeDelta = style.PointerSize;
            pointer.anchoredPosition = new Vector2(0f, PointerLift);
            pointerImage = pointer.gameObject.AddComponent<Image>();
            pointerImage.sprite = style.Pointer;
            pointerImage.color = colour;
            pointerImage.raycastTarget = false;

            dot = Child(root, name + " Dot");
            dot.sizeDelta = style.DotSize;
            dotImage = dot.gameObject.AddComponent<Image>();
            dotImage.sprite = style.Dot;
            dotImage.color = colour;
            dotImage.raycastTarget = false;

            ring = Child(root, name + " Ring");
            ring.sizeDelta = style.RingSize;
            ringImage = ring.gameObject.AddComponent<Image>();
            ringImage.sprite = style.Ring;
            ringImage.color = colour.WithAlpha(0f);
            ringImage.raycastTarget = false;

            label = Child(root, name + " Label");
            label.pivot = new Vector2(0f, 0.5f);
            label.sizeDelta = new Vector2(LabelWidth, textSize * (1f + DetailSizeFactor));
            label.anchoredPosition = new Vector2(LabelOffsetX, 0f);
            labelScale = style.LabelScale > 0.01f ? style.LabelScale : 0.5f;
            label.localScale = new Vector3(labelScale, labelScale, 1f);
            title = Text(label, name + " Title", style, textSize);
            detail = Text(label, name + " Detail", style, textSize * DetailSizeFactor);
            detail.rectTransform.anchorMin = detail.rectTransform.anchorMax = new Vector2(0f, 0f);
            detail.rectTransform.pivot = new Vector2(0f, 0f);

            labelX = targetX = LabelOffsetX;
            labelY = targetY = 0f;
            SetVisible(false);
        }

        /// <summary>Is this slot currently drawing a contract?</summary>
        internal bool Visible => root.gameObject.activeSelf;


        /// <summary>The label's resting point: vanilla's anti-overlap tests these, not the drifting points.</summary>
        internal Vector2 LabelAnchor => root.anchoredPosition + new Vector2(targetX, targetY);

        internal void SetVisible(bool visible)
        {
            if (root.gameObject.activeSelf == visible) return;
            root.gameObject.SetActive(visible);
            if (!visible) return;
            snap = true;
            nudgeX = 0f;
            nudgeY = 0f;
        }

        /// <summary>The projected point, in canvas space: icons stay glued here.</summary>
        internal void Place(float x, float y) => root.anchoredPosition = new Vector2(x, y);

        /// <summary>
        /// Vanilla's own pointer/dot test: a dot within ten degrees of the nose, a pointer
        /// beyond it, turned along the bearing from the screen centre to the (pre-clamp) point.
        /// </summary>
        internal void SetIcons(Vector3 cameraForward, Vector3 cameraToTarget, float bearingDegrees)
        {
            bool inNose = Vector3.Angle(cameraForward, cameraToTarget) <= ContractMarkerLook.DotAngleDegrees;
            if (dot.gameObject.activeSelf != inNose) dot.gameObject.SetActive(inNose);
            if (pointer.gameObject.activeSelf == inNose) pointer.gameObject.SetActive(!inNose);
            pointer.localRotation = Quaternion.Euler(0f, 0f, bearingDegrees - 90f);
        }

        /// <summary>
        /// The vanilla area ring: full diameter from the angular scale, vanilla's fade, and
        /// roll-locked to the camera. The ring is dropped while the marker is clamped to the
        /// frame edge, exactly like vanilla.
        /// </summary>
        internal void SetRing(float radius, float range, float canvasHeight, Camera camera, bool clamped)
        {
            float alpha = clamped ? 0f : OperationMarkerCopy.RingAlpha(radius, range);
            float pixels = alpha <= 0.0001f
                ? 0f
                : 2f * radius * ContractMarkerMath.PixelsPerMetre(canvasHeight, camera.fieldOfView, range);
            bool show = pixels >= MinRingPixels && pixels <= MaxRingPixels;
            if (ring.gameObject.activeSelf != show) ring.gameObject.SetActive(show);
            if (!show) return;
            ring.sizeDelta = new Vector2(pixels, pixels);
            ringImage.color = colour.WithAlpha(alpha);
            ring.localRotation = Quaternion.Euler(0f, 0f, -camera.transform.eulerAngles.z);
        }

        /// <summary>
        /// Title and detail, cached until they change. The label hangs 18 px to the right of
        /// the point and flips left when it would leave the frame. Text size is the player's
        /// vanilla overlay size; the tone shows only on the detail line, so the icons and the
        /// ring stay vanilla-identical for a located contract.
        /// </summary>
        internal void SetLabel(ContractCard card, float distance, bool metric, float x, float halfWidth,
            bool clamped, Color tone)
        {
            string titleText = card.TitleLine;
            if (titleCache != titleText)
            {
                titleCache = titleText;
                title.text = titleText;
                title.ForceMeshUpdate();
            }

            // The detail line carries a live distance and clock, so it is rebuilt only when one
            // of its inputs actually moved: a metre of range, a second of clock, a new tone or
            // a new contract.
            int metres = Mathf.RoundToInt(distance);
            int seconds = Mathf.CeilToInt(card.Seconds);
            if (detailCache == null || detailCard != card.Id || detailMetric != metric ||
                detailTone != tone || Mathf.Abs(metres - detailMetres) >= 1 || seconds != detailSeconds)
            {
                detailCard = card.Id;
                detailMetric = metric;
                detailTone = tone;
                detailMetres = metres;
                detailSeconds = seconds;
                detailCache = card.Detail(distance, metric);
                detail.text = detailCache;
                detail.ForceMeshUpdate();
            }

            title.color = colour;
            detail.color = tone;

            float width = Mathf.Max(title.preferredWidth, detail.preferredWidth);
            float visual = width * labelScale;
            if (!Mathf.Approximately(width, label.sizeDelta.x))
                label.sizeDelta = new Vector2(width, label.sizeDelta.y);
            bool rightEdge = clamped && x >= halfWidth - 0.5f;
            bool wantFlip = rightEdge || x + LabelOffsetX + visual > halfWidth;
            if (wantFlip != flipped)
            {
                flipped = wantFlip;
                label.pivot = new Vector2(wantFlip ? 1f : 0f, 0.5f);
                snap = true;
            }
            targetX = wantFlip ? -LabelOffsetX : LabelOffsetX;
            targetY = 0f;
        }

        /// <summary>One anti-overlap step against another label: dx,dy = other − this.</summary>
        internal void Nudge(float dx, float dy, float deltaTime) =>
            ContractMarkerLook.Step(ref nudgeX, ref nudgeY, dx, dy, deltaTime);

        /// <summary>The vanilla nudge decay, for a frame with no near neighbour to push against.</summary>
        internal void DecayNudge()
        {
            nudgeX *= ContractMarkerLook.NudgeDecay;
            nudgeY *= ContractMarkerLook.NudgeDecay;
        }

        /// <summary>Glide the label a fifth of the way toward its anchor plus nudge.</summary>
        internal void GlideLabel()
        {
            if (snap)
            {
                labelX = targetX + nudgeX;
                labelY = targetY + nudgeY;
                snap = false;
            }
            else
            {
                labelX = ContractMarkerLook.Glide(labelX, targetX + nudgeX);
                labelY = ContractMarkerLook.Glide(labelY, targetY + nudgeY);
            }
            label.anchoredPosition = new Vector2(labelX, labelY);
        }

        /// <summary>The player changed the overlay text size: follow it live.</summary>
        internal void SetTextSize(float size)
        {
            if (size <= 0f || Mathf.Approximately(size, textSize)) return;
            textSize = size;
            title.fontSize = size;
            detail.fontSize = size * DetailSizeFactor;
            title.rectTransform.sizeDelta = new Vector2(LabelWidth, size);
            detail.rectTransform.sizeDelta = new Vector2(LabelWidth, size * DetailSizeFactor);
            label.sizeDelta = new Vector2(label.sizeDelta.x, size * (1f + DetailSizeFactor));
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, VanillaHudStyle.CockpitStyle style,
            float size)
        {
            RectTransform rect = Child(parent, name);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(LabelWidth, size);
            rect.anchoredPosition = Vector2.zero;
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = style.Font;
            if (style.FontMaterial != null) label.fontSharedMaterial = style.FontMaterial;
            label.fontSize = size;
            label.color = style.Colour;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Child(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }
    }
}
