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
        public ScrollRect Scroll { get; }
        public RectTransform Viewport { get; }

        /// <summary>The single content rect, or null when the owner swaps pages under the viewport itself.</summary>
        public RectTransform Content { get; }

        public Scrollbar Bar { get; }

        public AvScrollView(RectTransform host, bool withContent)
        {
            Scroll = host.gameObject.AddComponent<ScrollRect>();
            Scroll.horizontal = false; Scroll.movementType = ScrollRect.MovementType.Clamped; Scroll.scrollSensitivity = 24f;
            Viewport = AvLay.Child(host, "Viewport");
            Viewport.gameObject.AddComponent<RectMask2D>();
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
            AvLay.Place((RectTransform)Bar.transform, width - AvGridTokens.Pad - AvGridTokens.Gutter + 2f, 2f, 4f, height - 4f);
        }

        public void Restyle()
        {
            Bar.GetComponent<Image>().color = AvStyleHost.FuiFill("scrollbar", AvTheme.Hairline);
            Bar.handleRect.GetComponent<Image>().color = AvStyleHost.FuiFill("scrollbar-thumb", AvTheme.Frame);
        }

        private static Scrollbar MakeScrollbar(RectTransform parent)
        {
            RectTransform bar = AvLay.Child(parent, "Scrollbar");
            var track = bar.gameObject.AddComponent<Image>();
            track.raycastTarget = true;
            RectTransform area = AvLay.Child(bar, "Area"); AvLay.Fill(area);
            RectTransform handle = AvLay.Child(area, "Handle"); AvLay.Fill(handle);
            handle.gameObject.AddComponent<Image>();
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handle;
            sb.targetGraphic = handle.GetComponent<Image>();
            return sb;
        }
    }
}
