namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>The window's geometry in canvas units on the 1920 x 1080 reference: the sky picker on the left, the sky overhead and wind on the right, the strip along the wind across the bottom.</summary>
    internal static class MetOfficeLayout
    {
        public const float Width = 1144f, Title = 30f, Pad = 8f, Gap = 5f, ColGap = 8f, Footer = 30f;
        public const float Left = 720f, Right = 400f;

        // Fixed heights of the parts. Kit parts keep their kit height (AvControl row 28).
        public const float Row = 28f, CardChrome = 30f, CellGap = 4f;
        public const float SkyCard = 150f, WindLine = 16f;
        /// <summary>The fronts list takes the rest of the right column, so it ends flush with the sky gallery.</summary>
        public const float FrontsBody = BodyHeight - SkyCard - Gap - CardChrome - WindLine - Gap;
        public const float StripTop = 2f, StripArt = 66f, StripMeta = 15f;
        public const float TileArt = 66f, TileButton = 64f;
        public const float Tile = TileArt + TileButton;
        public const float Gallery = 2f * Tile + CellGap;
        public const float StripBody = StripTop + StripArt + StripMeta;
        public const float TapeCard = CardChrome + StripBody;

        public const float BodyTop = Title + Pad;
        public const float BodyHeight = Row + Gap + Gallery;
        public const float TapeTop = BodyTop + BodyHeight + Gap;
        public const float Height = TapeTop + TapeCard + Gap + Footer;
    }
}
