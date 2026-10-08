using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal readonly struct ObjectiveCount
    {
        public readonly int held, contested, n;

        public ObjectiveCount(int held, int contested, int n)
        {
            this.held = held;
            this.contested = contested;
            this.n = n;
        }
    }

    /// <summary>The objective census the "leading" test of the AI factions and the ASAT victim choice read.</summary>
    internal sealed partial class SupportManager
    {
        private const float CensusSeconds = 2f;
        private readonly Dictionary<FactionHQ, (float at, ObjectiveCount count)> census =
            new Dictionary<FactionHQ, (float, ObjectiveCount)>();

        /// <summary>
        /// Ground airbases (carriers excluded): held by <paramref name="hq"/>, contested (being captured by it), total.
        /// Capturability is not readable on this build, so every non-carrier airbase counts. Cached for 2 s per faction.
        /// </summary>
        internal ObjectiveCount Census(FactionHQ hq)
        {
            if (hq == null) return default;
            float t = Time.unscaledTime;
            if (census.TryGetValue(hq, out var cached) && t - cached.at < CensusSeconds) return cached.count;

            int held = 0, contested = 0, n = 0;
            if (FactionRegistry.airbaseLookup != null)
            {
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                    n++;
                    if (airbase.CurrentHQ == hq) held++;
                    else if (airbase.capture != null && airbase.capture.capturingHQ == hq) contested++;
                }
            }
            var count = new ObjectiveCount(held, contested, n);
            census[hq] = (t, count);
            return count;
        }

        /// <summary>
        /// The faction's share of the counted objectives (the same <see cref="ObjectiveShare"/> the ASAT victim reads: held plus half the contested, over every ground airbase) and the
        /// best share any other faction holds. NaN when the map has nothing to count: no lead, so no funding.
        /// </summary>
        internal void ObjectiveShares(FactionHQ owner, out float share, out float bestRival)
        {
            share = bestRival = float.NaN;
            if (owner == null) return;
            ObjectiveCount mine = Census(owner);
            if (mine.n <= 0) return;
            share = ObjectiveShare(owner);
            var hqs = FactionRegistry.GetAllHQs();
            float best = -1f;
            if (hqs != null)
                foreach (FactionHQ hq in hqs)
                {
                    if (hq == null || hq == owner) continue;
                    float rival = ObjectiveShare(hq);
                    if (!float.IsNaN(rival)) best = Mathf.Max(best, rival);
                }
            bestRival = best < 0f ? float.NaN : best;
        }
    }
}
