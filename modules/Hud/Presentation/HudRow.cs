using BoscaliSummer.Framework.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Hud.Presentation
{
    /// <summary>
    /// Everything a row needs that comes from the game rather than from the feature: the HUD
    /// font and its material, the vanilla label ink, the live theme palette, and the pilot's own
    /// overlay text size. Resolved once per scene, so a custom theme and a changed text size
    /// both carry over.
    /// </summary>
    internal struct HudStyle
    {
        public TMP_FontAsset Font;
        public Material FontMaterial;

        /// <summary>Vanilla's own label ink. A routine line wears this rather than a colour.</summary>
        public Color Ink;

        public Color AllClear;
        public Color Warning;
        public Color Alert;

        /// <summary>Vanilla's own objective label size, before the pilot's size step.</summary>
        public float TextSize;

        /// <summary>
        /// The key line's ink. Only a condition worth acting on is coloured, so the cockpit is
        /// not four saturated blocks at once and a colour always means something.
        /// </summary>
        public Color PrimaryColour(HudTone tone) =>
            tone == HudTone.Warning ? Alert : tone == HudTone.Caution ? Warning : Ink;
    }

    /// <summary>
    /// One entry on the common HUD element: a key line, a dim supporting line under it, and an
    /// optional progress bar beneath both. No panel and no rail — the vanilla HUD writes its own
    /// objective text straight onto the sky, and this sits in that same idiom rather than in a
    /// box of its own.
    ///
    /// <para>Children are laid out from the row's top-left at fixed offsets, once, in
    /// <see cref="Layout"/>. A right-edge anchor flips three things and only three: the row's own
    /// pivot, the alignment of the words inside their box, and the fixed-width bar track's side.
    /// The text box itself is inset the same on both sides, so it needs no mirroring at all. The
    /// supporting line and the bar's band are always reserved, so a row never changes height as
    /// its words change and the stack never jumps under the pilot's eye.</para>
    /// </summary>
    internal sealed class HudRow : IHudLine
    {
        private const float PadY = 2f;
        private const float Gap = 8f;
        private const float BarHeight = 3f;
        private const float BarGap = 4f;
        private const float BarTrackWidth = 160f;
        private const float BarBackAlpha = 0.5f;
        private const float LineSpacing = 1.15f;
        private const float DetailScale = 0.85f;
        private const float DetailAlpha = 0.85f;

        private readonly RectTransform rect;
        private readonly TMP_Text primary;
        private readonly TMP_Text detail;
        private readonly Image barBack;
        private readonly Image barFill;
        private readonly bool notice;

        private float textWidth;
        private float lineHeight;
        private float anchorX;

        private HudTone tone = HudTone.Info;
        private string pendingText = "";
        private string pendingDetail = "";
        private float pendingBar;
        private float paintedBar = -1f;
        private Color paintedPrimary = Color.clear;
        private Color paintedDetail = Color.clear;

        internal HudRow(string owner, string channel, string key, RectTransform parent, bool notice = false)
        {
            Owner = owner;
            Channel = channel;
            Key = key;
            this.notice = notice;

            var go = new GameObject("Hud Line", typeof(RectTransform));
            rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            primary = TextChild("Primary");
            detail = TextChild("Detail");
            barBack = ImageChild("Bar Back");
            barBack.color = new Color(0f, 0f, 0f, BarBackAlpha);

            var fillObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            barFill = fillObject.GetComponent<Image>();
            barFill.raycastTarget = false;
            barFill.rectTransform.SetParent(barBack.rectTransform, false);
            barFill.rectTransform.anchoredPosition = Vector2.zero;

            barBack.gameObject.SetActive(false);
            go.SetActive(false);
        }

        public string Owner { get; }
        public string Channel { get; }
        public string Key { get; }

        /// <summary>When the owning feature last refreshed this line.</summary>
        public float Refreshed { get; private set; }

        /// <summary>How loud this line's last reading was, which decides its place in the stack.</summary>
        internal HudTone Tone => tone;

        internal bool Released { get; private set; }

        public void Set(HudTone newTone, string text, string newDetail, float bar)
        {
            Refreshed = Time.unscaledTime;
            Released = false;
            tone = newTone;
            pendingText = text ?? "";
            pendingDetail = newDetail ?? "";
            pendingBar = float.IsNaN(bar) ? 0f : bar < 0f ? 0f : bar > 1f ? 1f : bar;
        }

        public void Release() => Released = true;

        /// <summary>
        /// Row height at this style and scale: two reserved lines plus the bar's own band, so a
        /// row with no bar and a row with one are the same height and the stack never re-pitches.
        /// </summary>
        internal float Height(HudStyle style, float scale)
        {
            float line = style.TextSize * scale * LineSpacing;
            return PadY * 2f + line * 2f + BarGap + BarHeight;
        }

        /// <summary>
        /// Re-measure every column and mirror the whole row if the block hangs off a right edge.
        /// Runs on a scale or anchor change only, never on a tick where just the words changed.
        /// </summary>
        internal void Layout(HudStyle style, float scale, bool rightAligned)
        {
            float size = style.TextSize * scale;
            lineHeight = size * LineSpacing;

            float width = HudLayout.BlockWidth;
            float height = Height(style, scale);
            rect.sizeDelta = new Vector2(width, height);
            anchorX = rightAligned ? 1f : 0f;

            // The text box is inset the same amount on both sides, so it needs no mirroring at
            // all: only the alignment of the words inside it and the row's own pivot change. The
            // bar's track does move, because it is a fixed-width gauge rather than a full line.
            textWidth = width - Gap * 2f;
            float barX = rightAligned ? width - Gap - BarTrackWidth : Gap;

            Fill(primary.rectTransform, Gap, -PadY, textWidth, lineHeight);
            Fill(detail.rectTransform, Gap, -PadY - lineHeight, textWidth, lineHeight);
            Fill(barBack.rectTransform, barX, -(PadY + lineHeight * 2f + BarGap), BarTrackWidth, BarHeight);

            primary.alignment = rightAligned ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            detail.alignment = primary.alignment;

            SetText(primary, style, size);
            SetText(detail, style, size * DetailScale);

            // A full-width rail for a short gauge is a stray underline, so the track is a fixed
            // short bar at the text box's trailing end, and the fill grows left to right from the
            // track's own start — the same direction as the vanilla capacitor bar right above it.
            float track = textWidth < BarTrackWidth ? textWidth : BarTrackWidth;
            barBack.rectTransform.sizeDelta = new Vector2(track, BarHeight);
            barFill.rectTransform.anchorMin = barFill.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            barFill.rectTransform.anchoredPosition = Vector2.zero;
            barFill.rectTransform.sizeDelta = new Vector2(track * pendingBar, BarHeight);
            paintedBar = pendingBar;

            paintedPrimary = paintedDetail = Color.clear;
        }

        /// <summary>Paint this tick's reading. Only a changed string touches the text mesh.</summary>
        internal void Paint(HudStyle style)
        {
            // A held line states a condition and wears the game's own ink unless it is worth
            // acting on. A notice is tinted throughout, so a still frame can tell "this just
            // happened" from "this is standing status" without reading the words.
            Color primaryColour = notice && tone == HudTone.Info ? style.AllClear : style.PrimaryColour(tone);
            Color detailColour = notice ? primaryColour : style.Ink;
            detailColour.a *= notice ? 0.8f : DetailAlpha;

            if (primary.text != pendingText) primary.text = pendingText;
            if (detail.text != pendingDetail) detail.text = pendingDetail;

            if (paintedPrimary != primaryColour)
            {
                paintedPrimary = primaryColour;
                primary.color = primaryColour;
                barFill.color = primaryColour;
            }
            if (paintedDetail != detailColour)
            {
                paintedDetail = detailColour;
                detail.color = detailColour;
            }

            // The fill is width, not colour, so it has to be re-measured whenever the feature's
            // reading moves. A gauge that only moved on a relayout would never animate.
            if (!Mathf.Approximately(paintedBar, pendingBar))
            {
                paintedBar = pendingBar;
                barFill.rectTransform.sizeDelta = new Vector2(barBack.rectTransform.sizeDelta.x * pendingBar, BarHeight);
            }

            bool showBar = pendingBar > 0.001f;
            if (barBack.gameObject.activeSelf != showBar) barBack.gameObject.SetActive(showBar);
        }

        /// <summary>Hang the row off the block's leading edge, at a whole-pixel offset.</summary>
        internal void Place(float y)
        {
            float x = anchorX;
            rect.anchorMin = rect.anchorMax = new Vector2(x, 1f);
            rect.pivot = new Vector2(x, 1f);
            rect.anchoredPosition = new Vector2(0f, Mathf.Round(y));
        }

        internal void Show(bool show)
        {
            if (rect.gameObject.activeSelf != show) rect.gameObject.SetActive(show);
        }

        internal void Destroy()
        {
            if (rect != null) Object.Destroy(rect.gameObject);
        }

        /// <summary>
        /// Anchor and size a child in one place. Every child here is top-left anchored, so the
        /// caller's offsets read the same whether the row is mirrored or not.
        /// </summary>
        private static void Fill(RectTransform target, float x, float y, float width, float height)
        {
            target.anchorMin = target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = new Vector2(x, y);
            target.sizeDelta = new Vector2(width, height);
        }

        private static void SetText(TMP_Text label, HudStyle style, float size)
        {
            if (label.font != style.Font) label.font = style.Font;
            if (style.FontMaterial != null && label.fontSharedMaterial != style.FontMaterial)
                label.fontSharedMaterial = style.FontMaterial;
            if (!Mathf.Approximately(label.fontSize, size)) label.fontSize = size;
        }

        private Image ImageChild(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.rectTransform.SetParent(rect, false);
            return image;
        }

        private TMP_Text TextChild(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var label = go.GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(rect, false);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = false;
            label.raycastTarget = false;
            return label;
        }
    }
}
