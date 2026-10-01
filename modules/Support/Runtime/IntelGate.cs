using System.Collections.Generic;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// One track near a strike grid: seconds since it was spotted and metres from the
    /// grid. Pure data so the gate is testable without the game.
    /// </summary>
    internal readonly struct IntelCandidate
    {
        public readonly float AgeSeconds;
        public readonly float DistanceMeters;

        public IntelCandidate(float ageSeconds, float distanceMeters)
        {
            AgeSeconds = ageSeconds;
            DistanceMeters = distanceMeters;
        }
    }

    /// <summary>
    /// FIRES intel gate (§6.1). A strike needs an HQ-known position near the grid spotted
    /// within the window; anything unknown fails closed except clock skew (a negative age
    /// reads as fresh so a clock step cannot deny a good strike).
    /// </summary>
    internal static class IntelGate
    {
        /// <summary>Most tracks examined per gate check; the scan is bounded.</summary>
        public const int MaxTracks = 256;

        /// <summary>True when any candidate is fresh and near. Rims are inside.</summary>
        public static bool AnyFresh(IReadOnlyList<IntelCandidate> candidates, float windowSeconds, float radiusMeters)
        {
            if (candidates == null || windowSeconds <= 0f || radiusMeters <= 0f) return false;
            if (!float.IsFinite(windowSeconds) || !float.IsFinite(radiusMeters)) return false;
            int count = candidates.Count < MaxTracks ? candidates.Count : MaxTracks;
            for (int i = 0; i < count; i++)
            {
                float age = candidates[i].AgeSeconds;
                float distance = candidates[i].DistanceMeters;
                if (!float.IsFinite(age) || !float.IsFinite(distance)) continue;
                if (age < 0f) age = 0f;
                if (age <= windowSeconds && distance <= radiusMeters) return true;
            }
            return false;
        }
    }
}
