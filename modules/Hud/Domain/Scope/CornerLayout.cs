using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The furniture of one screen corner as the board draws it (LAYOUT.md 7), in design units from the top-left corner of the canvas, y down. The brackets are the rectangle
    /// (<see cref="X0"/>, <see cref="Y0"/>) to (<see cref="X1"/>, <see cref="Y1"/>) with arms of <see cref="Arm"/> on every corner (<see cref="Both"/>: the map), or on its left corners only (the feed, whose text leaves
    /// no room on the right); the caption shelf is <see cref="ShelfW"/> wide from <see cref="ShelfX"/>, its line 6 under <see cref="ShelfY"/> (<c>ScopeCanvas.CaptionShelf</c>'s convention).
    /// </summary>
    internal struct CornerFurniture
    {
        public float X0, Y0, X1, Y1, Arm, ShelfX, ShelfY, ShelfW;
        public bool Both;

        /// <summary>There is room for the brackets.</summary>
        public readonly bool Present => Arm >= 2f && Y1 > Y0;
    }

    /// <summary>
    /// Where the furniture of the two screen corners goes, from the rectangle of the game's own object (LAYOUT.md 7). Pure. The rectangle is in canvas units from the canvas's top-left corner (x right, y down);
    /// <paramref name="k"/> carries canvas units into design units (the mock's k: 1 at 1080p, 0.784 at 720p, where a design unit is 0.85 of a pixel and a canvas unit 0.667).
    /// </summary>
    internal static class CornerLayout
    {
        /// <summary>DL EVT LOG (top-left): the game's four lines are at x 6, in a box 0..475 by 0..90; the brackets stand 2 in from its left edge, their corners 10 under its top and 4 over its bottom, arms 10; the caption shelf is
        /// 8 under the box, from x 2, 150 wide (design units, as the mock draws them).</summary>
        public const float FeedInset = 2f, FeedTop = 10f, FeedBottom = 4f, FeedArm = 10f, FeedShelfGap = 8f, FeedShelfW = 150f;

        /// <summary>TAC MAP (bottom-left): the brackets stand 4 in from the plate at its left, right and top, 12 over its bottom (the screen's bottom), arms 18 (all of it in canvas units, carried by k); the shelf is 16 over them
        /// (design units) and as wide as they are. The plate is x 0..320 by 760..1080: the brackets are (4, 766) to (316, 1068), 312 x 302.</summary>
        public const float MapInsetX = 4f, MapInsetTop = 6f, MapInsetBottom = 12f, MapArm = 18f, MapShelfGap = 16f;

        /// <summary>The shelf's line is this far under the <c>y</c> a shelf is given (the block's top).</summary>
        private const float ShelfLine = 6f;

        /// <summary>The kill feed's box (<paramref name="l"/>, <paramref name="t"/>, <paramref name="r"/>, <paramref name="b"/>, canvas units): two arm-10 brackets on its left and the caption shelf under it.</summary>
        public static CornerFurniture Feed(float l, float t, float r, float b, float k)
        {
            float left = l * k, top = t * k, bottom = b * k;
            var f = new CornerFurniture { X0 = left + FeedInset, Y0 = top + FeedTop, Y1 = bottom - FeedBottom, ShelfX = left + FeedInset, ShelfY = bottom + FeedShelfGap - ShelfLine, ShelfW = FeedShelfW };
            f.X1 = f.X0;
            f.Arm = Math.Min(FeedArm, (f.Y1 - f.Y0) * 0.5f - 2f);
            return f;
        }

        /// <summary>The minimised map's plate (canvas units; <paramref name="screenBottom"/> is the canvas's bottom, which the plate runs to or past): four brackets round it and the caption shelf above them.</summary>
        public static CornerFurniture Map(float l, float t, float r, float b, float screenBottom, float k)
        {
            var f = new CornerFurniture
            {
                X0 = (l + MapInsetX) * k, Y0 = (t + MapInsetTop) * k, X1 = (r - MapInsetX) * k, Y1 = (Math.Min(b, screenBottom) - MapInsetBottom) * k, Arm = MathF.Round(MapArm * k), Both = true,
            };
            f.ShelfX = f.X0;
            f.ShelfW = f.X1 - f.X0;
            f.ShelfY = f.Y0 - MapShelfGap - ShelfLine;
            return f;
        }
    }
}
