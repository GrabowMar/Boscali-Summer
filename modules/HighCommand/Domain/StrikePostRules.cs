using System;

namespace BoscaliSummer.Modules.HighCommand.Domain
{
    /// <summary>One confirmed standing enemy post as the picker sees it. Public fields: the manager refills one reusable array.</summary>
    internal struct PostCandidate
    {
        public int Id, Tier;
        /// <summary>Metres from the reference point (the operation fix, else the nearest owned airbase); NaN when it could not be measured.</summary>
        public float Distance;
        /// <summary>The attacker's faction already took this office's bounty.</summary>
        public bool Claimed;
    }

    /// <summary>Which confirmed enemy post the next STRIKE COMMAND POST sortie offers.</summary>
    internal static class StrikePostRules
    {
        /// <summary>
        /// Index of the nearest candidate within maxMetres (NaN, zero or negative: no limit) that is not claimed and not skipped.
        /// Ties go to the higher office (the lower Tier: theater before component before base), then the lower id. An unmeasured
        /// distance counts as infinitely far. -1 when none qualifies.
        /// </summary>
        public static int Pick(PostCandidate[] candidates, int count, float maxMetres, Predicate<int> skip)
        {
            if (candidates == null) return -1;
            if (count > candidates.Length) count = candidates.Length;
            bool limited = maxMetres > 0f;
            int best = -1;
            float bestDistance = 0f;
            for (int i = 0; i < count; i++)
            {
                PostCandidate c = candidates[i];
                if (c.Claimed || (skip != null && skip(c.Id))) continue;
                float distance = float.IsNaN(c.Distance) ? float.PositiveInfinity : c.Distance;
                if (limited && distance > maxMetres) continue;
                if (best >= 0)
                {
                    PostCandidate b = candidates[best];
                    bool better = distance < bestDistance ||
                        distance == bestDistance && (c.Tier < b.Tier || c.Tier == b.Tier && c.Id < b.Id);
                    if (!better) continue;
                }
                best = i;
                bestDistance = distance;
            }
            return best;
        }
    }
}
