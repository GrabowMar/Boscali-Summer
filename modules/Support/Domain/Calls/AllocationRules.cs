using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    /// <summary>
    /// The pure rules of paying a perk in vanilla allocation (<c>Player.Allocation</c>): the host spends before it executes and gives the same
    /// amount back when the action fails. The runtime applies the results with <c>Player.SetAllocation</c>.
    /// </summary>
    internal static class AllocationRules
    {
        private const float Slack = 0.001f;

        public static bool CanAfford(float balance, float cost) =>
            !float.IsNaN(balance) && !float.IsInfinity(balance) && !float.IsNaN(cost) && !float.IsInfinity(cost) && cost >= 0f && balance + Slack >= cost;

        public static float AfterSpend(float balance, float cost) => Math.Max(0f, balance - cost);

        public static float AfterRefund(float balance, float amount) => balance + Math.Max(0f, amount);
    }
}
