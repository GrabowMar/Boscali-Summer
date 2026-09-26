using System;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// Contracts are faction work, but one pilot takes each one. That pilot alone may abort it
    /// while it runs and earns the full reward; every other faction pilot earns the host's team
    /// share. A lone pilot always earns the full reward, and a contract whose pilot has left is
    /// anyone's to abort. Pure; the host applies it.
    /// </summary>
    internal static class ContractShares
    {
        /// <summary>An offer is anyone's to dismiss; a running contract only its pilot's, unless that
        /// pilot has left or was never recorded.</summary>
        internal static bool MayCancel(OperationState state, ulong requester, ulong acceptor, bool acceptorPresent) =>
            state != OperationState.Active || acceptor == 0 || acceptor == requester || !acceptorPresent;

        /// <summary>The fraction of a contract's money and XP one faction pilot earns.</summary>
        internal static float Share(bool completer, int players, float teamShare)
        {
            if (completer || players <= 1) return 1f;
            return teamShare > 0f ? Math.Min(1f, teamShare) : 0f;
        }

        internal static int Scaled(int amount, float share) =>
            (int)Math.Round(amount * share, MidpointRounding.AwayFromZero);
    }
}
