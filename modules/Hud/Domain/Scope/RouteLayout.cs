namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The ROUTE slot's rectangles in its own units (the zone is 281 x 133 reference px, origin its top-left): the caption shelf, five 20 high rows, and the DOCK face's dial on the left. Pure.</summary>
    internal static class RouteLayout
    {
        public const float W = 281f, H = 133f, Shelf = 16f, RowH = 20f, RowsTop = 22f, Dial = 109f, Gap = 12f;

        /// <summary>Row <paramref name="i"/> (0..4) across the whole zone.</summary>
        public static BoardRect Row(int i) => new BoardRect(0f, RowsTop + i * RowH, W, RowH);

        /// <summary>The DOCK face's funnel dial.</summary>
        public static BoardRect DialBox() => new BoardRect(0f, RowsTop, Dial, Dial);

        /// <summary>Row <paramref name="i"/> (0..4) to the right of the dial.</summary>
        public static BoardRect RowBesideDial(int i) => new BoardRect(Dial + Gap, RowsTop + i * RowH, W - Dial - Gap, RowH);
    }
}
