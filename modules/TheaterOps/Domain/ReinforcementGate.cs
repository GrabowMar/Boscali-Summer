namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>Why a reinforcement call was allowed or refused. Order is priority order.</summary>
    internal enum ReinforcementGate
    {
        Ready,
        Cooling,
        Unaffordable,
        Disabled
    }

    /// <summary>
    /// Pure decision for funding one convoy group. Kept apart from the service so the
    /// arithmetic rule — a call is ready only when allowed, off cooldown and affordable
    /// against a real faction pool — is testable without game types.
    /// </summary>
    internal static class ReinforcementGatePolicy
    {
        internal static ReinforcementGate Evaluate(bool allowed, float funds, float cost, float cooldownSeconds)
        {
            if (!allowed) return ReinforcementGate.Disabled;
            if (cooldownSeconds > 0f) return ReinforcementGate.Cooling;
            if (float.IsNaN(funds) || float.IsNaN(cost) || !(funds >= cost)) return ReinforcementGate.Unaffordable;
            return ReinforcementGate.Ready;
        }
    }
}
