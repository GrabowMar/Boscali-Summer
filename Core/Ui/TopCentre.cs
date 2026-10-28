using NOAvionics;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The top-centre lane (2026-10-28 review), shared by the ace-hunt HUD (Progression) and the centre WARNING banner (Hud), both drawn under the native HUD canvas: three
    /// FIXED slots one above the other on the reticle's axis, one width, one look. Fixed, not negotiated: nothing in the lane ever moves out of another's way, so nothing can land on
    /// the heading tape (it starts about 232 down in the cockpit, the third-person HDG box about 198). Every length is in reference pixels of a 1080-high screen; the owners scale
    /// their root by screen height / 1080. Pure: the test project links it.
    /// <list type="bullet">
    /// <item>A, the strip (8..40): the ace hunt while it is on.</item>
    /// <item>B, the card (48..120): the ace's dossier for a few seconds when the hunt starts.</item>
    /// <item>C, the banner (128..188): MISSILE, PULL UP, FIRE, ENGINE OUT, LOCKED, BINGO FUEL.</item>
    /// </list>
    /// </summary>
    internal static class TopCentre
    {
        /// <summary>
        /// The lane's width: at 16:9 with the TAC BOARD at its default size it fits between the board and the screen's centre line with <see cref="Gap"/> to spare, so the lane stays
        /// on the reticle's axis (in game a 520 lane had to step 100 left of it). A board drawn bigger or moved pushes it aside (<see cref="LaneShift"/>).
        /// </summary>
        public const float Width = 312f, Gap = 12f;
        public const float StripTop = 8f, StripHeight = 32f, CardTop = 48f, CardHeight = 72f, BannerTop = 128f, BannerHeight = 60f;

        // The look, one set for every piece in the lane and the centre layer: strokes, corner brackets, the hazard chevrons, shade, type sizes.
        public const float Stroke = 1.5f, Arm = 8f, ChevronSize = 12f, ChevronStroke = 2f, ChevronStep = 12f, ShadeMargin = 16f;
        public const float WordPt = 28f, NamePt = 22f, DetailPt = 14f, LabelPt = 12f;
        /// <summary>Letter spacing: the big words wide, the reading lines close.</summary>
        public const float WordSpacing = 10f, LineSpacing = 2f;

        // Motion, one set: in faster than a glance, out faster than in; the onset blink is three blinks at 3 Hz and nothing in the layer flashes faster.
        public const float EnterS = 0.18f, ExitS = 0.12f, TypeS = 0.03f, OnsetS = 1f, BlinkHz = 2f, UrgentHz = 3f;

        /// <summary>Lit for the first 62 % of each cycle at <paramref name="hz"/>: a word blinking at this reads longer than it hides.</summary>
        public static bool Lit(float t, float hz) => t < 0f || t * hz % 1f < 0.62f;

        /// <summary>
        /// How far (reference pixels, + right) a piece of the lane <paramref name="width"/> wide, standing from <paramref name="top"/> down <paramref name="height"/>, moves sideways to keep
        /// <see cref="Gap"/> clear of the TAC BOARD's box (screen pixels, y down from the top), from the screen's size in pixels. The strip and the card ask with the lane's width over
        /// both their slots (<see cref="AceShift"/>), so they always share one centre line; the banner asks with its own width, so a short word never leaves the axis.
        /// </summary>
        public static float LaneShift(float screenW, float screenH, float width, float top, float height, float boardLeft, float boardTop, float boardRight, float boardBottom)
        {
            if (!(screenH > 0f)) return 0f;
            float k = 1080f / screenH;
            return Shift(screenW * k, width, top, height, boardLeft * k, boardTop * k, boardRight * k, boardBottom * k);
        }

        /// <summary>The strip's and the card's one shift (<see cref="LaneShift"/> over slots A and B).</summary>
        public static float AceShift(float screenW, float screenH, float boardLeft, float boardTop, float boardRight, float boardBottom) =>
            LaneShift(screenW, screenH, Width, StripTop, CardTop + CardHeight - StripTop, boardLeft, boardTop, boardRight, boardBottom);

        /// <summary>
        /// How far (to the right, negative to the left) a panel <paramref name="width"/> wide, centred on a screen <paramref name="screenW"/> wide and standing from <paramref name="top"/> down
        /// <paramref name="height"/>, has to move sideways to leave the board's box and <see cref="Gap"/> clear. 0 when it is already clear or there is no board. It goes the shorter way out
        /// that keeps it on the screen (the board is at the right, so that is usually left); where neither does, as far left as the screen allows.
        /// </summary>
        public static float Shift(float screenW, float width, float top, float height, float boardLeft, float boardTop, float boardRight, float boardBottom)
        {
            float left = (screenW - width) * 0.5f, right = left + width;
            if (!(boardRight > boardLeft && boardBottom > boardTop)) return 0f;
            if (right <= boardLeft - Gap || left >= boardRight + Gap || top + height <= boardTop - Gap || top >= boardBottom + Gap) return 0f;
            float toLeft = boardLeft - Gap - right, toRight = boardRight + Gap - left;
            bool leftFits = left + toLeft >= 0f, rightFits = right + toRight <= screenW;
            if (leftFits && rightFits) return -toLeft < toRight ? toLeft : toRight;
            if (leftFits) return toLeft;
            if (rightFits) return toRight;
            return System.Math.Max(toLeft, -left);
        }

        /// <summary>
        /// The lane's warning chrome round a word centred at (<paramref name="x"/>, <paramref name="y"/>) and <paramref name="halfWord"/> half-wide (y up): three chevrons each side pointing
        /// in. <paramref name="step"/> 0..2 is the bright one of a light marching in from the outside (a warning); -1 holds them all at one level (a caution).
        /// </summary>
        public static void Hazard(AvQuadBuffer b, float x, float y, float halfWord, Rgba ink, int step, float size = ChevronSize)
        {
            for (int i = 0; i < 3; i++)
            {
                float d = halfWord + size * 1.5f + i * ChevronStep, a = step < 0 ? 0.75f : i == 2 - step ? 1f : 0.3f;
                AvStrokes.Chevron(b, x - d, y, size, 0f, ChevronStroke, ink.WithAlpha(ink.A * a));
                AvStrokes.Chevron(b, x + d, y, size, 180f, ChevronStroke, ink.WithAlpha(ink.A * a));
            }
        }

        /// <summary>How far out the hazard chevrons reach from the word's centre (<see cref="Hazard"/>), for a bracket round both.</summary>
        public static float HazardReach(float halfWord, float size = ChevronSize) => halfWord + size * 1.5f + 2f * ChevronStep + size * 0.5f;
    }
}
