using System;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>
    /// The director's per-objective resistance when the faction's own threat picture is ready.
    /// Held ground keeps the live contact count (the defence watch needs contacts now). Ground
    /// the faction does not hold reads its observed power when scouted; when unscouted it reads
    /// the median of the scouted targets' power (else 4), or what is already known there if that
    /// is more — never zero (spec §8: "When the area is UNSCOUTED, a prior applies: the median
    /// of scouted targets, else 4").
    /// </summary>
    internal static class AreaResistance
    {
        public const int UnscoutedPrior = 4;

        public static int Round(float power) =>
            float.IsNaN(power) || power <= 0f ? 0 : (int)Math.Round(power, MidpointRounding.AwayFromZero);

        /// <summary>Rewrites <paramref name="hostile"/> for ground with intel it does not hold; returns the prior used.</summary>
        public static int Resolve(int count, bool[] held, bool[] known, bool[] scouted, float[] power, int[] hostile,
            int[] scratch)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
                if (!held[i] && known[i] && scouted[i] && n < scratch.Length) scratch[n++] = Round(power[i]);
            int prior = UnscoutedPrior;
            if (n > 0)
            {
                Array.Sort(scratch, 0, n);
                // The upper median: with two scouted targets the staff assumes the stronger.
                prior = scratch[n / 2];
            }
            for (int i = 0; i < count; i++)
            {
                if (held[i] || !known[i]) continue;
                int observed = Round(power[i]);
                hostile[i] = scouted[i] ? observed : Math.Max(prior, observed);
            }
            return prior;
        }
    }
}
