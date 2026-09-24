using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>
    /// How a concluded offensive reads in the staff log. Mirrors the STR board's own
    /// outcome words (Command's TheaterReadout): the two cannot share code across the
    /// module boundary, so they share the exact strings instead — change both or neither.
    /// </summary>
    internal static class DirectorWords
    {
        public static string OutcomeWord(TheaterOperationOutcome outcome)
        {
            switch (outcome)
            {
                case TheaterOperationOutcome.ObjectiveSecured: return "SECURED";
                case TheaterOperationOutcome.ObjectiveLost: return "OBJECTIVE CLOSED";
                case TheaterOperationOutcome.Stalled: return "STALLED";
                case TheaterOperationOutcome.CommitmentSpent: return "COMMITMENT SPENT";
                case TheaterOperationOutcome.Cancelled: return "CANCELLED";
                default: return "CONCLUDED";
            }
        }
    }
}
