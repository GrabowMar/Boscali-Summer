using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>One convoy group as the quote read it. NaN in Cost or Cooldown means the native read failed and is never treated as ready.</summary>
    internal readonly struct GroupRead
    {
        internal readonly bool Eligible;
        internal readonly float Cost, Cooldown;

        internal GroupRead(bool eligible, float cost, float cooldown)
        {
            Eligible = eligible; Cost = cost; Cooldown = cooldown;
        }
    }

    /// <summary>Everything the wave gates read, as plain values. PoolFactor is 1 for a faction with a human pilot and 2 for an AI-only faction.</summary>
    internal readonly struct WaveInputs
    {
        internal readonly bool SwitchedOff, SortiesAvailable, MissionAllows, OperationLive, DepotStanding, HasGroup;
        internal readonly float Cooldown, Pool, Cost, PoolFactor;

        internal WaveInputs(bool switchedOff, bool sortiesAvailable, bool missionAllows, bool operationLive, bool depotStanding,
            bool hasGroup, float cooldown, float pool, float cost, float poolFactor)
        {
            SwitchedOff = switchedOff; SortiesAvailable = sortiesAvailable; MissionAllows = missionAllows;
            OperationLive = operationLive; DepotStanding = depotStanding; HasGroup = hasGroup;
            Cooldown = cooldown; Pool = pool; Cost = cost; PoolFactor = poolFactor;
        }
    }

    /// <summary>
    /// When a reinforcement wave may be bought and which group it is. Pure: the host reads the game into WaveInputs and GroupRead and the
    /// verdict is a tested table, so a wave is skipped for a reason the panel can print, never silently (ruling R4).
    /// </summary>
    internal static class WavePolicy
    {
        /// <summary>
        /// First failing gate in the order SwitchedOff, NoSorties, MissionForbids, NoOperation, NoDepot, NoGroup, Cooling
        /// (cooldown above 0 or NaN), PoolShort (pool below cost * factor, or NaN, negative or infinite cost, or NaN pool); else None.
        /// Pool equal to the price is ready.
        /// </summary>
        internal static WaveBlock Evaluate(in WaveInputs i)
        {
            if (i.SwitchedOff) return WaveBlock.SwitchedOff;
            if (!i.SortiesAvailable) return WaveBlock.NoSorties;
            if (!i.MissionAllows) return WaveBlock.MissionForbids;
            if (!i.OperationLive) return WaveBlock.NoOperation;
            if (!i.DepotStanding) return WaveBlock.NoDepot;
            if (!i.HasGroup) return WaveBlock.NoGroup;
            if (!Elapsed(i.Cooldown)) return WaveBlock.Cooling;
            return Affordable(i.Pool, i.Cost, i.PoolFactor) ? WaveBlock.None : WaveBlock.PoolShort;
        }

        /// <summary>
        /// From rotation index <paramref name="start"/>: the first eligible group that is off cooldown and affordable; otherwise the first
        /// eligible one, so the panel can show a price. False (index -1) when no group is eligible.
        /// </summary>
        internal static bool Choose(GroupRead[] groups, int count, int start, float pool, float poolFactor, out int index)
        {
            index = -1;
            if (groups == null) return false;
            if (count > groups.Length) count = groups.Length;
            if (count <= 0) return false;
            start = (start % count + count) % count;
            int firstEligible = -1;
            for (int step = 0; step < count; step++)
            {
                int at = (start + step) % count;
                GroupRead group = groups[at];
                if (!group.Eligible) continue;
                if (Elapsed(group.Cooldown) && Affordable(pool, group.Cost, poolFactor))
                {
                    index = at;
                    return true;
                }
                if (firstEligible < 0) firstEligible = at;
            }
            index = firstEligible;
            return firstEligible >= 0;
        }

        /// <summary>The AI timer: switch on, operation live, now at or past nextAt. An unreadable clock is never due.</summary>
        internal static bool AiTimerDue(bool switchedOn, bool operationLive, float now, float nextAt) =>
            switchedOn && operationLive && now >= nextAt;

        // A negative cooldown has elapsed; NaN compares false, so an unreadable cooldown reads as cooling.
        private static bool Elapsed(float cooldown) => cooldown <= 0f;

        // A factor below one (or NaN) is read as one, so a bad input can never make a wave cheaper than its price.
        private static bool Affordable(float pool, float cost, float factor) =>
            float.IsFinite(cost) && cost >= 0f && pool >= cost * (factor >= 1f ? factor : 1f);
    }
}
