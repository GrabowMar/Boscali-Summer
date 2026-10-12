using System;

namespace BoscaliSummer.Modules.Progression.Domain
{
    /// <summary>
    /// Where the ace-hunt banner stands against the TAC BOARD (<c>IHudBoard.TryGetScreenBox</c>). The banner is centred along the top of the screen; the board hangs from the top-right corner, and in WIDE its stores
    /// side (HERO) reaches far enough left to be under the banner's right end (the 704 wide first panel, up to 272 x 116 pixels of it). Pure, in one unit throughout (the banner's canvas units).
    /// </summary>
    internal static class AceHuntPlacement
    {
        /// <summary>Clear space kept between the banner and the board.</summary>
        public const float Gap = 12f;

        /// <summary>
        /// How far (to the right, negative to the left) a panel <paramref name="width"/> wide, centred on a screen <paramref name="screenW"/> wide and standing from <paramref name="top"/> down <paramref name="height"/>,
        /// has to move sideways to leave the board's box (<paramref name="boardLeft"/>, <paramref name="boardTop"/>, <paramref name="boardRight"/>, <paramref name="boardBottom"/>) and <see cref="Gap"/> clear. 0 when it is already clear. It goes the shorter way out that keeps it on the
        /// screen (the board is at the right, so that is usually left); where neither does, as far left as the screen allows.
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
            return Math.Max(toLeft, -left);
        }
    }
}
