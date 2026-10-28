using System;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>
    /// SIPHON (CYBER R3): funds move from the strongest enemy's front budgets into the caster's CYBER front budget. The ask is (40 + 15 per enemy node the caster holds) x power; it is capped by what
    /// the enemy has and taken from its richest budget first, then the next. Below <see cref="Minimum"/> there is nothing worth taking. Pure; the host applies the plan to the two books.
    /// </summary>
    internal static class Siphon
    {
        public const float Base = 40f, PerNode = 15f, Minimum = 10f;

        /// <summary>
        /// What a front can lose: its unspent budget plus what its programmes not yet building hold in their funding bars. The book pours a budget into the
        /// queue head every tick, so a faction that keeps programmes queued (every AI faction) has almost nothing unspent; the bars are where its funds sit.
        /// </summary>
        public static float Available(FrontSide side)
        {
            float sum = Math.Max(0f, side.Budget);
            foreach (FrontProgrammeEntry e in side.Queue) if (!e.Started) sum += Math.Max(0f, e.Funded);
            return sum;
        }

        /// <summary>Takes <paramref name="take"/> from the unspent budget first, then out of the funding bars of programmes not yet building, the head first
        /// (its next completion slips). Returns what it took.</summary>
        public static float Drain(FrontSide side, float take)
        {
            float left = Math.Max(0f, take);
            float b = Math.Min(left, Math.Max(0f, side.Budget));
            side.Budget -= b; left -= b;
            foreach (FrontProgrammeEntry e in side.Queue)
            {
                if (left <= 0f) break;
                if (e.Started || e.Funded <= 0f) continue;
                float f = Math.Min(left, e.Funded);
                e.Funded -= f; left -= f;
            }
            return take - left;
        }

        /// <summary>What the caster asks for, in whole funds.</summary>
        public static float Wanted(int heldEnemyNodes, float power) =>
            (float)Math.Floor((Base + PerNode * Math.Max(0, heldEnemyNodes)) * PerkPower.Sane(power));

        /// <summary>
        /// Fills <paramref name="takes"/> (same length as <paramref name="budgets"/>) with what to take from each budget, richest first, and returns the total (0 when it is under <see cref="Minimum"/>,
        /// in which case nothing is taken). Whole funds only: a budget's fraction stays behind.
        /// </summary>
        public static float Plan(float wanted, float[] budgets, float[] takes)
        {
            for (int i = 0; i < takes.Length; i++) takes[i] = 0f;
            float available = 0f;
            for (int i = 0; i < budgets.Length; i++) if (budgets[i] > 0f && !float.IsNaN(budgets[i])) available += (float)Math.Floor(budgets[i]);
            float total = (float)Math.Floor(Math.Min(wanted, available));
            if (!(total >= Minimum)) return 0f;
            float left = total;
            var used = new bool[budgets.Length];
            while (left > 0f)
            {
                int best = -1;
                for (int i = 0; i < budgets.Length; i++)
                    if (!used[i] && budgets[i] >= 1f && (best < 0 || budgets[i] > budgets[best])) best = i;
                if (best < 0) break;
                used[best] = true;
                float take = Math.Min(left, (float)Math.Floor(budgets[best]));
                takes[best] = take;
                left -= take;
            }
            return total - left;
        }
    }
}
