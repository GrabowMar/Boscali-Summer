namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The one door for spending the faction pool on a convoy. Registered by TheaterOps; resolved late by callers.</summary>
    internal interface IConvoyPurchase
    {
        /// <summary>
        /// Host only. Buys the cheapest ready ground-vehicle group the pool plus <paramref name="subsidy"/> can pay for.
        /// The subsidy is credited first, the group queued, then the cost debited; on failure the subsidy is taken back.
        /// Every success writes a staff line naming <paramref name="source"/> and raises the wave callout.
        /// </summary>
        bool TryBuyCheapest(int factionInstanceId, float subsidy, string source, out string group, out int cost, out string why);
    }
}
