using System;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>Host-owned, faction-wide infrastructure budget. Player count never changes income.</summary>
    internal sealed class FactionOpsReserve
    {
        public const float Starting = 2200f;
        public const float Maximum = 5500f;
        public const float BasePerSecond = 6f;
        public const float SitePerSecond = 0.5f;
        public const int IncomeSites = 3;

        private float lastTick = -1f;
        public float Balance { get; private set; } = Starting;

        public void Tick(float now, int ownedSites)
        {
            if (float.IsNaN(now) || float.IsInfinity(now)) return;
            if (lastTick < 0f || now < lastTick) { lastTick = now; return; }
            float elapsed = Math.Min(30f, now - lastTick);
            lastTick = now;
            Balance = Math.Min(Maximum, Balance + elapsed *
                (BasePerSecond + SitePerSecond * Math.Max(0, Math.Min(IncomeSites, ownedSites))));
        }

        public bool CanSpend(float cost) => cost >= 0f && Balance + 0.001f >= cost;

        public bool Spend(float cost)
        {
            if (!CanSpend(cost)) return false;
            Balance = Math.Max(0f, Balance - cost);
            return true;
        }

        public void Refund(float amount)
        {
            if (amount > 0f && !float.IsNaN(amount) && !float.IsInfinity(amount))
                Balance = Math.Min(Maximum, Balance + amount);
        }

        public void Clear()
        {
            Balance = Starting;
            lastTick = -1f;
        }
    }
}
