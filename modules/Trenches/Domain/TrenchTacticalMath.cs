using System;

namespace BoscaliSummer.Features.Trenches.Domain
{
    /// <summary>
    /// Pure engine-free mathematical rules for trench zigzag traverses, flank hooks,
    /// and tactical stage progression. Unit-testable without the game running.
    /// </summary>
    internal static class TrenchTacticalMath
    {
        public const float DefaultSegmentLength = 7.5f;
        public const float MinSappingDistance = 8.0f;
        public const float MaxSappingDistance = 55.0f;
        public const float DefaultFlankHookDistance = 16.0f;

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
        /// Evaluates whether a trench network meets the criteria to advance to the next lifecycle stage.
        /// </summary>
        public static int EvaluateNextStage(int currentStage, int nodeCount, int edgeCount, int fortifiedBunkers)
        {
            switch (currentStage)
            {
                case 0: // Stage 0 (Scrapes) -> Stage 1 (Crawl) when connected
                    if (edgeCount >= Math.Max(1, nodeCount - 1)) return 1;
                    return 0;

                case 1: // Stage 1 (Crawl) -> Stage 2 (Fire Trench)
                    if (edgeCount >= 2 && nodeCount >= 3) return 2;
                    return 1;

                case 2: // Stage 2 (Fire Trench) -> Stage 3 (Hardened)
                    if (fortifiedBunkers >= 1 || edgeCount >= 3) return 3;
                    return 2;

                case 3: // Stage 3 (Hardened) -> Stage 4 (Integrated)
                    if (nodeCount >= 4 && edgeCount >= 4) return 4;
                    return 3;

                default:
                    return currentStage;
            }
        }
    }
}
