using System;

namespace BoscaliSummer.Features.Trenches.Domain
{
    /// <summary>How far a field position has been built out.</summary>
    internal enum TrenchStage
    {
        Scrape = 0,      // Shallow hasty cut along the trace
        FireTrench = 1,  // Full fire trench with parapet, traverses and MG teams
        Support = 2,     // Support trace behind the fire line, dugouts, more works
        Redoubt = 3,     // Rear redoubt trace, weapon pits, air watch
        Saps = 4         // Forward listening posts pushed into no man's land
    }

    /// <summary>
    /// Pure engine-free rules for turning a Command front trace into a natural trench line:
    /// a smooth Bezier chain through the sparse contour, the ground-seeking offset on the
    /// owned side, the traversed ditch polyline, and the stages a position matures through.
    /// Unit-testable without the game running.
    /// </summary>
    internal static class TrenchTraceMath
    {
        // The contour arrives one point per control-grid cell, so the trace is resampled
        // into a curve before anything else touches it.
        public const float CurveSpacing = 10f;
        public const float MeshSpacing = 3.5f;
        public const float TraverseSpacing = 5.5f;
        public const float TraverseAmplitude = 1.35f;
        public const float MinRunLength = 140f;
        public const int MaximumStations = 320;

        // Belt: the fire trench sits behind the line on the owned side; the support and
        // redoubt traces sit at deliberate field depths behind it.
        public const float FireDepth = 60f;
        public const float SupportDepth = 110f;
        public const float RedoubtDepth = 220f;
        public const float DepthSearchStep = 12f;
        public const int DepthSearchLevels = 5;
        public const float DepthStayWeight = 0.045f;
        public const float UndulationWeight = 6f;
        public const float LinkSpacing = 150f;
        public const float SapDepth = 34f;
        public const float SapLateralFraction = 0.3f;
        public const float MinimumLineSpacing = 260f;

        // Validated ground: dry, level enough to dig, and a safe trench-side probe distance.
        public const float MinimumGroundHeight = 2f;
        public const float MinimumNormalY = 0.985f;
        public const float ConstructionSuppressionSeconds = 60f;

        public static bool IsBuildableGround(float heightAboveSea, float normalY)
            => !float.IsNaN(heightAboveSea) && !float.IsInfinity(heightAboveSea) &&
                heightAboveSea > MinimumGroundHeight && normalY >= MinimumNormalY && normalY <= 1f;

        /// <summary>
        /// True when a trace's ends meet: Command repeats a pocket ring's first point last,
        /// so a beachhead ring is resampled with wraparound tangents instead of a seam kink.
        /// </summary>
        public static bool IsClosed(float[] x, float[] z, int count)
        {
            if (x == null || z == null || count < 3) return false;
            float dx = x[count - 1] - x[0], dz = z[count - 1] - z[0];
            return dx * dx + dz * dz < 1f;
        }

        /// <summary>
        /// Traverse wave of a trench: -1..1 with a rounded turn every
        /// <paramref name="segmentLength"/> metres. Each half period is a cubic Bezier ease,
        /// so the bay runs straight through every turn and the traverses curl instead of
        /// cutting a corner, which is what the ditch reads as from the air.
        /// </summary>
        public static float TraverseWave(float distance, float segmentLength)
        {
            float t = distance / segmentLength;
            float corner = (float)Math.Floor(t);
            float u = t - corner;
            float ease = u * u * (3f - 2f * u);
            float value = 1f - 2f * ease;
            return ((int)corner & 1) == 0 ? value : -value;
        }

