using System;

namespace BoscaliSummer.Features.Trenches.Domain
{
    /// <summary>
    /// Pure engine-free mathematical rules for trench traverses, sapping, neighbour
    /// junctions and tactical stage progression. Unit-testable without the game running.
    /// </summary>
    internal static class TrenchTacticalMath
    {
        public const float ConstructionSuppressionSeconds = 60f;
        public static int DefenderBudget(int stage) => stage >= 3 ? 4 : stage >= 2 ? 3 : 2;
        public static bool CanConstruct(bool overrun, float now, float suppressedUntil, float nextGrowth)
            => !overrun && now >= suppressedUntil && now >= nextGrowth;
        public static bool IsBuildableGround(float heightAboveSea, float normalY)
            => !float.IsNaN(heightAboveSea) && !float.IsInfinity(heightAboveSea) &&
                heightAboveSea > 2f && normalY >= 0.985f && normalY <= 1f;

        public const float MinSappingDistance = 8.0f;
        public const float MaxSappingDistance = 55.0f;

        // Frontline belt layout: one sector is a link in a continuous front line, so its
        // fire and support trenches run to the flank limit. Support and rear lines sit at
        // deliberate field-position depth (~110m and ~220m behind the fire trench).
        public const float FrontBaySpacing = 22f;
        public const int SeedBayCount = 7;
        public const float SupportLineDepth = 110f;
        public const float RearLineDepth = 220f;
        public const float MaxFlankHalfLength = 176f;
        public const float MinFlankHalfLength = 72f;
        public const float PathClearance = 4.8f;

        // Adjacent same-faction sectors extend to the flank limit and are then joined by a
        // short junction trench, so a chain of sectors forms one unbroken front.
        public const float LinkRange = MaxSappingDistance;
        public const float LinkMargin = LinkRange;

        // Forward saps: short crawl trenches pushed toward the enemy out of the front
        // line, ending in listening posts (forward observation, early warning).
        public const float ForwardLimit = 60f;
        public const float SapDepth = 34f;
        public const float SapLateralOffset = 22f;

        /// <summary>Lateral offset of bay <paramref name="index"/> in a symmetric line of <paramref name="bays"/>.</summary>
        public static float LineOffset(int index, int bays)
            => (index - (bays - 1) * 0.5f) * FrontBaySpacing;

        public static float LineSpan(int bays) => Math.Max(0, bays - 1) * FrontBaySpacing;

        /// <summary>Frontline half-width a network may fortify, bounded for terrain and performance.</summary>
        public static float CapFlankLimit(float siteHalfLength)
            => Math.Min(MaxFlankHalfLength, Math.Max(MinFlankHalfLength, siteHalfLength - 40f));

        /// <summary>Determines if two sector ends are close enough for a junction trench.</summary>
        public static bool IsSappingEligible(float distance, float minRange = MinSappingDistance, float maxRange = MaxSappingDistance)
            => distance >= minRange && distance <= maxRange;

        /// <summary>
        /// Evaluates whether a trench network graph is complete enough to attempt the next lifecycle stage.
        /// The simulator owns the actual additions; this is the pure precondition gate.
        /// </summary>
        public static int EvaluateNextStage(int currentStage, int nodeCount, int edgeCount, int fortifiedBunkers)
        {
            switch (currentStage)
            {
                case 0: // Unlinked scrapes -> a connected crawl line
                    if (edgeCount >= Math.Max(1, nodeCount - 1)) return 1;
                    return 0;

                case 1: // Crawl line -> deepened fire trench
                    if (nodeCount >= SeedBayCount && edgeCount >= nodeCount - 1) return 2;
                    return 1;

                case 2: // Fire trench -> extended across the sector
                    if (nodeCount >= SeedBayCount && edgeCount >= nodeCount - 1) return 3;
                    return 2;

                case 3: // Hardened requires the seed line extended on both flanks
                    if (nodeCount >= 9 && edgeCount >= 8) return 4;
                    return 3;

                case 4: // Integrated requires the support line linked and a dugout
                    if (nodeCount >= 12 && edgeCount >= 13 && fortifiedBunkers >= 1) return 5;
                    return 4;

                case 5: // Redoubt requires both dugouts and the whole front/support span
                    if (nodeCount >= 17 && edgeCount >= 17 && fortifiedBunkers >= 2) return 6;
                    return 5;

                default:
                    return currentStage;
            }
        }
    }
}
