using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Fronts;

namespace BoscaliSummer.Modules.Support.Domain.Sof
{
    /// <summary>
    /// DIRECT ACTION (SOF R5): a ready team raids a revealed high-value enemy target (a SAM or radar ground unit, or an enemy anchor) within 2 km of the aim. The raid takes 45 s divided by the power, then one roll:
    /// odds = 0.55 x power + 0.05 per own team afield, clamped 20 % .. 95 %. Success destroys the target; failure wounds the team the way a failed SABOTAGE does. Pure; <see cref="SofDesk.Raid"/> carries it.
    /// </summary>
    internal static class DirectAction
    {
        public const float Reach = 2000f, BaseSeconds = 45f;

        /// <summary>45 s at power 1, 30 s at 1.5, 75 s at 0.6.</summary>
        public static float Seconds(float power) => BaseSeconds / Sane(power);

        /// <summary>The roll, as the whole percent the desk keeps on the team.</summary>
        public static int OddsPercent(float power, int teamsAfield) =>
            (int)Math.Round(PerkPower.ClampOdds(0.55f * Sane(power) + 0.05f * Math.Max(0, teamsAfield)) * 100f, MidpointRounding.AwayFromZero);

        /// <summary>The revealed target nearest the aim that <paramref name="highValue"/> accepts and lies within <see cref="Reach"/> of it. False when there is none.</summary>
        public static bool TryNearest(IReadOnlyList<SofTarget> revealed, Func<SofTarget, bool> highValue, float x, float z, out SofTarget best)
        {
            best = default;
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; revealed != null && i < revealed.Count; i++)
            {
                SofTarget t = revealed[i];
                if (t.Kind != TargetKind.Ground && t.Kind != TargetKind.Anchor) continue;
                if (highValue != null && !highValue(t)) continue;
                float d = SofRules.Distance(t.X, t.Z, x, z);
                if (d > Reach || d >= bestD) continue;
                bestD = d; best = t; found = true;
            }
            return found;
        }

        private static float Sane(float power) => float.IsNaN(power) || float.IsInfinity(power) || power <= 0f ? 1f : Math.Max(0.6f, Math.Min(1.5f, power));
    }
}
