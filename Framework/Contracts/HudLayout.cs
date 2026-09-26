namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Where the stack hangs, resolved to plain numbers so it can be tested without Unity.
    /// Positions are normalised anchors on the reference canvas; offsets are reference pixels
    /// from the screen edge the block hangs from, and are negative for a top or right edge.
    /// </summary>
    internal struct HudPlacement
    {
        public float AnchorX;
        public float AnchorY;
        public float PivotX;
        public float PivotY;
        public float OffsetX;
        public float OffsetY;

        /// <summary>True when each row's text is right-aligned to the block's right edge.</summary>
        public bool AlignRight;

        /// <summary>
        /// True when this anchor hangs off the vanilla weapon column and wants its live
        /// rectangle instead of the fixed fallback offsets.
        /// </summary>
        public bool FollowsWeaponColumn;
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
        public const int OpacityCount = 4;
        public const int MinRows = 1;
        public const int MaxRows = 6;

        /// <summary>Feeds the element will accept. Past this a declaration is refused once.</summary>
        public const int MaxChannels = 8;

        public const float MinNoticeSeconds = 3f;
        public const float MaxNoticeSeconds = 20f;
        public const float DefaultNoticeSeconds = 8f;

        /// <summary>How wide the block is. Matches the vanilla weapon column so their edges line up.</summary>
        public const float BlockWidth = 460f;

        /// <summary>Air between the vanilla weapon column's own rectangle and the first line.</summary>
        public const float ColumnGap = 10f;

        /// <summary>
        /// Fallback top offset, used only until the vanilla weapon column has been measured: its
        /// own background reaches 166 px below the top of the screen at 1080p.
        /// </summary>
        private const float TopInset = 166f;

        private const float SideInset = 28f;

        /// <summary>Clear of the log, ammo and throttle rows along the bottom.</summary>
        private const float BottomInset = 132f;

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

        /// <summary>
        /// Resolve an anchor to the block's anchor, pivot and edge offsets. Every block grows
        /// away from the screen edge it hangs from — the pivot carries that, not a separate
        /// flag — which is what keeps a long contract title on screen instead of off the side.
        /// </summary>
        public static HudPlacement Place(HudAnchor anchor)
        {
            var placement = new HudPlacement();
            switch (anchor)
            {
                case HudAnchor.TopCentre:
                    placement.AnchorX = placement.PivotX = 0.5f;
                    placement.AnchorY = placement.PivotY = 1f;
                    placement.OffsetY = -TopInset;
                    break;

                case HudAnchor.TopRight:
                    placement.AnchorX = placement.PivotX = 1f;
                    placement.AnchorY = placement.PivotY = 1f;
                    placement.OffsetX = -SideInset;
                    placement.OffsetY = -TopInset;
                    placement.AlignRight = true;
                    break;

                case HudAnchor.TopLeft:
                    placement.AnchorX = placement.PivotX = 0f;
                    placement.AnchorY = placement.PivotY = 1f;
                    placement.OffsetX = SideInset;
                    placement.OffsetY = -TopInset;
                    break;

                case HudAnchor.MiddleRight:
                    placement.AnchorX = placement.PivotX = 1f;
                    placement.AnchorY = placement.PivotY = 0.5f;
                    placement.OffsetX = -SideInset;
                    placement.AlignRight = true;
                    break;

                case HudAnchor.MiddleLeft:
                    placement.AnchorX = placement.PivotX = 0f;
                    placement.AnchorY = placement.PivotY = 0.5f;
                    placement.OffsetX = SideInset;
                    break;

                case HudAnchor.BottomRight:
                    placement.AnchorX = placement.PivotX = 1f;
                    placement.AnchorY = placement.PivotY = 0f;
                    placement.OffsetX = -SideInset;
                    placement.OffsetY = BottomInset;
                    placement.AlignRight = true;
                    break;

                case HudAnchor.BottomLeft:
                    placement.AnchorX = placement.PivotX = 0f;
                    placement.AnchorY = placement.PivotY = 0f;
                    placement.OffsetX = SideInset;
                    placement.OffsetY = BottomInset;
                    break;

                default:
                    // UnderWeapons: the fallback offsets are the vanilla weapon column's own
                    // measured extent; the board replaces them the moment it can measure it.
                    placement.AnchorX = placement.PivotX = 1f;
                    placement.AnchorY = placement.PivotY = 1f;
                    placement.OffsetX = -SideInset;
                    placement.OffsetY = -(TopInset + ColumnGap);
                    placement.AlignRight = true;
                    placement.FollowsWeaponColumn = true;
                    break;
            }
            return placement;
        }
    }
}
