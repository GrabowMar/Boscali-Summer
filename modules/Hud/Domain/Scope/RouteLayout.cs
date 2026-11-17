namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The ROUTE slot's rectangles in its own units (the zone is 281 x 133 reference px, origin its top-left): the header shelf (its rule at 14), four 24 high rows (middles 38 / 62 / 86 / 110), the bar line at
    /// <see cref="BarY"/>, and the dial of APPROACH, DOCK and OBJECTIVE on the left. Pure.
    /// </summary>
    internal static class RouteLayout
    {
        public const float W = 281f, H = 133f, RowH = 24f, RowsTop = 26f, DialTop = 20f, Dial = 109f, Gap = 12f, BarY = 123f;

        /// <summary>Row <paramref name="i"/> (0..3) across the whole zone.</summary>
        public static BoardRect Row(int i) => new BoardRect(0f, RowsTop + i * RowH, W, RowH);

        /// <summary>The dial's box (its centre at <see cref="Dial"/> / 2, <see cref="DialTop"/> + <see cref="Dial"/> / 2).</summary>
        public static BoardRect DialBox() => new BoardRect(0f, DialTop, Dial, Dial);

        /// <summary>Row <paramref name="i"/> (0..3) to the right of the dial.</summary>
        public static BoardRect RowBesideDial(int i) => new BoardRect(Dial + Gap, RowsTop + i * RowH, W - Dial - Gap, RowH);
    }
}
