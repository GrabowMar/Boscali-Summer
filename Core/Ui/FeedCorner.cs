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

        /// <summary>Every card in the column is this wide (canvas units at 1080p) with a header this tall over a 16:9 picture.</summary>
        public const float Width = 264f, Header = 18f;

        /// <summary>
        /// The column's one chrome, in the HUD's own ink, y up (2026-10-20 rework, user: "still fugly"): a header rule like the TAC BOARD's
        /// section titles (hairline under the words, end ticks rising at both ends), a faint frame round the picture, sharp corner ticks on it.
        /// No plate: the cards sit on the HUD like its other line art.
        /// </summary>
        public static void Frame(AvQuadBuffer b, float left, float bottom, float right, float top, Rgba ink)
        {
            float rule = top - Header, tick = Header * 0.4f, arm = 8f;
            AvStrokes.Line(b, left, rule, right, rule, 1f, ink.WithAlpha(0.7f));
            AvStrokes.Line(b, left, rule, left, rule + tick, 1.5f, ink);
            AvStrokes.Line(b, right, rule, right, rule + tick, 1.5f, ink);
            Rgba faint = ink.WithAlpha(0.3f);
            AvStrokes.Line(b, left, bottom, right, bottom, 1f, faint);
            AvStrokes.Line(b, left, bottom, left, rule, 1f, faint);
            AvStrokes.Line(b, right, bottom, right, rule, 1f, faint);
            AvStrokes.Line(b, left, rule, left + arm, rule, 1.5f, ink);
            AvStrokes.Line(b, left, rule, left, rule - arm, 1.5f, ink);
            AvStrokes.Line(b, right, rule, right - arm, rule, 1.5f, ink);
            AvStrokes.Line(b, right, rule, right, rule - arm, 1.5f, ink);
            AvStrokes.Line(b, left, bottom, left + arm, bottom, 1.5f, ink);
            AvStrokes.Line(b, left, bottom, left, bottom + arm, 1.5f, ink);
            AvStrokes.Line(b, right, bottom, right - arm, bottom, 1.5f, ink);
            AvStrokes.Line(b, right, bottom, right, bottom + arm, 1.5f, ink);
        }
    }
}
