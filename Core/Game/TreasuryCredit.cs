namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Mod money paid into a faction treasury (stipends, bounties, event grants). Vanilla
    /// <c>FactionHQ.DistributeFunds</c> hands every fund above <c>excessFundsThreshold</c> (the mission's starting balance)
    /// to the side's players, so a plain <c>AddFunds</c> leaked all of it into a solo pilot's allocation. Raising the
    /// threshold by the same amount keeps the credit in the treasury for AI convoys and fronts.
    /// </summary>
    internal static class TreasuryCredit
    {
        public static void Add(FactionHQ hq, float amount)
        {
            if (hq == null || !(amount > 0f)) return;
            hq.excessFundsThreshold += amount;
            hq.AddFunds(amount);
        }
    }
}
