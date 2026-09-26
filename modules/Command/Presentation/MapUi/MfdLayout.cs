using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Where the three columns of the maximised map screen begin and end.
    /// Thin adapter over <see cref="AvGrid"/>.
    ///
    /// Panels on the left, the map in the centre, and one thin rail on the right holding
    /// every button. One authority means they cannot disagree.
    ///
    /// All rectangles are in the maximised map canvas's own space: origin at the canvas
    /// centre, +Y up, which is what <c>RectTransform.anchoredPosition</c> wants when anchor
    /// and pivot are both centred.
    /// </summary>
    internal static class MfdLayout
    {
        /// <summary>
        /// Width of the button rail. Wide enough for a glyph, the game's short code and the
        /// descriptor that says what the code means; the map gives up 54px for a column a
        /// new player can actually read.
        /// </summary>
        public const float RailWidth = 150f;
        public const float Gutter = 8f;
        public const float MapInset = 0f;
        public const float Margin = 8f;

        /// <summary>
        /// Height kept clear for the game's own spawn and spectator controls. Wide screens
        /// put the instrument and context surfaces on one row; narrow screens retain two
        /// rows so native buttons keep their usable width.
        /// </summary>
        public const float BottomReserve = 120f;
        private const float WideBottomReserve = 72f;

        /// <summary>
        /// Height kept clear at the top for the mission clock and the kill / chat feed,
        /// which the game paints in the top-left. With no reserve the stock panels docked
        /// in the left column had their first row sitting under that feed and clipped by
        /// the canvas edge.
        /// </summary>
        public const float TopReserve = 26f;

        /// <summary>The three columns, resolved against a canvas of this size.</summary>
        internal struct Columns
        {
            /// <summary>Where a panel goes.</summary>
            public Rect Panel;

            /// <summary>What is left for the map, between the panel column and the rail.</summary>
            public Rect Map;

            /// <summary>The button rail, against the right edge.</summary>
            public Rect Rail;

            /// <summary>Canvas size the columns were resolved against.</summary>
            public Vector2 Canvas;
        }

        /// <summary>Resolve the columns for a canvas using AvGrid geometry authority.</summary>
        public static Columns Resolve(Vector2 canvasSize, float panelWidth = AvTokens.PanelWidth)
        {
            var spec = AvGridSpec.Default;
            spec.Gutter = Gutter;
            spec.Margin = Margin;
            spec.TopReserve = TopReserve;
            spec.BottomReserve = canvasSize.x >= 1600f ? WideBottomReserve : BottomReserve;
            spec.MapInset = MapInset;

            AvRegions regions = AvGrid.Resolve(canvasSize.x, canvasSize.y, panelWidth, RailWidth, spec);

            return new Columns
            {
                Panel = ToUnityRect(regions.Panel),
                Map = ToUnityRect(regions.Map),
                Rail = ToUnityRect(regions.Rail),
                Canvas = canvasSize,
            };
        }

        /// <summary>
        /// The UI area the layout divides up.
        ///
        /// <para><b>Why not <c>canvas.rect</c>.</b> <c>DynamicMap.maximizedMapCanvas</c> is a
        /// screen-space canvas whose <c>RectTransform</c> only takes its new size on the canvas
        /// update after the GameObject is activated; on the first open of a mission it can
        /// still report the size it had the last time it was enabled, and it keeps that value
        /// for as long as the map stays open. The CanvasScaler, however, stamps
        /// <c>canvas.scaleFactor</c> from the live screen as soon as it is enabled, and a
        /// screen-space canvas is exactly <c>Screen / scaleFactor</c> units across. Resolving
        /// from that is the size Unity is about to apply and never inherits a stale rect.
        /// World-space canvases keep their authored rect.</para>
        /// </summary>
        public static Vector2 CanvasSize(Canvas canvas)
        {
            if (canvas == null) return Vector2.zero;

            Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            if (root.renderMode == RenderMode.ScreenSpaceOverlay && root.scaleFactor > 0f &&
                Screen.width > 1 && Screen.height > 1)
            {
                return new Vector2(Screen.width, Screen.height) / root.scaleFactor;
            }

            Vector2 size = RectSize(root);
            if (size.x <= 1f || size.y <= 1f) size = RectSize(canvas);
            return size;
        }

        private static Vector2 RectSize(Canvas canvas)
        {
            if (canvas == null) return Vector2.zero;
            var rt = canvas.transform as RectTransform;
            return rt == null ? Vector2.zero : rt.rect.size;
        }

        /// <summary>Resolve against a live canvas, or report failure if there is not one yet.</summary>
        public static bool TryResolve(Canvas canvas, out Columns columns, float panelWidth = AvTokens.PanelWidth)
        {
            columns = default;
            if (canvas == null) return false;

            Vector2 size = CanvasSize(canvas);
            if (size.x <= 1f || size.y <= 1f) return false;

            columns = Resolve(size, panelWidth);
            return true;
        }

        private static Rect ToUnityRect(AvRect r) => new Rect(r.X, r.Y, r.Width, r.Height);

        /// <summary>
        /// The centre of a column, as an <c>anchoredPosition</c> for a child of the canvas
        /// whose anchor and pivot are centred.
        /// </summary>
        public static Vector2 CentreOf(Rect column) =>
            new Vector2(column.x + column.width * 0.5f, column.y - column.height * 0.5f);

        /// <summary>
        /// The top-left corner of a column, as an <c>anchoredPosition</c> for a child of the
        /// canvas whose anchors are centred and whose <b>pivot is its own top-left</b>.
        /// </summary>
        public static Vector2 TopLeftOf(Rect column) => new Vector2(column.x, column.y);
    }
}

