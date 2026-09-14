using System;

namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>Pure pay-out rules. All of it is funds and score only; nothing here touches units.</summary>
    internal static class CommandEconomy
    {
        public const int MaximumMarks = 2;

        public static int BountyFunds(int tier, bool marked, int markedPercent,
            int baseFunds, int componentFunds, int theaterFunds, float traitMultiplier)
        {
            int funds = tier == CommandTier.Theater ? theaterFunds
                      : tier == CommandTier.Component ? componentFunds
                      : baseFunds;
            float value = funds * Math.Max(0.1f, traitMultiplier);
            if (marked) value *= 1f + Math.Max(0, markedPercent) / 100f;
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return (int)Math.Round(value);
        }

        public static int BountyScore(int tier) =>
            tier == CommandTier.Theater ? 20 : tier == CommandTier.Component ? 10 : 5;

        public static int Stipend(int baseAmount, float liveWeight, float cohesion)
        {
            if (baseAmount <= 0 || liveWeight <= 0f) return 0;
            float scale = 0.6f + 0.4f * Clamp01(cohesion);
            float value = baseAmount * liveWeight * scale;
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return (int)Math.Round(value);
        }

        public static int IntervalCommandPoints(float cohesion, int politicalCount)
        {
            int points = cohesion >= 0.75f ? 1 : 0;
            points += Math.Max(0, politicalCount);
            return points;
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