        /// <summary>
        /// Resamples a sparse trace into a smooth cubic Bezier (Catmull-Rom form) chain at
        /// roughly <paramref name="spacing"/> metres. Returns the station count written.
        /// </summary>
        public static int Resample(float[] x, float[] z, int count, float spacing, bool closed,
            float[] outX, float[] outZ)
        {
            if (x == null || z == null || outX == null || outZ == null || count < 2 ||
                outX.Length < 2 || outZ.Length < 2) return 0;
            spacing = Math.Max(1f, spacing);

            int written = 0;
            int segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                int j1 = closed ? Neighbour(i, count, true) : i;
                int j2 = closed ? Neighbour(i + 1, count, true) : i + 1;
                // Open ends reflect their phantom control point, so a straight trace stays
                // straight and evenly spaced instead of easing short at both ends.
                float x0, z0, x3, z3;
                if (closed)
                {
                    int j0 = Neighbour(i - 1, count, true), j3 = Neighbour(i + 2, count, true);
                    x0 = x[j0]; z0 = z[j0];
                    x3 = x[j3]; z3 = z[j3];
                }
                else
                {
                    x0 = i == 0 ? 2f * x[j1] - x[j2] : x[i - 1];
                    z0 = i == 0 ? 2f * z[j1] - z[j2] : z[i - 1];
                    x3 = j2 + 1 >= count ? 2f * x[j2] - x[j1] : x[j2 + 1];
                    z3 = j2 + 1 >= count ? 2f * z[j2] - z[j1] : z[j2 + 1];
                }

                float dx = x[j2] - x[j1], dz = z[j2] - z[j1];
                float length = (float)Math.Sqrt(dx * dx + dz * dz);
                int steps = Math.Max(1, (int)Math.Ceiling(length / spacing));
                for (int s = 0; s < steps && written < outX.Length; s++)
                {
                    float t = (float)s / steps;
                    CatmullRom(x0, z0, x[j1], z[j1], x[j2], z[j2], x3, z3, t,
                        out outX[written], out outZ[written]);
                    written++;
                }
            }
            if (written < outX.Length)
            {
                // The far end is the last contour point, not a Bezier extrapolation.
                int last = closed ? 0 : count - 1;
                outX[written] = x[last];
                outZ[written] = z[last];
                written++;
            }
            return written;
        }

        private static int Neighbour(int index, int count, bool closed)
        {
            if (closed) return ((index % count) + count) % count;
            return Math.Clamp(index, 0, count - 1);
        }

        private static void CatmullRom(float x0, float z0, float x1, float z1, float x2, float z2,
            float x3, float z3, float t, out float x, out float z)
        {
            float t2 = t * t, t3 = t2 * t;
            x = 0.5f * (2f * x1 + (x2 - x0) * t + (2f * x0 - 5f * x1 + 4f * x2 - x3) * t2 +
                (3f * x1 - x0 - 3f * x2 + x3) * t3);
            z = 0.5f * (2f * z1 + (z2 - z0) * t + (2f * z0 - 5f * z1 + 4f * z2 - z3) * t2 +
                (3f * z1 - z0 - 3f * z2 + z3) * t3);
        }

