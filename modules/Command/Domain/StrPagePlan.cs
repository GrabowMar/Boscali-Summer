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
        /// <summary>The platform lanes (compact); the modes with no war report show none.</summary>
        public readonly bool Lanes;
        /// <summary>The posture row.</summary>
        public readonly bool Posture;

        private StrPagePlan(bool hero, bool tickets, bool lanes, bool posture)
        {
            Hero = hero; ProposalTickets = tickets; Lanes = lanes; Posture = posture;
        }

        public static StrPagePlan For(StrBoardMode mode)
        {
            switch (mode)
            {
                case StrBoardMode.Operation: return new StrPagePlan(true, false, true, true);
                case StrBoardMode.Proposals: return new StrPagePlan(false, true, true, true);
                case StrBoardMode.Assessing: return new StrPagePlan(false, false, true, true);
                case StrBoardMode.NoOpening: return new StrPagePlan(false, false, true, true);
                case StrBoardMode.Reduced: return new StrPagePlan(false, false, true, false);
                default: return new StrPagePlan(false, false, false, false);
            }
        }

        /// <summary>A lane stays off the board when a ticket above it already carries its proposal (Proposals mode only).</summary>
        public static bool ShowsLane(StrBoardMode mode, StrFrontRow row) =>
            row != null && !(mode == StrBoardMode.Proposals && !row.Offer.IsNone);
    }
}
