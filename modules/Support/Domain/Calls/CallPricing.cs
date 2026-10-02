using System;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal readonly struct PriceInputs
    {
        public readonly float ObjectiveShare;   // 0..1, NaN when unknown
        public readonly int ObjectiveCount;     // N capturable objectives
        public readonly bool Degraded;          // a family anchor is down (+40 %)
        public readonly string DegradedReason;  // chip words, e.g. "UPLINK DOWN"
        public readonly bool Exploit;           // counter-triangle advantage (-25 %)
        public readonly float EventMultiplier;  // Events module cost factor, 1 = none
        public readonly float PerkMultiplier;   // personal perk factor, 1 = none, never a chip
        public readonly float Knob;             // master price knob, 1 = none, never a chip

        public PriceInputs(float objectiveShare, int objectiveCount, bool degraded, string degradedReason, bool exploit,
            float eventMultiplier, float perkMultiplier, float knob)
        {
            ObjectiveShare = objectiveShare;
            ObjectiveCount = objectiveCount;
            Degraded = degraded;
            DegradedReason = degradedReason;
            Exploit = exploit;
            EventMultiplier = eventMultiplier;
            PerkMultiplier = perkMultiplier;
            Knob = knob;
        }
    }

    internal readonly struct CallQuote
    {
        public readonly int Cost;
        public readonly string Reason; // "" or one chip naming the largest visible modifier

        public CallQuote(int cost, string reason)
        {
            Cost = cost;
            Reason = reason;
        }
    }

    /// <summary>Core §5.1 / §6.3: base price × UNDERDOG × degraded × exploit × event × perk × knob, one reason chip.</summary>
    internal static class CallPricing
    {
        public const float DegradedFactor = 0.40f, ExploitFactor = -0.25f;

        public static float Underdog(float share, int n)
        {
            if (n <= 0 || float.IsNaN(share) || float.IsInfinity(share)) return 0f;
            share = Math.Max(0f, Math.Min(1f, share));
            float m = share < 0.5f ? -0.30f * (0.5f - share) / 0.5f : 0.20f * (share - 0.5f) / 0.5f;
            if (n <= 3) m = Math.Max(-0.15f, Math.Min(0.10f, m));
            return m;
        }

        public static CallQuote Quote(CallTier tier, in PriceInputs p)
        {
            float underdog = Underdog(p.ObjectiveShare, p.ObjectiveCount);
            float ev = Sane(p.EventMultiplier);
            float price = CallSheet.BasePrice(tier) * (1f + underdog)
                * (p.Degraded ? 1f + DegradedFactor : 1f)
                * (p.Exploit ? 1f + ExploitFactor : 1f)
                * ev * Sane(p.PerkMultiplier) * Sane(p.Knob);
            int cost = Math.Max(1, (int)Math.Round(price, MidpointRounding.AwayFromZero));

            string reason = "";
            float best = 0.005f;
            Consider(underdog, underdog < 0f ? "UNDERDOG" : "LEADING", ref best, ref reason);
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
