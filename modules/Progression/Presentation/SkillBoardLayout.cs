using System;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// The SKILLS board's arithmetic, kept out of the panel so a test can hold the one
    /// property that matters: four lanes fit side by side, every cell the same width, none
    /// zero and none overlapping. The board is a matrix - one row per grade, one column per
    /// qualification, a narrow gutter naming the grade - and it is laid out with explicit
    /// rects rather than a layout tree, because a cell that collapses to zero width renders
    /// as a smear of one-letter lines and the failure is invisible until it is in the game.
    /// </summary>
    internal static class SkillBoardLayout
    {
        /// <summary>Grade gutter on the left of the matrix: "G1".."G6".</summary>
        public const float Gutter = 26f;

        /// <summary>Column and row gap; the same 6px the tables elsewhere use.</summary>
        public const float Gap = 6f;

        /// <summary>Below this a cell cannot hold an icon, a two-line name and its line.</summary>
        public const float MinCellHeight = 68f;

        /// <summary>Above this the board reads as scattered tiles instead of a table.</summary>
        public const float MaxCellHeight = 76f;

        public const float FileHeaderHeight = 28f;
        public const float TitleHeight = 24f;
        public const float LaneHeaderHeight = 30f;
        public const float LegendHeight = 34f;
        public const float DetailHeight = 58f;
        public const float StateHeight = 14f;
        public const float StateBottomInset = 3f;

        /// <summary>Top offset of the state line, measured down from a cell's top edge.</summary>
        public static float StateTop(float rowHeight) =>
            -rowHeight + StateHeight + StateBottomInset;

        /// <summary>Equal share of the width after the gutter and one gap per lane.</summary>
        public static float CellWidth(float width, int lanes) =>
            lanes <= 0 ? 0f : Math.Max(0f, (width - Gutter - Gap * lanes) / lanes);

        public static float CellX(float x, float cellWidth, int lane) =>
            x + Gutter + Gap + lane * (cellWidth + Gap);

        /// <summary>
        /// Row height that makes the whole board fit <paramref name="listHeight"/> exactly,
        /// clamped to the range a cell stays readable in. ContentHeight(RowHeight(h), g) never
        /// exceeds h unless the minimum itself already does, which a test pins.
        /// </summary>
        public static float RowHeight(float listHeight, int grades)
        {
            if (grades <= 0) return MinCellHeight;
            return Math.Min(MaxCellHeight,
                Math.Max(MinCellHeight, (listHeight - FixedHeight(grades)) / grades));
        }

        /// <summary>Everything the board spends on anything but the grade rows.</summary>
        public static float FixedHeight(int grades) => ContentHeight(0f, grades);

        /// <summary>Everything the board draws, in draw order, for the scroll viewport's height.</summary>
        public static float ContentHeight(float rowHeight, int grades) =>
            FileHeaderHeight + TitleHeight + LegendHeight + LaneHeaderHeight + Gap +
            grades * (rowHeight + Gap);
    }
}
