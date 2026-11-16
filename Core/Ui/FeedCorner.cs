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

        /// <summary>The shelf's lead rule, the gap round the name, and where the state word's right edge stands (all from the card's left or right edge).</summary>
        public const float ShelfLead = 10f, ShelfGap = 4f, ShelfText = ShelfLead + ShelfGap, StateInset = 8f;

        /// <summary>Where a card's picture ends and its data strip starts, y up, for a card whose foot is at <paramref name="bottom"/>.</summary>
        public static float PictureBottom(float bottom) => bottom + Strip;

        /// <summary>
        /// The cards' one chrome (2026-11-16 visor look), in the HUD's own ink, y up: a caption shelf in the board's grammar (a short lead rule, the name, a long rule, the state word at the right, an end tick),
        /// the picture in a faint frame with sharp corner ticks, and room for the data strip under it. No plate: the cards sit on the HUD like its other line art.
        /// </summary>
        /// <param name="nameW">Width of the name word on the shelf (the caller measures its text; the rule starts after it).</param>
        /// <param name="stateW">Width of the state word at the right (0 = none: the rule runs to the end tick).</param>
        public static void Frame(AvQuadBuffer b, float left, float bottom, float right, float top, Rgba ink, float nameW, float stateW)
        {
            float y = top - Header * 0.5f + 1.5f, picTop = top - Header, picBottom = PictureBottom(bottom), arm = 8f;
            Rgba rule = ink.WithAlpha(0.85f);
            AvStrokes.Line(b, left, y, left + ShelfLead, y, 1f, rule);
            float from = left + ShelfText + nameW + ShelfGap, to = stateW > 0f ? right - StateInset - stateW - ShelfGap : right;
            if (to > from) AvStrokes.Line(b, from, y, to, y, 1f, rule);
            AvStrokes.Line(b, right, y - 4f, right, y + 4f, 1.5f, ink);

            Rgba faint = ink.WithAlpha(0.4f);
            AvStrokes.Line(b, left, picBottom, right, picBottom, 1f, faint);
            AvStrokes.Line(b, left, picTop, right, picTop, 1f, faint);
            AvStrokes.Line(b, left, picBottom, left, picTop, 1f, faint);
            AvStrokes.Line(b, right, picBottom, right, picTop, 1f, faint);
            AvStrokes.Line(b, left, picTop, left + arm, picTop, 1.5f, ink);
            AvStrokes.Line(b, left, picTop, left, picTop - arm, 1.5f, ink);
            AvStrokes.Line(b, right, picTop, right - arm, picTop, 1.5f, ink);
            AvStrokes.Line(b, right, picTop, right, picTop - arm, 1.5f, ink);
            AvStrokes.Line(b, left, picBottom, left + arm, picBottom, 1.5f, ink);
            AvStrokes.Line(b, left, picBottom, left, picBottom + arm, 1.5f, ink);
            AvStrokes.Line(b, right, picBottom, right - arm, picBottom, 1.5f, ink);
            AvStrokes.Line(b, right, picBottom, right, picBottom + arm, 1.5f, ink);
        }

        /// <summary>The data strip's text size for a card <paramref name="width"/> wide: 12 pt at the full 264, smaller on a narrower card so three cells still fit.</summary>
        public static float StripPt(float width) => Mathf.Clamp(width / 21.5f, 8f, 12f);

        /// <summary>The rich-text lead of a strip cell: the label dimmed ("RNG "), the value after it full. Pair with <see cref="CellTail"/>.</summary>
        public const string CellHead = "<alpha=#E0>", CellTail = "<alpha=#FF> ";
    }
}
