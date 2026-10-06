using BoscaliSummer.Modules.Support.Domain.Ops;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The ledger seams of the AI-controlled factions (spec section 4, core 6.4 and 7a): the flat treasury seed and the objective census the "leading" test reads.</summary>
    internal sealed partial class SupportManager
    {
        /// <summary>
        /// A faction with no humans has nobody to feed HQ FUND, so it earns a flat seed (40 CR a minute, capped) while it stays without humans. Returns what was added; a faction with a human is
        /// never seeded (the human-active gate of core 6.4 applies to it).
        /// </summary>
        internal float AiTreasurySeed(FactionHQ owner, float seconds)
        {
            if (credits == null || owner == null || HumanCount(owner) > 0) return 0f;
            int key = credits.FactionKey(owner);
            float add = AiRules.SeedFor(credits.Fund.Balance(key), seconds);
            if (add > 0f) credits.Fund.Add(key, add);
            return add;
        }

        /// <summary>
        /// The faction's share of the counted objectives (the same <see cref="ObjectiveShare"/> the CALL prices and the ASAT victim read: held plus half the contested, over every ground airbase) and the
        /// best share any other faction holds. NaN when the map has nothing to count: no lead, so no funding.
        /// </summary>
        internal void ObjectiveShares(FactionHQ owner, out float share, out float bestRival)
        {
            share = bestRival = float.NaN;
            if (credits == null || owner == null) return;
            ObjectiveCount mine = credits.Census(owner);
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
