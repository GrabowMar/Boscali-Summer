using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// One candidate for a JTAC mark: a hostile unit's position. Pure data so the
    /// selection is testable without the game; the action maps the winning index
    /// back to its unit.
    /// </summary>
    internal readonly struct MarkCandidate
    {
        public readonly float X;
        public readonly float Z;

        public MarkCandidate(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// JTAC mark resolution. The SOF team lases the nearest hostile unit (buildings
    /// included) inside the mark radius; empty ground resolves to no one and the
    /// action answers <see cref="SupportResult.NoMarkTarget"/>. PAW S1 resolves at
    /// the best-intel radius; S4 scales it down toward the floor as intel stales.
    /// </summary>
    internal static class JtacResolve
    {
        /// <summary>Mark radius at best intel quality, metres (D9).</summary>
        public const float MarkRadius = 500f;

        /// <summary>Bounded floor the radius scales toward as intel stales (D9).</summary>
        public const float MarkRadiusFloor = 150f;

        /// <summary>Most candidates examined per mark; the scan is bounded.</summary>
        public const int MaxCandidates = 64;

        /// <summary>Index of the nearest candidate within radius of the mark, or -1.</summary>
        public static int SelectNearest(IReadOnlyList<MarkCandidate> candidates, float x, float z, float radius)
        {
            if (candidates == null || radius <= 0f) return -1;
            float best = radius * radius;
            int found = -1;
            int count = candidates.Count < MaxCandidates ? candidates.Count : MaxCandidates;
            for (int i = 0; i < count; i++)
            {
                float dx = candidates[i].X - x;
                float dz = candidates[i].Z - z;
                float squared = dx * dx + dz * dz;
                if (squared <= best)
                {
                    best = squared;
                    found = i;
                }
            }
            return found;
        }

        /// <summary>Radius for an intel quality in 0..1: best radius down to the floor.</summary>
        public static float RadiusFor(float quality)
        {
            if (quality >= 1f) return MarkRadius;
            if (quality <= 0f) return MarkRadiusFloor;
            return MarkRadiusFloor + (MarkRadius - MarkRadiusFloor) * quality;
        }
    }
}
