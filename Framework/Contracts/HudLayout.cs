namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// The common HUD element's presentation ladder: the size steps, the opacity steps and the
    /// bounds on every one of them. Pure, and shared by the seam, its implementation and the
    /// settings page, so the labels the pilot reads and the numbers the board applies can never
    /// drift apart. The element hangs at one fixed dock (below the native weapon panel) since
    /// the 2026-09-28 minimal rebuild, so there is no anchor preset here any more.
    /// </summary>
    internal static class HudLayout
    {
        public const int ScaleCount = 4;
        public static string ContrastName(int value) => value == 0 ? "CLEAR" : value == 2 ? "SOLID" : "GLASS";
        public const int OpacityCount = 4;
        public const int MinRows = 1;
        public const int MaxRows = 6;

        /// <summary>
        /// Feeds the element will accept. Past this a declaration is refused once. Sized for the
        /// module feeds plus the mechanic widgets a full install declares, with room to spare
        /// before a feature has to share a channel.
        /// </summary>
        public const int MaxChannels = 16;

        public const float MinNoticeSeconds = 3f;
        public const float MaxNoticeSeconds = 20f;
        public const float DefaultNoticeSeconds = 8f;

        private const float OpacityFull = 1f;
        private const float OpacityHigh = 0.8f;
        private const float OpacityLow = 0.55f;

        private static readonly string[] ScaleNames = { "COMPACT", "NORMAL", "LARGE", "HUGE" };
        private static readonly float[] ScaleFactors = { 0.85f, 1f, 1.2f, 1.45f };

        private static readonly string[] OpacityNames = { "FULL", "HIGH", "LOW", "OFF" };
        private static readonly float[] OpacityFactors = { OpacityFull, OpacityHigh, OpacityLow, 0f };

        public static int ClampScale(int step) => step < 0 ? 0 : step >= ScaleCount ? ScaleCount - 1 : step;

        public static string ScaleName(int step) => ScaleNames[ClampScale(step)];

        /// <summary>The multiplier the step applies to vanilla's own objective text size.</summary>
        public static float Scale(int step) => ScaleFactors[ClampScale(step)];

        public static int ClampOpacity(int step) => step < 0 ? 0 : step >= OpacityCount ? OpacityCount - 1 : step;

        public static string OpacityName(int step) => OpacityNames[ClampOpacity(step)];

        /// <summary>The panel's overall alpha. Zero means OFF, which hides the element.</summary>
        public static float Opacity(int step) => OpacityFactors[ClampOpacity(step)];

        public static int ClampRows(int rows) => rows < MinRows ? MinRows : rows > MaxRows ? MaxRows : rows;

        public static float ClampNoticeSeconds(float seconds) =>
            float.IsNaN(seconds) || float.IsInfinity(seconds) ? DefaultNoticeSeconds
                : seconds < MinNoticeSeconds ? MinNoticeSeconds
                : seconds > MaxNoticeSeconds ? MaxNoticeSeconds : seconds;

        /// <summary>Step one value around a bounded ring, for the settings page's - and + buttons.</summary>
        public static int Cycle(int step, int count, int direction)
        {
            if (count <= 1) return 0;
            int current = step < 0 ? 0 : step >= count ? count - 1 : step;
            int next = current + (direction < 0 ? -1 : 1);
            return next < 0 ? count - 1 : next >= count ? 0 : next;
        }
    }
}
