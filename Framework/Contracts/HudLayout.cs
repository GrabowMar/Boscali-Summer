namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Where the stack hangs, resolved to plain numbers so it can be tested without Unity:
    /// normalised anchors on the safe area, 0 at the left or bottom edge and 1 at the right or top.
    /// </summary>
    internal struct HudPlacement
    {
        public float AnchorX;
        public float AnchorY;
    }

    /// <summary>
    /// The common HUD element's presentation ladder: the anchor presets, the size steps, the
    /// opacity steps and the bounds on every one of them. Pure, and shared by the seam, its
    /// implementation and the settings page, so the labels the pilot reads and the numbers the
    /// board applies can never drift apart.
    /// </summary>
    internal static class HudLayout
    {
        public const int ScaleCount = 4;
        public static string ContrastName(int value) => value == 0 ? "CLEAR" : value == 2 ? "SOLID" : "GLASS";
        public static string CornerName(int value) => value == 1 ? "BOTTOM LEFT" : value == 2 ? "TOP RIGHT" : value == 3 ? "TOP LEFT" : "BOTTOM RIGHT";
        public const int OpacityCount = 4;
        public const int MinRows = 1;
        public const int MaxRows = 6;

        /// <summary>
        /// Feeds the element will accept. Past this a declaration is refused once. Sized for the
        /// two module feeds plus the seven mechanic widgets a full install declares, with room
        /// for one more before a feature has to share a channel.
        /// </summary>
        public const int MaxChannels = 12;

        public const float MinNoticeSeconds = 3f;
        public const float MaxNoticeSeconds = 20f;
        public const float DefaultNoticeSeconds = 8f;

        private const float OpacityFull = 1f;
        private const float OpacityHigh = 0.8f;
        private const float OpacityLow = 0.55f;

        private static readonly string[] AnchorNames =
        {
            "UNDER WEAPONS", "TOP CENTRE", "TOP RIGHT", "TOP LEFT", "RIGHT", "LEFT",
            "BOTTOM RIGHT", "BOTTOM LEFT"
        };

        private static readonly string[] ScaleNames = { "COMPACT", "NORMAL", "LARGE", "HUGE" };
        private static readonly float[] ScaleFactors = { 0.85f, 1f, 1.2f, 1.45f };

        private static readonly string[] OpacityNames = { "FULL", "HIGH", "LOW", "OFF" };
        private static readonly float[] OpacityFactors = { OpacityFull, OpacityHigh, OpacityLow, 0f };

        public static int AnchorCount => AnchorNames.Length;

        public static int ClampAnchor(int index) => index < 0 ? 0 : index >= AnchorNames.Length ? AnchorNames.Length - 1 : index;

        public static string AnchorName(int index) => AnchorNames[ClampAnchor(index)];

        public static int ClampScale(int step) => step < 0 ? 0 : step >= ScaleCount ? ScaleCount - 1 : step;

        public static string ScaleName(int step) => ScaleNames[ClampScale(step)];

        /// <summary>The multiplier the step applies to vanilla's own objective text size.</summary>
        public static float Scale(int step) => ScaleFactors[ClampScale(step)];

        public static int ClampOpacity(int step) => step < 0 ? 0 : step >= OpacityCount ? OpacityCount - 1 : step;

        public static string OpacityName(int step) => OpacityNames[ClampOpacity(step)];

        /// <summary>The block's CanvasGroup alpha. Zero means OFF, which hides the element.</summary>
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

        /// <summary>Resolve an anchor preset to its normalised screen anchor.</summary>
        public static HudPlacement Place(HudAnchor anchor)
        {
            switch (anchor)
            {
                case HudAnchor.TopCentre: return new HudPlacement { AnchorX = 0.5f, AnchorY = 1f };
                case HudAnchor.TopLeft: return new HudPlacement { AnchorX = 0f, AnchorY = 1f };
                case HudAnchor.MiddleRight: return new HudPlacement { AnchorX = 1f, AnchorY = 0.5f };
                case HudAnchor.MiddleLeft: return new HudPlacement { AnchorX = 0f, AnchorY = 0.5f };
                case HudAnchor.BottomRight: return new HudPlacement { AnchorX = 1f, AnchorY = 0f };
                case HudAnchor.BottomLeft: return new HudPlacement { AnchorX = 0f, AnchorY = 0f };
                // UnderWeapons starts here too; the status feed then pins it below the weapon column.
                case HudAnchor.TopRight:
                default: return new HudPlacement { AnchorX = 1f, AnchorY = 1f };
            }
        }
    }
}
