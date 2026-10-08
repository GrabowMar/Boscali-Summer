using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Per-station rounds from a mass-limited stock: the vanilla Rearmer allocation, minus nukes in flight.</summary>
    internal static class RearmAllocation
    {
        public struct StationNeed
        {
            public int Missing;
            public float MassPerRound;
            public float CostPerRound;
            public bool Skip; // cargo, massless or nuclear stations
        }

        public static int[] Allocate(IList<StationNeed> needs, ref float stockKg, ref float funds, bool free)
        {
            var rounds = new int[needs.Count];
            for (int i = 0; i < needs.Count; i++)
            {
                StationNeed n = needs[i];
                if (n.Skip || n.Missing <= 0 || n.MassPerRound <= 0f) continue;
                int byMass = Mathf.FloorToInt(stockKg / n.MassPerRound);
                int byFunds = free || n.CostPerRound <= 0f ? int.MaxValue : Mathf.FloorToInt(funds / n.CostPerRound);
                int r = Mathf.Max(0, Mathf.Min(n.Missing, Mathf.Min(byMass, byFunds)));
                rounds[i] = r;
                stockKg -= r * n.MassPerRound;
                if (!free) funds -= r * n.CostPerRound;
            }
            return rounds;
        }
    }
}
