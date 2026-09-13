using System;

namespace BoscaliSummer.Features.Trenches.Domain
{
    /// <summary>
    /// Pure engine-free mathematical rules for trench zigzag traverses, flank hooks,
    /// and tactical stage progression. Unit-testable without the game running.
    /// </summary>
    internal static class TrenchTacticalMath
    {
        public const float ConstructionSuppressionSeconds = 60f;
        public static int DefenderBudget(int stage) => stage >= 3 ? 6 : stage >= 2 ? 4 : 2;
        public static bool CanConstruct(bool overrun, float now, float suppressedUntil, float nextGrowth)
            => !overrun && now >= suppressedUntil && now >= nextGrowth;
        public static bool IsBuildableGround(float heightAboveSea, float normalY)
            => !float.IsNaN(heightAboveSea) && !float.IsInfinity(heightAboveSea) &&
                heightAboveSea > 2f && normalY >= 0.985f && normalY <= 1f;

        public const float DefaultSegmentLength = 7.5f;
        public const float MinSappingDistance = 8.0f;
        public const float MaxSappingDistance = 55.0f;
        public const float DefaultFlankHookDistance = 16.0f;

        // Frontline belt layout: a network is a sector garrison, not a cluster.
        public const float FrontBaySpacing = 22f;
        public const int SeedBayCount = 7;
        public const float SupportLineDepth = 58f;
        public const float RearLineDepth = 116f;
        public const float MaxFlankHalfLength = 176f;
        public const float MinFlankHalfLength = 72f;
        public const float PathClearance = 4.8f;

        /// <summary>Lateral offset of bay <paramref name="index"/> in a symmetric line of <paramref name="bays"/>.</summary>
        public static float LineOffset(int index, int bays)
            => (index - (bays - 1) * 0.5f) * FrontBaySpacing;

        public static float LineSpan(int bays) => Math.Max(0, bays - 1) * FrontBaySpacing;

        /// <summary>Frontline half-width a network may fortify, bounded for terrain and performance.</summary>
        public static float CapFlankLimit(float siteHalfLength)
            => Math.Min(MaxFlankHalfLength, Math.Max(MinFlankHalfLength, siteHalfLength - 40f));

        /// <summary>
        /// Calculates the alternating lateral traverse displacement for a given bay index.
        /// Alternates between forward parapet crests and rearward parados turns.
        /// </summary>
        public static float ComputeZigzagOffset(int bayIndex, float totalDistance, float maxOffset = 3.2f)
        {
            if (totalDistance < 10.0f) return 0f;

            float baseOffset = Math.Min(maxOffset, totalDistance * 0.25f);
            // Odd bays displace forward (+), even bays displace rearward (-)
            float sign = (bayIndex % 2 == 1) ? 1.0f : -0.6f;
            return baseOffset * sign;
        }

        /// <summary>
        /// Calculates the number of traverse bays for a given edge length.
        /// </summary>
        public static int ComputeBayCount(float totalDistance, float segmentLength = DefaultSegmentLength)
        {
            if (totalDistance < 10.0f) return 1;
            return Math.Max(2, (int)Math.Round(totalDistance / segmentLength));
        }

        /// <summary>
        /// Determines if two positions are within sapping and linking distance.
        /// </summary>
        public static bool IsSappingEligible(float distance, float minRange = MinSappingDistance, float maxRange = MaxSappingDistance)
        {
            return distance >= minRange && distance <= maxRange;
        }

        /// <summary>
        /// Calculates the coordinate of a rearward flank-hook to prevent enfilade attacks on dead-ends.
        /// </summary>
        public static void ComputeFlankHook(
            float originX, float originZ,
            float threatX, float threatZ,
            float hookDistance,
            out float hookX, out float hookZ)
        {
            float len = (float)Math.Sqrt(threatX * threatX + threatZ * threatZ);
            if (len < 0.001f)
            {
                threatX = 0f;
                threatZ = 1f;
                len = 1f;
            }

            float normThreatX = threatX / len;
            float normThreatZ = threatZ / len;

            // Hook projects rearward (-threat)
            hookX = originX - normThreatX * hookDistance;
            hookZ = originZ - normThreatZ * hookDistance;
        }

        /// <summary>
        /// Evaluates whether a trench network graph is complete enough to attempt the next lifecycle stage.
        /// The simulator owns the actual additions; this is the pure precondition gate.
        /// </summary>
        public static int EvaluateNextStage(int currentStage, int nodeCount, int edgeCount, int fortifiedBunkers)
        {
            switch (currentStage)
            {
                case 0: // Stage 0 (Scrapes) -> Stage 1 (Crawl) when connected
                    if (edgeCount >= Math.Max(1, nodeCount - 1)) return 1;
                    return 0;

                case 1: // Stage 1 (Crawl) -> Stage 2 (Fire Trench): the seed line is connected
                    if (nodeCount >= SeedBayCount && edgeCount >= nodeCount - 1) return 2;
                    return 1;

                case 2: // Stage 2 (Fire Trench) -> Stage 3 (Hardened): line connected, ready to extend
                    if (nodeCount >= SeedBayCount && edgeCount >= nodeCount - 1) return 3;
                    return 2;

                case 3: // Stage 3 (Hardened) -> Stage 4 (Integrated): both flanks extended
                    if (nodeCount >= 9 && edgeCount >= 8) return 4;
                    return 3;

                case 4: // Stage 4 (Integrated) -> Stage 5 (Redoubt): support line linked, dugout built
                    if (nodeCount >= 12 && edgeCount >= 13 && fortifiedBunkers >= 1) return 5;
                    return 4;

                default:
                    return currentStage;
            }
        }
    }
}
