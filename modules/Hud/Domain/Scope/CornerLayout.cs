using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The furniture of the minimised map's corner as the board draws it (LAYOUT.md 7), in design units from the top-left corner of the canvas, y down. The brackets are the rectangle
    /// (<see cref="X0"/>, <see cref="Y0"/>) to (<see cref="X1"/>, <see cref="Y1"/>) with arms of <see cref="Arm"/> on every corner (<see cref="Both"/>); the caption shelf is <see cref="ShelfW"/> wide from <see cref="ShelfX"/>,
    /// its line 6 under <see cref="ShelfY"/> (<c>ScopeCanvas.CaptionShelf</c>'s convention). (The kill feed's corner, DL EVT LOG, is <c>EventLogLayout</c>'s.)
    /// </summary>
    internal struct CornerFurniture
    {
        public float X0, Y0, X1, Y1, Arm, ShelfX, ShelfY, ShelfW;
        public bool Both;

        /// <summary>There is room for the brackets.</summary>
        public readonly bool Present => Arm >= 2f && Y1 > Y0;
    }

    /// <summary>
    /// Where the furniture of the minimised map's corner goes, from the rectangle of the game's own object (LAYOUT.md 7). Pure. The rectangle is in canvas units from the canvas's top-left corner (x right, y down);
    /// <paramref name="k"/> carries canvas units into design units (the mock's k: 1 at 1080p, 0.784 at 720p, where a design unit is 0.85 of a pixel and a canvas unit 0.667).
    /// </summary>
    internal static class CornerLayout
    {
        /// <summary>TAC MAP (bottom-left): the brackets stand 4 in from the plate at its left, right and top, 12 over its bottom (the screen's bottom), arms 18 (all of it in canvas units, carried by k). The plate is x 0..320 by
        /// 760..1080: the brackets are (4, 766) to (316, 1068), 312 x 302. The caption shelf stands INSIDE the frame at its lower left (the game's own edge markers, "Missile [ARH] 7,4km", sit on the screen's edges and
        /// over the plate's top, where the old shelf was and was covered): its line <see cref="MapShelfRise"/> over the bottom bracket line, <see cref="MapShelfInset"/> in from the left bracket, <see cref="MapShelfW"/> wide (design units).</summary>
        public const float MapInsetX = 4f, MapInsetTop = 6f, MapInsetBottom = 12f, MapArm = 18f, MapShelfInset = 6f, MapShelfRise = 14f, MapShelfW = 104f;
        /// <summary>The north tick on the frame: 8 long inwards, the "N" 3 clear of its end, kept <see cref="NorthClear"/> from every corner arm (and from the shelf on the bottom edge).</summary>
        public const float NorthTick = 8f, NorthGap = 3f, NorthClear = 8f;

        /// <summary>The shelf's line is this far under the <c>y</c> a shelf is given (the block's top).</summary>
        private const float ShelfLine = 6f;

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
            if (!float.IsFinite(turnedCcwDeg) || !f.Present || !f.Both) return n;
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
    }
}
