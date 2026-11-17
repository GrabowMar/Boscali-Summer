using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The top-centre lane (2026-11-16, concept A): ONE slot in the gap above the game's heading tape, shared by the slim WARNING line (Hud: MISSILE, PULL UP, FIRE, ENGINE OUT, LOCKED, BINGO FUEL) and the ace's
    /// two-row card (Progression, for a few seconds when a hunt starts, only while no warning line is up). A single row with a class bar (no wash) (the warning line) or inside four small corner ticks (the ace card): no box, no plate. Both are drawn under the native HUD canvas
    /// on the reticle's axis in reference pixels of a 1080-high screen (the owners scale their root by screen height / 1080), and both step sideways, never up or down, to keep clear of the TAC BOARD's box
    /// (<see cref="LaneShift"/>). The threat bearings are not here: they hang on the tape itself. Pure: the test project links it.
    /// </summary>
    internal static class TopCentre
    {
        /// <summary>The lane's width: at 16:9 with the TAC BOARD at its default size it fits between the board and the screen's centre line with <see cref="Gap"/> to spare. A line with long text grows to <see cref="MaxWidth"/> and steps aside.</summary>
        public const float Width = 312f, MaxWidth = 400f, Gap = 12f;
        /// <summary>The slot: the ticks' top (reference y, down from the screen's top), the warning line's box height and the ace card's. The heading tape's numbers start about 232 down in the cockpit.</summary>
        public const float LineTop = 144f, LineHeight = 40f, CardHeight = 66f;
        /// <summary>The row inside the ticks: its inset from the box edge, the row's height (an EMPHASIS line) and the gap between words.</summary>
        public const float Pad = 10f, RowTop = 8f, RowHeight = 28f, WordGap = 12f, CallGap = 16f;
        /// <summary>How far the lane rises in the external views (the third-person HDG box stands about 200 down).</summary>
        public const float ExternalLift = 100f;
        /// <summary>The lane's lift now (0 in the cockpit, <see cref="ExternalLift"/> outside): set by the HUD board every tick, read by both owners. Main thread only.</summary>
        public static float Lift;
        /// <summary>The ace card is up in the slot (set by its owner): the countermeasure popup then stays out of the way.</summary>
        public static bool CardUp;
        /// <summary>The WARNING line is up in the slot (set by its owner): the ace card then stays down, the line wins.</summary>
        public static bool LineUp;

        // The look: strokes and tick arms, then the sizes the standard names.
        public const float Stroke = HudStroke.Standard, Arm = 6f;
        public const float NamePt = HudType.Emphasis, DetailPt = HudType.Body, LabelPt = HudType.Label, ValuePt = HudType.Value;

        // Motion: the onset blink is three blinks at 3 Hz and nothing in the layer flashes faster.
        public const float EnterS = 0.18f, ExitS = 0.12f, TypeS = 0.03f, OnsetS = 1f, BlinkHz = 2f, UrgentHz = 3f;

        /// <summary>Lit for the first 62 % of each cycle at <paramref name="hz"/>: a word blinking at this reads longer than it hides.</summary>
        public static bool Lit(float t, float hz) => t < 0f || t * hz % 1f < 0.62f;

        /// <summary>The ticks' box width for a row whose words are <paramref name="contentW"/> wide: the content and its insets, never past <see cref="MaxWidth"/>.</summary>
        public static float BoxWidth(float contentW) => System.Math.Min(MaxWidth, contentW + Pad * 2f);

        /// <summary>The room a middle word has between the kind word and the call word of a row <paramref name="boxW"/> wide (0 or less: none).</summary>
        public static float MiddleRoom(float boxW, float kindW, float callW) =>
            boxW - Pad * 2f - kindW - WordGap - (callW > 0f ? CallGap + callW : 0f);

        /// <summary>
        /// How far (reference pixels, + right) a piece of the lane <paramref name="width"/> wide, standing from <paramref name="top"/> down <paramref name="height"/>, moves sideways to keep
        /// <see cref="Gap"/> clear of the TAC BOARD's box (screen pixels, y down from the top), from the screen's size in pixels. A short line never leaves the axis.
        /// </summary>
        public static float LaneShift(float screenW, float screenH, float width, float top, float height, float boardLeft, float boardTop, float boardRight, float boardBottom)
        {
            if (!(screenH > 0f)) return 0f;
            float k = 1080f / screenH;
            return Shift(screenW * k, width, top, height, boardLeft * k, boardTop * k, boardRight * k, boardBottom * k);
        }

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
    }
}
