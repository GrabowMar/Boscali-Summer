namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>The window's geometry in canvas units on the 1920 x 1080 reference. One place, so a test can prove each column fits before anything is drawn.</summary>
    internal static class MetOfficeLayout
    {
        public const float Width = 1480f, Height = 930f, Title = 30f, Pad = 8f, Gap = 5f, ColGap = 8f, Footer = 30f;
        public const float Left = 300f, Right = 400f, Hero = 84f;

        public static float Centre => Width - 2f * Pad - Left - Right - 2f * ColGap;
        public static float HeroTop => Title + Pad;
        public static float BodyTop => HeroTop + Hero + Gap;
        public static float TapeTop => Height - Footer - Gap - TapeCard;
        public static float BodyBottom => TapeTop - Gap;
        public static float BodyHeight => BodyBottom - BodyTop;

        // Fixed heights of the parts. Kit parts keep their kit height (AvSection 23, AvSegmented / AvControl row 28).
        public const float Section = 23f, Row = 28f, CardChrome = 30f;
        public const float FrontsCard = 71f, IdentityCard = 78f;   // identity: chrome 30 + three 16 px lines
        public const float SkyCard = 150f, OutlookStrip = 76f, SetCellRow = 43f, CellGap = 4f;
        public const float PipsRow = 19f, StripArt = 66f, StripMeta = 15f;
        public const float ScenarioRow = 46f, StoryRow = 36f, LogLines = 4f, LogLine = 16f;   // the log is DataSmall text: 16 px a line, so the card reserves exactly that
        public const float TileArt = 66f, TileButton = 64f, InspectRow = 26f;

        public static float SetGrid => 3f * SetCellRow + 2f * CellGap;
        public static float StoryBody => PipsRow + 2f + StripArt + StripMeta;
        public static float TapeCard => CardChrome + StoryBody + Gap + Row;
        public static float ScenarioCard => CardChrome + 6f * ScenarioRow + 5f * CellGap;
        public static float StoriesCard => CardChrome + (WeatherKey.StoryCount + 1) * StoryRow + WeatherKey.StoryCount * CellGap;
        public static float LogCard => CardChrome + LogLines * LogLine;
        public static float Tile => TileArt + TileButton;
        public static float Gallery => 2f * Tile + CellGap;
        public static float InspectCard => CardChrome + 3f * InspectRow + Gap + Row;

        private static float Stack(params float[] parts)
        {
            float h = 0f;
            for (int i = 0; i < parts.Length; i++) h += parts[i] + (i > 0 ? Gap : 0f);
            return h;
        }

        /// <summary>Content height of a column (1 left, 2 centre, 3 right), gaps included.</summary>
        public static float ColumnHeight(int column)
        {
            switch (column)
            {
                case 1: return Stack(ScenarioCard, StoriesCard);
                case 2: return Stack(Row, Gallery, InspectCard, SkyCard);
                default: return Stack(FrontsCard, Section, OutlookStrip, Section, SetGrid, Row, Row, IdentityCard, LogCard);
            }
        }
    }
}