        /// <summary>Station normal (left-hand perpendicular) from the neighbouring stations.</summary>
        public static void Normal(float[] x, float[] z, int count, int index, out float nx, out float nz)
        {
            int before = Math.Max(0, index - 1);
            int after = Math.Min(count - 1, index + 1);
            float dx = x[after] - x[before], dz = z[after] - z[before];
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-4f) { nx = 0f; nz = 1f; return; }
            nx = -dz / length;
            nz = dx / length;
        }

        /// <summary>
        /// Routes a line through candidate positions. Each candidate carries the ground
        /// height and a position cost (low ground plus distance from the intended offset);
        /// the route minimizes that cost plus the height change between neighbouring
        /// candidates, so the line settles into the flattest, lowest corridor it can reach
        /// and stays straight where the ground is level. NaN marks ground that cannot be
        /// dug, and an unbuildable stretch breaks the line instead of failing it whole.
        /// <paramref name="route"/> receives one candidate index per station, or -1.
        /// </summary>
        public static bool PlanRoute(float[,] groundHeight, float[,] positionCost, int stations,
            float undulationWeight, int[] route)
        {
            if (groundHeight == null || positionCost == null || route == null) return false;
            int candidates = groundHeight.GetLength(1);
            if (stations <= 0 || candidates == 0 || route.Length < stations) return false;

            var cumulative = new float[stations, candidates];
            var back = new int[stations, candidates];
            for (int k = 0; k < candidates; k++)
            {
                cumulative[0, k] = RouteValid(groundHeight, positionCost, 0, k)
                    ? positionCost[0, k] : float.PositiveInfinity;
                back[0, k] = -1;
            }

            for (int s = 1; s < stations; s++)
            {
                for (int k = 0; k < candidates; k++)
                {
                    cumulative[s, k] = float.PositiveInfinity;
                    back[s, k] = -1;
                    if (!RouteValid(groundHeight, positionCost, s, k)) continue;
                    for (int j = 0; j < candidates; j++)
                    {
                        float prefix = cumulative[s - 1, j];
                        if (float.IsPositiveInfinity(prefix)) continue;
                        float cost = prefix + positionCost[s, k] +
                            undulationWeight * Math.Abs(groundHeight[s, k] - groundHeight[s - 1, j]);
                        if (cost >= cumulative[s, k]) continue;
                        cumulative[s, k] = cost;
                        back[s, k] = j;
                    }
                }
            }

            for (int s = 0; s < stations; s++) route[s] = -1;
            int routed = 0;
            for (int s = stations - 1; s >= 0; s--)
            {
                if (route[s] >= 0) continue;
                int bestK = -1;
                float bestCost = float.PositiveInfinity;
                for (int k = 0; k < candidates; k++)
                    if (cumulative[s, k] < bestCost) { bestCost = cumulative[s, k]; bestK = k; }
                if (bestK < 0) continue;
                routed++;
                for (int ss = s, kk = bestK; ss >= 0 && kk >= 0 && route[ss] < 0; ss--)
                {
                    route[ss] = kk;
                    kk = back[ss, kk];
                }
            }
            return routed > 0;
        }

        private static bool RouteValid(float[,] groundHeight, float[,] positionCost, int station, int candidate)
            => !float.IsNaN(groundHeight[station, candidate]) && !float.IsNaN(positionCost[station, candidate]);

        /// <summary>
        /// Densifies the curve into ditch-mesh rings and lays the traverse wave along it.
        /// The wave is phased off the line's world position, so neighbouring lines continue
        /// one pattern instead of restarting it. Returns the ring count written.
        /// </summary>
        public static int DensifyTraversed(float[] x, float[] z, int count, float spacing,
            float traverseSpacing, float amplitude, float phase, float[] outX, float[] outZ)
        {
            if (x == null || z == null || outX == null || outZ == null || count < 2 ||
                outX.Length < 2 || outZ.Length < 2) return 0;
            spacing = Math.Max(1f, spacing);

            int written = 0;
            outX[written] = x[0];
            outZ[written] = z[0];
            written++;

            float arc = 0f;
            for (int i = 1; i < count; i++)
            {
                float dx = x[i] - x[i - 1], dz = z[i] - z[i - 1];
                float length = (float)Math.Sqrt(dx * dx + dz * dz);
                if (length < 0.0001f) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(length / spacing));
                for (int s = 1; s <= steps && written < outX.Length; s++)
                {
                    float k = (float)s / steps;
                    float offset = amplitude * TraverseWave(phase + arc + length * k, traverseSpacing);
                    outX[written] = x[i - 1] + dx * k - dz / length * offset;
                    outZ[written] = z[i - 1] + dz * k + dx / length * offset;
                    written++;
                }
                arc += length;
            }
            return written;
        }

        /// <summary>
        /// Splits a validity mask into runs no shorter than <paramref name="minStations"/>.
        /// Returns the run count written; a front interrupted by water or a cliff simply
        /// continues on the far side instead of being dropped whole.
        /// </summary>
        public static int SplitRuns(bool[] valid, int count, int minStations, float[] runStarts,
            float[] runLengths, int maximumRuns)
        {
            if (valid == null || runStarts == null || runLengths == null || count <= 0) return 0;
            int runs = 0;
            int start = -1;
            for (int i = 0; i <= count; i++)
            {
                bool ok = i < count && valid[i];
                if (ok)
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start >= 0)
                {
                    int length = i - start;
                    if (length >= minStations && runs < maximumRuns &&
                        runs < runStarts.Length && runs < runLengths.Length)
                    {
                        runStarts[runs] = start;
                        runLengths[runs] = length;
                        runs++;
                    }
                    start = -1;
                }
            }
            return runs;
        }

        public static bool CanAdvance(bool overrun, float now, float suppressedUntil, float nextGrowth)
            => !overrun && now >= suppressedUntil && now >= nextGrowth;

        public static int DefenderBudget(TrenchStage stage)
            => stage >= TrenchStage.Redoubt ? 4 : stage >= TrenchStage.Support ? 3 : stage >= TrenchStage.FireTrench ? 2 : 1;

        public static int WorksBudget(TrenchStage stage)
            => stage >= TrenchStage.Redoubt ? 8 : stage >= TrenchStage.Support ? 6 : stage >= TrenchStage.FireTrench ? 3 : 0;

        public static bool HasSupport(TrenchStage stage) => stage >= TrenchStage.Support;
        public static bool HasRedoubt(TrenchStage stage) => stage >= TrenchStage.Redoubt;
        public static bool HasSaps(TrenchStage stage) => stage >= TrenchStage.Saps;
    }
}
