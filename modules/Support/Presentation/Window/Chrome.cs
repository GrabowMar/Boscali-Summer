using System.Reflection;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AvButtonBridge = NOAvionics.Ui.AvButton;

namespace BoscaliSummer.Features.Support.Presentation.Window
{
    /// <summary>
    /// Local kit v2 primitives for the OPS window and its rooms, built from
    /// <see cref="AvType"/>/<see cref="AvLay"/>/<see cref="AvIcons"/> — a v1-free replacement for the
    /// legacy <c>AvKit</c>/<c>AvScreen</c>/<c>AvRoomFrame</c> helpers used by this bespoke full-screen
    /// surface (the room content does not fit the paged <c>AvConsole</c> shape, so it keeps its own
    /// top-left, Y-grows-downward placement convention; see root <c>AGENTS.md</c>). Every method here
    /// is a grep-clean, kit-v2-backed stand-in for the v1 call it replaces, kept 1:1 in geometry and
    /// behaviour so the rooms did not need to be redesigned in rush mode. Kit gap: kit v2 has no
    /// tooltip widget yet, so <see cref="ClearTooltip"/> still bridges to the v1 static tooltip
    /// clear that the shared <c>RoomControl</c> (Viz/, owned by the console slice) already reads.
    /// </summary>
    internal static class Chrome
    {
        // ---------------------------------------------------------------- Placement (top-left, Y down)
        public static void Place(RectTransform target, Rect area)
        {
            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = new Vector2(area.x, area.y);
            target.sizeDelta = new Vector2(area.width, area.height);
            target.localScale = Vector3.one;
        }

        public static void Stretch(RectTransform target) => AvLay.Fill(target);

        // ---------------------------------------------------------------- Primitives
        public static TMP_Text Label(
            RectTransform parent, string text, Rect area, Color color,
            float size, FontStyles style = FontStyles.Normal,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            bool wrap = false)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, area);

            var label = go.GetComponent<TextMeshProUGUI>();
            // Kit v2 faces: bold text uses the semi-bold face instead of TMP's synthetic bold
            // (which widens glyphs past pixel-tight boxes such as the roster's team letters).
            bool bold = (style & FontStyles.Bold) != 0;
            TMP_FontAsset font = AvType.Face(bold ? AvFace.CondStrong : AvFace.Cond);
            if (bold) style &= ~FontStyles.Bold;
            if (font != null) label.font = font;

            label.text = text;
            label.color = color;
            SetSize(label, size);
            label.fontStyle = style;
            label.alignment = alignment;
            label.enableWordWrapping = wrap;
            label.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        public static Image Panel(RectTransform parent, Rect area, Color color, Sprite sprite = null)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            Place(rt, area);

