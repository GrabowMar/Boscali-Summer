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
        /// <summary>DL EVT LOG (top-left): the game's four lines are at x 6, in a box 0..475 by 0..90; the brackets stand 2 in from its left edge, their corners <see cref="FeedTop"/> under its top and 4 over its bottom, arms 10; the caption shelf is
        /// 8 under the box, from x 2, 150 wide (design units, as the mock draws them). The mock drew its first line 21 down (so a top corner 10 under the top cleared the capitals); in game the first line is higher whatever the box holds
        /// (a one line box is 34 high and its capitals reach from y 9 to 21: the arm at 10 ran through the first letter), so the corner is 3 under the top.</summary>
        public const float FeedInset = 2f, FeedTop = 3f, FeedBottom = 4f, FeedArm = 10f, FeedShelfGap = 8f, FeedShelfW = 150f;

        /// <summary>TAC MAP (bottom-left): the brackets stand 4 in from the plate at its left, right and top, 12 over its bottom (the screen's bottom), arms 18 (all of it in canvas units, carried by k). The plate is x 0..320 by
        /// 760..1080: the brackets are (4, 766) to (316, 1068), 312 x 302. The caption shelf stands INSIDE the frame at its lower left (the game's own edge markers, "Missile [ARH] 7,4km", sit on the screen's edges and
        /// over the plate's top, where the old shelf was and was covered): its line <see cref="MapShelfRise"/> over the bottom bracket line, <see cref="MapShelfInset"/> in from the left bracket, <see cref="MapShelfW"/> wide (design units).</summary>
        public const float MapInsetX = 4f, MapInsetTop = 6f, MapInsetBottom = 12f, MapArm = 18f, MapShelfInset = 6f, MapShelfRise = 14f, MapShelfW = 104f;
        /// <summary>The north tick on the frame: 8 long inwards, the "N" 3 clear of its end, kept <see cref="NorthClear"/> from every corner arm (and from the shelf on the bottom edge).</summary>
        public const float NorthTick = 8f, NorthGap = 3f, NorthClear = 8f;

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

        /// <summary>The minimised map's plate (canvas units; <paramref name="screenBottom"/> is the canvas's bottom, which the plate runs to or past): four brackets round it and the caption shelf inside their lower left.</summary>
        public static CornerFurniture Map(float l, float t, float r, float b, float screenBottom, float k)
        {
            var f = new CornerFurniture
            {
                X0 = (l + MapInsetX) * k, Y0 = (t + MapInsetTop) * k, X1 = (r - MapInsetX) * k, Y1 = (Math.Min(b, screenBottom) - MapInsetBottom) * k, Arm = MathF.Round(MapArm * k), Both = true,
            };
            f.ShelfX = f.X0 + MapShelfInset;
            f.ShelfW = MapShelfW;
            f.ShelfY = f.Y1 - MapShelfRise - ShelfLine;
            return f;
        }

        /// <summary>Where the map's north is on its frame (<see cref="Map"/>'s rectangle): the point on its edge, which edge, and where the "N" goes.</summary>
        public struct MapNorth
        {
            public bool Present;
            /// <summary>0 top, 1 right, 2 bottom, 3 left.</summary>
            public int Side;
            /// <summary>The tick's outer end on the frame's edge, its inner end <see cref="CornerLayout.NorthTick"/> in, and the centre of the "N".</summary>
            public float X, Y, InX, InY, LabelX, LabelY;
        }

        /// <summary>
        /// The minimised map turns with the aircraft: the game rotates its map image by the heading, counter-clockwise (z = heading), so north lies <paramref name="turnedCcwDeg"/> counter-clockwise of up on the screen.
        /// The tick is where a ray from the frame's middle in that direction leaves the frame, slid along its edge to stay <see cref="NorthClear"/> clear of the corner arms (and of the caption shelf at the lower left). NaN: not readable, no tick.
        /// </summary>
        public static MapNorth North(in CornerFurniture f, float turnedCcwDeg)
        {
            var n = default(MapNorth);
            if (float.IsNaN(turnedCcwDeg) || float.IsInfinity(turnedCcwDeg) || !f.Present || !f.Both) return n;
            float a = turnedCcwDeg * (MathF.PI / 180f), dx = -MathF.Sin(a), dy = -MathF.Cos(a);      // screen y is down
            float cx = (f.X0 + f.X1) * 0.5f, cy = (f.Y0 + f.Y1) * 0.5f;
            float tx = dx > 1e-4f ? (f.X1 - cx) / dx : dx < -1e-4f ? (f.X0 - cx) / dx : float.MaxValue;
            float ty = dy > 1e-4f ? (f.Y1 - cy) / dy : dy < -1e-4f ? (f.Y0 - cy) / dy : float.MaxValue;
            float arm = f.Arm + NorthClear;
            n.Present = true;
            if (tx < ty)
            {
                n.Side = dx > 0f ? 1 : 3;
                n.X = n.Side == 1 ? f.X1 : f.X0;
                n.Y = Math.Min(Math.Max(cy + dy * tx, f.Y0 + arm), f.Y1 - arm);
                float inward = n.Side == 1 ? -1f : 1f;
                n.InX = n.X + inward * NorthTick; n.InY = n.Y;
                n.LabelX = n.X + inward * (NorthTick + NorthGap + 4f); n.LabelY = n.Y;
            }
            else
            {
                n.Side = dy > 0f ? 2 : 0;
                n.Y = n.Side == 2 ? f.Y1 : f.Y0;
                float lo = f.X0 + arm, hi = f.X1 - arm;
                if (n.Side == 2) lo = Math.Max(lo, f.ShelfX + f.ShelfW + NorthClear + 8f);      // not over the caption shelf
                n.X = Math.Min(Math.Max(cx + dx * ty, lo), Math.Max(lo, hi));
                float inward = n.Side == 2 ? -1f : 1f;
                n.InX = n.X; n.InY = n.Y + inward * NorthTick;
                n.LabelX = n.X; n.LabelY = n.Y + inward * (NorthTick + NorthGap + 6f);
            }
            return n;
        }

        // ---- NAV DATA (bottom right): the block's box from the screen's size in design units

        /// <summary>NAV DATA: 260 wide, its right edge and its bottom 24 in from the screen's, rows 18 apart; the caption shelf's line is 6 under the block's top and the first row's middle 24 under it (6 clear of the caption's glyphs), without the shelf 6.</summary>
        public const float NavWidth = 260f, NavMargin = 24f, NavPitch = 18f, NavShelfToRow = 24f, NavRowOffset = 6f, NavShadeStep = 16f;

        /// <summary>The NAV DATA block as laid out (design units from the screen's top-left, y down). <see cref="Top"/> is where the caption shelf is given (its line is 6 under it); a row's middle is <see cref="RowY"/>.</summary>
        public struct NavPlan
        {
            public float Left, Top, Right, Bottom, FirstRowY;
            public int Rows;
            public bool Shelf;

            public readonly bool Present => Rows > 0;
            public readonly float Height => Bottom - Top;
            public readonly float RowY(int i) => FirstRowY + NavPitch * i;
        }

        /// <summary>The block for <paramref name="rows"/> rows on a screen <paramref name="screenW"/> x <paramref name="screenH"/> design units; <paramref name="shelf"/> false (Lean) leaves the caption row out. Rows grow upwards from the bottom margin.</summary>
        public static NavPlan Nav(float screenW, float screenH, int rows, bool shelf)
        {
            if (rows <= 0) return default;
            float height = (shelf ? NavShelfToRow : NavRowOffset) + NavPitch * (rows - 1) + NavRowOffset, right = screenW - NavMargin, bottom = screenH - NavMargin;
            return new NavPlan { Left = right - NavWidth, Right = right, Bottom = bottom, Top = bottom - height, FirstRowY = bottom - height + (shelf ? NavShelfToRow : NavRowOffset), Rows = rows, Shelf = shelf };
        }

        /// <summary>
        /// The block's rectangle for the cloud behind it: as wide as the block, its height up to the next <see cref="NavShadeStep"/> so a row coming or going does not bake the field again each time, from the bottom margin up.
        /// </summary>
        public static BoardRect NavShade(in NavPlan p)
        {
            if (!p.Present) return default;
            float h = MathF.Ceiling(p.Height / NavShadeStep) * NavShadeStep;
            return new BoardRect(p.Left, p.Bottom - h, NavWidth, h);
        }
    }
}
