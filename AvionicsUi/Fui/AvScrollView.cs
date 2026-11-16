using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// The scroll scaffolding behind the kit's scrolling surfaces: a vertical-only <see cref="UnityEngine.UI.ScrollRect"/> on a host rect,
    /// a masked viewport, an optional content rect, and the kit scrollbar (auto-hiding, no keyboard navigation) at the right edge.
    /// </summary>
    public sealed class AvScrollView
    {
        private Image track, thumb;
        public ScrollRect Scroll { get; }
        public RectTransform Viewport { get; }

        /// <summary>The single content rect, or null when the owner swaps pages under the viewport itself.</summary>
        public RectTransform Content { get; }

        public Scrollbar Bar { get; }

        public AvScrollView(RectTransform host, bool withContent)
        {
            Scroll = host.gameObject.AddComponent<AvSmoothScroll>();
            Scroll.horizontal = false; Scroll.movementType = ScrollRect.MovementType.Clamped; Scroll.scrollSensitivity = AvSmoothScroll.Step;
            Viewport = AvLay.Child(host, "Viewport");
            Viewport.gameObject.AddComponent<RectMask2D>();
            Viewport.gameObject.AddComponent<Image>().color = Color.clear;   // wheel hits the gaps between rows too
            if (withContent)
            {
                Content = AvLay.Child(Viewport, "Content");
                Scroll.content = Content;
            }
            Scroll.viewport = Viewport;
            Bar = MakeScrollbar(host);
            Scroll.verticalScrollbar = Bar;
            Scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            AvInput.StripNavigation(Bar);
        }

        /// <summary>Sizes the viewport to the host's <paramref name="width"/> x <paramref name="height"/> and seats the scrollbar at its right edge.</summary>
        public void Place(float width, float height)
        {
            AvLay.Place(Viewport, 0f, 0f, width, height);
            // Overlay the panel edge: no blank column is deducted from the flow. The wide invisible
            // target overlaps only the outer few pixels of a card, outside its text/control padding.
            AvLay.Place((RectTransform)Bar.transform, width - 12f, 2f, 12f, Mathf.Max(0f, height - 4f));
        }

        public void Restyle()
        {
            track.color = AvStyleHost.FuiFill("scrollbar", AvTheme.Hairline).WithAlpha(.12f);
            thumb.color = AvStyleHost.FuiFill("scrollbar-thumb", AvTheme.Frame);
        }

        private Scrollbar MakeScrollbar(RectTransform parent)
        {
            RectTransform bar = AvLay.Child(parent, "Scrollbar");
            var hit = bar.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            track = Rail(bar, "Track");
            RectTransform area = AvLay.Child(bar, "Area"); AvLay.Fill(area);
            RectTransform handle = AvLay.Child(area, "Handle"); AvLay.Fill(handle);
            thumb = Rail(handle, "Thumb");
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handle;
            sb.targetGraphic = thumb;
            return sb;
        }

        private static Image Rail(RectTransform parent, string name)
        {
            Image image = AvLay.Solid(parent, name, Color.clear);
            RectTransform r = image.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(3f, 0f); r.anchoredPosition = new Vector2(7f, 0f);
            return image;
        }
    }
}
