namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Pure: which parts of the BOARD tab a mode shows (annex 05 P9 page plan). The panel hides a part with SetShown, so a gap closes; this
    /// table is the one place that says which parts those are, and the harness and the tests read the same table.
    /// </summary>
    internal readonly struct StrPagePlan
    {
        /// <summary>The live-operation lane with its tickets and wave strip.</summary>
        public readonly bool Hero;
        /// <summary>The three proposal tickets.</summary>
        public readonly bool ProposalTickets;
        /// <summary>The platform lanes (compact), or the placeholders when <see cref="Placeholders"/> is set.</summary>
        public readonly bool Lanes;
        /// <summary>Two hatched "not reported" lanes in place of the platforms (the modes with no war report at all).</summary>
        public readonly bool Placeholders;
        /// <summary>The posture row: above the tickets when there is no operation, below the lanes when there is one.</summary>
        public readonly bool PostureTop, PostureBottom;
        /// <summary>The no-opening checklist inside the banner.</summary>
        public readonly bool Checklist;
        /// <summary>Heading of the lane section.</summary>
        public readonly string LaneTitle;

        private StrPagePlan(bool hero, bool tickets, bool lanes, bool placeholders, bool postureTop, bool postureBottom, bool checklist, string laneTitle)
        {
            Hero = hero; ProposalTickets = tickets; Lanes = lanes; Placeholders = placeholders;
            PostureTop = postureTop; PostureBottom = postureBottom; Checklist = checklist; LaneTitle = laneTitle;
        }

        public static StrPagePlan For(StrBoardMode mode)
        {
            switch (mode)
            {
                case StrBoardMode.Operation: return new StrPagePlan(true, false, true, false, false, true, false, "OTHER PLATFORMS");
                case StrBoardMode.Proposals: return new StrPagePlan(false, true, true, false, true, false, false, "PLATFORMS");
                case StrBoardMode.Assessing: return new StrPagePlan(false, false, true, false, true, false, false, "PLATFORMS");
                case StrBoardMode.NoOpening: return new StrPagePlan(false, false, true, false, true, false, true, "PLATFORMS");
                case StrBoardMode.Reduced: return new StrPagePlan(false, false, true, false, false, false, false, "PLATFORMS");
                default: return new StrPagePlan(false, false, false, true, false, false, false, "PLATFORMS");
            }
        }

        /// <summary>A lane stays off the board when a ticket above it already carries its proposal (Proposals mode only).</summary>
        public static bool ShowsLane(StrBoardMode mode, StrFrontRow row) =>
            row != null && !(mode == StrBoardMode.Proposals && !row.Offer.IsNone);

        /// <summary>What a placeholder lane says: the modes that never heard from the host say so, the others say there is nothing to report.</summary>
        public static string PlaceholderText(StrBoardMode mode) =>
            mode == StrBoardMode.Waiting || mode == StrBoardMode.NoAnswer ? "PLATFORM NOT REPORTED" : "NO PLATFORMS REPORTED";
    }
}
