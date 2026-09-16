using System;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// What a deliberate dismissal costs. Aborting a contract the faction already accepted is
    /// a real failure and costs the faction morale; dismissing an offer or letting it expire
    /// never punishes a player who never took the job. The host applies the consequence
    /// through the existing local MoraleAwarded event; nothing here is networked.
    /// </summary>
    internal static class OperationFailure
    {
        internal const float AbortMoralePenalty = -1f;

        internal static bool DeliberateAbort(OperationState state) => state == OperationState.Active;

        internal static bool DeliberateAbort(Operation operation) =>
            operation != null && DeliberateAbort(operation.State);

        internal static string DismissalMessage(bool penalised) => penalised
            ? "Contract aborted. Faction morale -1. No money or XP."
            : "Contract dismissed. No reward, no penalty.";
    }
}
