using BoscaliSummer.Core.Contracts;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The bottom-right column of video cards, shared by two modules on two canvases (2026-10-20, user: "uav on bottom, missile and target on top"):
    /// the VANGUARD UAV card holds the bottom and publishes where it is; the HUD's target and missile cards stack on it. Screen pixels.
    /// </summary>
    internal static class FeedCorner
    {
        /// <summary>The UAV card's screen rectangle (pixels, origin bottom left) while it is up; zero size when it is not. Written every frame by its owner.</summary>
        public static Rect Uav;

        /// <summary>Every card in the column is this wide (canvas units at 1080p). From the top: a caption shelf <see cref="Header"/> tall, the picture, a one-line data strip <see cref="Strip"/> tall.</summary>
        public const float Width = 264f, Header = 18f, Strip = 16f;

        /// <summary>Where the shelf's caption starts and the state word's right edge stands (from the card's left and right edges): flush, the rule below them is the picture's top edge.</summary>
        public const float ShelfText = 0f, StateInset = 0f;

        /// <summary>Where a card's picture ends and its data strip starts, y up, for a card whose foot is at <paramref name="bottom"/>.</summary>
        public static float PictureBottom(float bottom) => bottom + Strip;

        /// <summary>
        /// The cards' one chrome (2026-11-17 refit), in the HUD's own ink, y up: the caption shelf (a Dim caption at the left, the state word at the right) over ONE rule across the picture's top edge, and 6-long hairline
        /// brackets round the picture (the top corners drop from the rule, the bottom corners have both arms), with room under it for the data strip. No plate, no box: the cards sit on the HUD like its other line art.
        /// </summary>
        public static void Frame(AvQuadBuffer b, float left, float bottom, float right, float top, Rgba ink)
        {
            float picTop = top - Header;      // one rule over the picture, no corner arms (user 2026-11-17)
            AvStrokes.Line(b, left, picTop, right, picTop, HudStroke.Hairline, ink.WithAlpha(HudInk.LineMid));
        }

        /// <summary>The data strip's text size for a card <paramref name="width"/> wide: 12 pt at the full 264, smaller on a narrower card so three cells still fit.</summary>
        public static float StripPt(float width) => HudType.Label;

        /// <summary>The rich-text lead of a strip cell: the label dimmed ("RNG "), the value after it full. Pair with <see cref="CellTail"/>.</summary>
        public const string CellHead = "<alpha=#CC>", CellTail = "<alpha=#FF> ";
    }
}
