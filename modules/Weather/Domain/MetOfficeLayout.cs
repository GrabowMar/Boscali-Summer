namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>The window's geometry in canvas units on the 1280 x 720 reference. One place, so a test can prove each column fits before anything is drawn.</summary>
    internal static class MetOfficeLayout
    {
        public const float Width = 1000f, Height = 684f, Title = 30f, Pad = 8f, Gap = 5f, ColGap = 8f, Command = 40f, Footer = 30f;
        public const float Col1 = 300f;

        public static float Col23 => (Width - 2f * Pad - Col1 - 2f * ColGap) * 0.5f;
        public static float BodyTop => Title + Pad;
        public static float CommandTop => Height - Footer - Command - Gap;
        public static float BodyBottom => CommandTop - Gap;
        public static float BodyHeight => BodyBottom - BodyTop;

        // Fixed heights of the parts. Kit parts keep their kit height (AvSection 23, AvSegmented / AvStepper / AvControl row 28).
        public const float Section = 23f, Row = 28f, CardChrome = 30f;
        public const float ChartSquare = 300f, ChartLegend = 38f, ChartPart = ChartSquare + Gap + ChartLegend;
        public const float FrontsCard = 66f, IdentityCard = 78f;   // identity: chrome 30 + three 16 px lines
        public const float SkyCard = 150f, PinCard = 90f, OutlookStrip = 76f, SetCellRow = 31f, CellGap = 4f;
        public const float PipsRow = 19f, StripArt = 66f, StripMeta = 15f;
        public const float ScenarioRow = 30f, LogLines = 4f, LogLine = 16f;

        public static float SetGrid => 3f * SetCellRow + 2f * CellGap;
        public static float StoryCard => CardChrome + Row + 3f + PipsRow + 2f + StripArt + StripMeta;
        public static float TimeCard => CardChrome + Row + Gap + Row + Gap + Row + 3f + StripMeta;
        public static float ScenarioCard => CardChrome + 3f * ScenarioRow + 2f * CellGap;
        public static float LogCard => CardChrome + LogLines * LogLine;

        private static float Stack(params float[] parts)
        {
            float h = 0f;
            for (int i = 0; i < parts.Length; i++) h += parts[i] + (i > 0 ? Gap : 0f);
            return h;
        }

        /// <summary>Content height of a column (1-3), gaps included.</summary>
        public static float ColumnHeight(int column)
        {
            switch (column)
            {
                case 1: return Stack(Section, ChartPart, FrontsCard, IdentityCard);
                case 2: return Stack(SkyCard, PinCard, Section, OutlookStrip, Section, SetGrid, Row, Row);
                default: return Stack(StoryCard, TimeCard, ScenarioCard, LogCard);
            }
        }
    }
}