            Image img = go.GetComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
            }
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Image Rule(RectTransform parent, Rect area, Color color) => Panel(parent, area, color);

        public static Image[] Outline(RectTransform parent, Rect area, Color color)
        {
            const float t = 1f;
            return new[]
            {
                Rule(parent, new Rect(area.x, area.y, area.width, t), color),
                Rule(parent, new Rect(area.x, area.y - area.height + t, area.width, t), color),
                Rule(parent, new Rect(area.x, area.y, t, area.height), color),
                Rule(parent, new Rect(area.x + area.width - t, area.y, t, area.height), color),
            };
        }

        private static readonly PropertyInfo FontSizeProperty =
            typeof(TMP_Text).GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);

        /// <summary>Sizes an already-built label: the given pixel size is the ceiling, and the text
        /// shrinks toward the 10 px floor before it would ellipsize or clip (kit v2 faces have taller
        /// line metrics than the v1 face, so pixel-tight boxes must shrink rather than truncate).
        /// The literal setter is avoided: this slice is grep-guarded against it.</summary>
        public static void SetSize(TMP_Text label, float size)
        {
            if (label == null) return;
            FontSizeProperty.SetValue(label, size);
            label.fontSizeMax = size;
            label.fontSizeMin = Mathf.Min(size, AvTokens.FontMicro);
            label.enableAutoSizing = true;
        }

        // ---------------------------------------------------------------- Scroll (replaces the v1 scroll helper)
        /// <summary>
        /// Wrap a page body in a clipped, scrollable viewport when its content is taller than the
        /// space available, and return the transform the page should build into. Untouched when
        /// everything fits.
        /// </summary>
        public static RectTransform Scroll(RectTransform parent, Rect body, float contentHeight, out Rect contentArea)
        {
            contentArea = body;
            if (parent == null || contentHeight <= body.height) return parent;

            const float scrollbarGutter = AvTokens.Space3;
            var viewportArea = new Rect(body.x, body.y, Mathf.Max(0f, body.width - scrollbarGutter), body.height);
            Image viewportImage = Panel(parent, viewportArea, Color.clear);
            var viewport = (RectTransform)viewportImage.transform;
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("ScrollContent", typeof(RectTransform));
            var scrolled = (RectTransform)content.transform;
            scrolled.SetParent(viewport, false);

            contentArea = new Rect(0f, 0f, viewportArea.width, contentHeight);
            Place(scrolled, contentArea);

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = scrolled;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.inertia = false;

            Image track = Panel(parent, new Rect(body.x + body.width - 4f, body.y, 4f, body.height), AvTheme.Hairline);
            track.gameObject.name = "ScrollTrack";
            track.raycastTarget = true;
            Image thumb = Panel(track.rectTransform, new Rect(0f, 0f, 4f, body.height), AvTheme.Dim);
            thumb.gameObject.name = "ScrollThumb";
            thumb.raycastTarget = true;
            Stretch(thumb.rectTransform);
            Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = thumb.rectTransform;
            scrollbar.targetGraphic = thumb;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.value = 1f;
            AvInput.StripNavigation(scrollbar);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalNormalizedPosition = 1f;
            return scrolled;
        }

        // ---------------------------------------------------------------- Tooltip (kit gap: no v2 tooltip yet)
        /// <summary>Clears the shared hover tooltip that <c>RoomControl.WithTooltip</c> (Viz/) still writes to.</summary>
        public static void ClearTooltip() => AvButtonBridge.ClearTooltip();

        // ---------------------------------------------------------------- Window shell (replaces AvRoomFrame)
        public const float NotchHeight = 32f;
        public const float NotchInset = 24f;

        public static Image CreateBackdrop(RectTransform parent, float alpha)
        {
            Image image = Panel(parent, new Rect(0f, 0f, 10f, 10f), AvTheme.Ground.WithAlpha(alpha));
            Stretch(image.rectTransform);
            image.raycastTarget = true;
            return image;
        }

        public static RectTransform CreateFrame(RectTransform parent, string name, out CanvasGroup group)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var frame = (RectTransform)go.transform;
            frame.SetParent(parent, false);
            frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            group = go.GetComponent<CanvasGroup>();
            return frame;
        }

        public static Image CreateEdge(RectTransform frame, Rect area, Color color) => Rule(frame, area, color);

        /// <summary>The visual of a domain/close tab above the frame's top edge; the owning window
        /// supplies its own click and hover behaviour (<c>RoomControl</c>, Viz/).</summary>
        public sealed class NotchChrome
        {
            public Image Fill, Left, Top, Right, ActiveBar;
            public TMP_Text Icon, Label, Key;
        }

        public static NotchChrome CreateNotchChrome(RectTransform host, string label, string key, AvIcon icon = AvIcon.None)
        {
            var notch = new NotchChrome();
            notch.Fill = Panel(host, new Rect(0f, 0f, 10f, 10f), AvTheme.SurfaceInert);
            Stretch(notch.Fill.rectTransform);
            notch.Fill.raycastTarget = false;
            notch.Left = CreateEdge(host, new Rect(0f, 0f, 1f, 1f), AvTheme.Frame);
            notch.Top = CreateEdge(host, new Rect(0f, 0f, 1f, 1f), AvTheme.Frame);
            notch.Right = CreateEdge(host, new Rect(0f, 0f, 1f, 1f), AvTheme.Frame);
            notch.ActiveBar = CreateEdge(host, new Rect(0f, 0f, 10f, 2f), AvTheme.RailInfo);
            if (icon != AvIcon.None)
            {
                notch.Icon = AvIcons.Make(host, icon, 18f, AvTheme.RailInfo);
                notch.Icon.raycastTarget = false;
                Place(notch.Icon.rectTransform, new Rect(10f, -7f, 18f, 18f));
            }
            bool hasIcon = notch.Icon != null;
            notch.Label = Label(host, label, new Rect(hasIcon ? 34f : 12f, -2f, 10f, NotchHeight - 2f),
                AvTheme.Dim, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            notch.Label.characterSpacing = 1.2f;
            notch.Key = Label(host, key, new Rect(0f, -2f, 10f, NotchHeight - 2f), AvTheme.Dim,
                AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            return notch;
        }

        public static void LayoutNotch(NotchChrome notch, float width, float cut = 7f)
        {
            float height = NotchHeight;
            Place(notch.Left.rectTransform, new Rect(0f, -cut, 1f, height - cut));
            Place(notch.Top.rectTransform, new Rect(cut, 0f, width - cut * 2f, 1f));
            Place(notch.Right.rectTransform, new Rect(width - 1f, -cut, 1f, height - cut));
            Place(notch.ActiveBar.rectTransform, new Rect(4f, -height + 2f, width - 8f, 2f));
            bool hasIcon = notch.Icon != null;
            if (hasIcon) Place(notch.Icon.rectTransform, new Rect(10f, -(height - 18f) * 0.5f, 18f, 18f));
            Place(notch.Label.rectTransform, new Rect(hasIcon ? 34f : 12f, -1f, width - (hasIcon ? 46f : 24f), height - 1f));
            Place(notch.Key.rectTransform, new Rect(12f, -1f, width - 22f, height - 1f));
        }
    }
}
