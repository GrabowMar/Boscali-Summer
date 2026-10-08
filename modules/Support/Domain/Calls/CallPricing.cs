using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal readonly struct PriceInputs
    {
        public readonly bool Degraded;          // a family anchor is down (+40 %)
        public readonly string DegradedReason;  // chip words, e.g. "UPLINK DOWN"
        public readonly bool Exploit;           // counter-triangle advantage (-25 %)
        public readonly float EventMultiplier;  // Events module cost factor, 1 = none
        public readonly float PerkMultiplier;   // personal progression perk factor, 1 = none, never a chip
        public readonly float Scale;            // PerkPriceScale, 1 = spec prices, never a chip

        public PriceInputs(bool degraded, string degradedReason, bool exploit, float eventMultiplier, float perkMultiplier, float scale)
        {
            Degraded = degraded;
            DegradedReason = degradedReason;
            Exploit = exploit;
            EventMultiplier = eventMultiplier;
            PerkMultiplier = perkMultiplier;
            Scale = scale;
        }
    }

    internal readonly struct CallQuote
    {
        public readonly int Cost;      // allocation
        public readonly string Reason; // "" or one chip naming the largest visible modifier

        public CallQuote(int cost, string reason)
        {
            Cost = cost;
            Reason = reason;
        }
    }

    /// <summary>Perk price in allocation: rung base price x degraded x exploit x event x perk x PerkPriceScale, one reason chip.</summary>
    internal static class CallPricing
    {
        public const float DegradedFactor = 0.40f, ExploitFactor = -0.25f;

        public static CallQuote Quote(int rung, in PriceInputs p)
        {
            float ev = Sane(p.EventMultiplier);
            float price = CallSheet.BasePrice(rung)
                * (p.Degraded ? 1f + DegradedFactor : 1f)
                * (p.Exploit ? 1f + ExploitFactor : 1f)
                * ev * Sane(p.PerkMultiplier) * Sane(p.Scale);
            int cost = Math.Max(1, (int)Math.Round(price, MidpointRounding.AwayFromZero));

            string reason = "";
            float best = 0.005f;
            if (p.Degraded) Consider(DegradedFactor, string.IsNullOrEmpty(p.DegradedReason) ? "DEGRADED" : p.DegradedReason, ref best, ref reason);
            if (p.Exploit) Consider(ExploitFactor, "EXPLOIT", ref best, ref reason);
            Consider(ev - 1f, "EVENT", ref best, ref reason);
            return new CallQuote(cost, reason);
        }

        private static void Consider(float modifier, string label, ref float best, ref string reason)
        {
            float size = Math.Abs(modifier);
            if (size < best) return;
            best = size;
            int percent = (int)Math.Round(size * 100f, MidpointRounding.AwayFromZero);
            reason = (modifier > 0f ? "+" : "-") + percent + " % " + label;
        }

        private static float Sane(float factor) =>
            float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f ? 1f : factor;
    }
}
