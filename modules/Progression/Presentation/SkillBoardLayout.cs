using System;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// The SKILLS board's arithmetic, kept out of the panel so a test can hold the one
    /// property that matters: four lanes fit side by side, every node the same width, none
    /// zero and none overlapping. The board is a tech tree - one column per qualification, one
    /// aligned tier row per grade, a narrow gutter numbering the tier - laid out with explicit
    /// rects rather than a layout tree, because a node that collapses to zero width renders as
    /// a smear of one-letter lines and the failure is invisible until it is in the game.
    /// </summary>
    internal static class SkillBoardLayout
    {
        /// <summary>Tier gutter on the left of the tree: "1".."6".</summary>
        public const float Gutter = 18f;

        /// <summary>Column gap.</summary>
        public const float Gap = 6f;

        /// <summary>Below this a node cannot hold its glyph and a two-line name.</summary>
        public const float NodeHeight = 52f;

        /// <summary>Vertical gap between tiers; the connector line runs through it.</summary>
        public const float NodeGap = 8f;

        public const float LegendHeight = 22f;
        public const float LaneHeaderHeight = 44f;

        /// <summary>Equal share of the width after the gutter and one gap per lane.</summary>
        public static float CellWidth(float width, int lanes) =>
            lanes <= 0 ? 0f : Math.Max(0f, (width - Gutter - Gap * lanes) / lanes);

        public static float CellX(float x, float cellWidth, int lane) =>
            x + Gutter + Gap + lane * (cellWidth + Gap);

        /// <summary>Top of the lane headers, below the legend row.</summary>
        public static float HeaderTop => LegendHeight + Gap;

        /// <summary>Top of a tier's node row (0-based tier).</summary>
        public static float NodeTop(int tier) =>
            HeaderTop + LaneHeaderHeight + Gap + tier * (NodeHeight + NodeGap);

        /// <summary>Everything the board draws, in draw order, for the flow's measurement.</summary>
        public static float ContentHeight(int grades) =>
            grades <= 0 ? HeaderTop + LaneHeaderHeight : NodeTop(grades - 1) + NodeHeight;
    }
}
