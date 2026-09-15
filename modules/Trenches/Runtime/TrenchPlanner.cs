using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Turns a Command front trace into natural trench positions: a Bezier chain through the
    /// sparse contour, an owned-side offset chosen for the ground it finds (low, level, dry),
    /// trimmed where the terrain refuses to be dug, then grown stage by stage into a field
    /// belt. All array sizes are fixed; the pure rules live in <see cref="TrenchTraceMath"/>.
    /// </summary>
    internal static class TrenchPlanner
    {
        public const float MaximumLineLength = 1200f;
        public const float AnchorSpacing = 60f;
        public const float CorridorWidth = 60f;
        private const float OwnedProbe = 40f;
        private const float OwnedProbeDeep = 160f;
        private const int MaximumRuns = 8;
        private const int MaximumLinks = 3;
        private const int MaximumSaps = 2;

        private static readonly float[] traceX = new float[FrontlineTraceLimits.MaximumPoints];
        private static readonly float[] traceZ = new float[FrontlineTraceLimits.MaximumPoints];
        private static readonly float[] stationX = new float[TrenchLine.MaximumCurvePoints];
        private static readonly float[] stationZ = new float[TrenchLine.MaximumCurvePoints];
        private static readonly float[] inwardX = new float[TrenchLine.MaximumCurvePoints];
        private static readonly float[] inwardZ = new float[TrenchLine.MaximumCurvePoints];
        private static readonly float[] curveX = new float[TrenchLine.MaximumCurvePoints];
        private static readonly float[] curveZ = new float[TrenchLine.MaximumCurvePoints];
        private static readonly float[,] height = new float[TrenchLine.MaximumCurvePoints, TrenchTraceMath.DepthSearchLevels];
        private static readonly float[,] cost = new float[TrenchLine.MaximumCurvePoints, TrenchTraceMath.DepthSearchLevels];
        private static readonly int[] route = new int[TrenchLine.MaximumCurvePoints];
        private static readonly bool[] valid = new bool[TrenchLine.MaximumCurvePoints];
        private static readonly float[] runStarts = new float[MaximumRuns];
        private static readonly float[] runLengths = new float[MaximumRuns];
        private static readonly List<Vector3> anchorScratch = new List<Vector3>(64);

        /// <summary>
        /// Plans one position-length window of a trace, starting at
        /// <paramref name="windowStartStation"/> of the resampled curve. False means the
        /// ground, ownership or length refused a position; <paramref name="nextStation"/>
        /// is 0 once the trace is exhausted, so one bad stretch never stalls the scan.
        /// </summary>
        public static bool TryPlanWindow(int lineId, string name, FactionHQ owner, float pressure,
            FrontlineTracePoint[] points, int pointOffset, int pointCount, int windowStartStation,
            ITerritoryIngress territory, out TrenchLine line, out int nextStation)
        {
            line = null;
            nextStation = 0;
            if (points == null || territory == null || owner == null || pointCount < 2) return false;

            pointCount = Math.Min(pointCount, traceX.Length);
            float traceLength = 0f;
            for (int i = 0; i < pointCount; i++)
            {
                traceX[i] = points[pointOffset + i].X;
                traceZ[i] = points[pointOffset + i].Z;
                if (i > 0)
                {
                    float dx = traceX[i] - traceX[i - 1], dz = traceZ[i] - traceZ[i - 1];
                    traceLength += (float)Math.Sqrt(dx * dx + dz * dz);
                }
            }
            if (traceLength < TrenchTraceMath.MinRunLength) return false;

            float spacing = Math.Max(TrenchTraceMath.CurveSpacing, traceLength / TrenchLine.MaximumCurvePoints);
            bool closed = TrenchTraceMath.IsClosed(traceX, traceZ, pointCount);
            int stations = TrenchTraceMath.Resample(traceX, traceZ, pointCount, spacing, closed,
                stationX, stationZ);
            if (stations < 4) return false;

            int windowStations = Mathf.Clamp(Mathf.RoundToInt(MaximumLineLength / spacing), 4,
                TrenchLine.MaximumCurvePoints);
            if (windowStartStation < 0 || windowStartStation >= stations - 2) windowStartStation = 0;
            int first = windowStartStation;
            int last = Math.Min(stations - 1, first + windowStations - 1);
            int count = last - first + 1;
            nextStation = last + 1 < stations - 2 ? last + 1 : 0;
            if (count < 4) return false;

            if (!SearchOffsets(territory, owner, first, count, stations)) return false;

            int runs = TrenchTraceMath.SplitRuns(valid, count,
                Mathf.Max(1, Mathf.CeilToInt(TrenchTraceMath.MinRunLength / spacing)),
                runStarts, runLengths, MaximumRuns);
            if (runs <= 0) return false;

            int best = 0;
            for (int r = 1; r < runs; r++)
                if (runLengths[r] > runLengths[best]) best = r;
            int runStart = (int)runStarts[best];
            int runCount = (int)runLengths[best];

            var baseCurve = new Vector3[runCount];
            var inward = new Vector3[runCount];
            var offset = new float[runCount];
            var curve = new Vector3[runCount];
            var threat = new Vector3[runCount];
            float sector = (TrenchTraceMath.DepthSearchLevels - 1) * 0.5f;
            for (int j = 0; j < runCount; j++)
            {
                int s = runStart + j;
                int i = first + s;
                float depth = TrenchTraceMath.FireDepth +
                    (route[s] - sector) * TrenchTraceMath.DepthSearchStep;
                baseCurve[j] = new Vector3(stationX[i], 0f, stationZ[i]);
                inward[j] = new Vector3(inwardX[s], 0f, inwardZ[s]);
                offset[j] = depth;
                curve[j] = new Vector3(curveX[s], height[s, route[s]], curveZ[s]);
                threat[j] = new Vector3(-inwardX[s], 0f, -inwardZ[s]);
            }

            TrenchLine built = new TrenchLine(lineId, name, owner, pressure, spacing, baseCurve, inward, offset,
                curve, threat)
            {
                Anchors = SampleAnchors(curve, AnchorSpacing)
            };
            int faction = owner.GetInstanceID();
            built.Validator = p => TrenchTerrain.TryGround(p, out _) &&
                territory.OwnsPosition(faction, p.x, p.z) && WithinCorridor(built, p);
            line = built;
            return true;
        }

        /// <summary>
        /// Owned-side offset search: five candidate depths around the fire depth, scored by
        /// ground height plus distance from the intended line, smoothed by the undulation
        /// penalty so the position settles into the flattest low ground it can reach.
        /// </summary>
        private static bool SearchOffsets(ITerritoryIngress territory, FactionHQ owner, int first,
            int count, int stations)
        {
            float sector = (TrenchTraceMath.DepthSearchLevels - 1) * 0.5f;
            int faction = owner.GetInstanceID();
            bool any = false;
            for (int s = 0; s < count; s++)
            {
                valid[s] = false;
                int i = first + s;
                TrenchTraceMath.Normal(stationX, stationZ, stations, i, out float nx, out float nz);
                if (!TryInward(territory, owner, stationX[i], stationZ[i], nx, nz,
                    out float ix, out float iz))
                {
                    // Stale rows from a previous plan must never be routed through.
                    for (int k = 0; k < TrenchTraceMath.DepthSearchLevels; k++)
                    {
                        height[s, k] = float.NaN;
                        cost[s, k] = float.NaN;
                    }
                    continue;
                }
                inwardX[s] = ix;
                inwardZ[s] = iz;
                for (int k = 0; k < TrenchTraceMath.DepthSearchLevels; k++)
                {
                    float depth = TrenchTraceMath.FireDepth + (k - sector) * TrenchTraceMath.DepthSearchStep;
                    float groundX = stationX[i] + ix * depth;
                    float groundZ = stationZ[i] + iz * depth;
                    // The chosen depth must stay on the faction's own ground, so a ragged
                    // front leaves a gap instead of digging into the enemy's cell.
                    if (!TrenchTerrain.TryGround(groundX, groundZ, out Vector3 ground) ||
                        !territory.OwnsPosition(faction, ground.x, ground.z))
                    {
                        height[s, k] = float.NaN;
                        cost[s, k] = float.NaN;
                        continue;
                    }
                    height[s, k] = ground.y;
                    cost[s, k] = ground.y + TrenchTraceMath.DepthStayWeight *
                        (depth - TrenchTraceMath.FireDepth) * (depth - TrenchTraceMath.FireDepth);
                    any = true;
                }
            }
            if (!any) return false;

            for (int s = 0; s < count; s++) route[s] = -1;
            if (!TrenchTraceMath.PlanRoute(height, cost, count, TrenchTraceMath.UndulationWeight, route))
                return false;
            for (int s = 0; s < count; s++)
            {
                if (route[s] < 0) continue;
                valid[s] = true;
                float depth = TrenchTraceMath.FireDepth +
                    (route[s] - sector) * TrenchTraceMath.DepthSearchStep;
                curveX[s] = stationX[first + s] + inwardX[s] * depth;
                curveZ[s] = stationZ[first + s] + inwardZ[s] * depth;
            }
            return true;
        }

        /// <summary>The side of the trace this faction actually holds, 40m out.</summary>
        private static bool TryInward(ITerritoryIngress territory, FactionHQ owner, float x, float z,
            float nx, float nz, out float ix, out float iz)
        {
            ix = nx;
            iz = nz;
            int id = owner.GetInstanceID();
            bool positive = territory.OwnsPosition(id, x + nx * OwnedProbe, z + nz * OwnedProbe);
            bool negative = territory.OwnsPosition(id, x - nx * OwnedProbe, z - nz * OwnedProbe);
            if (positive == negative)
            {
                // The front cells themselves are usually contested (and a contested cell
                // answers neither side as owned), so the deeper cells decide.
                positive = territory.OwnsPosition(id, x + nx * OwnedProbeDeep, z + nz * OwnedProbeDeep);
                negative = territory.OwnsPosition(id, x - nx * OwnedProbeDeep, z - nz * OwnedProbeDeep);
                if (positive == negative) return false;
            }
            if (negative)
            {
                ix = -nx;
                iz = -nz;
            }
            return true;
        }

        /// <summary>
        /// Lays out the ground the next stage adds. Every stage is atomic: an invalid belt
        /// leaves the position untouched and the stage retries on the next growth tick.
        /// </summary>
        public static bool TryGrowBelt(TrenchLine line, ITerritoryIngress territory)
        {
            if (line == null || territory == null) return false;
            switch (line.Stage)
            {
                case TrenchStage.Scrape:
                    line.Stage = TrenchStage.FireTrench;
                    return true;

                case TrenchStage.FireTrench:
                    if (line.Support == null)
                    {
                        if (!TryBuildBeltTrace(line, territory,
                            TrenchTraceMath.SupportDepth - TrenchTraceMath.FireDepth, out Vector3[] support))
                            return false;
                        line.Support = support;
                    }
                    if (line.Links == null) BuildLinks(line);
                    line.SupportAnchors = SampleAnchors(line.Support, AnchorSpacing);
                    line.Stage = TrenchStage.Support;
                    return true;

                case TrenchStage.Support:
                    if (line.Redoubt == null)
                    {
                        if (!TryBuildBeltTrace(line, territory,
                            TrenchTraceMath.RedoubtDepth - TrenchTraceMath.FireDepth, out Vector3[] redoubt))
                            return false;
                        line.Redoubt = redoubt;
                    }
                    line.RedoubtAnchors = SampleAnchors(line.Redoubt, AnchorSpacing);
                    line.Stage = TrenchStage.Redoubt;
                    return true;

                case TrenchStage.Redoubt:
                    if (line.Spurs == null && !TryBuildSaps(line, territory)) return false;
                    line.Stage = TrenchStage.Saps;
                    return true;

                default:
                    return false; // A finished position adds nothing else.
            }
        }

        /// <summary>
        /// A trace parallel to the fire line, further onto the owned side, trimmed to the
        /// ground that accepts it.
        /// </summary>
        private static bool TryBuildBeltTrace(TrenchLine line, ITerritoryIngress territory,
            float extraDepth, out Vector3[] trace)
        {
            trace = null;
            int faction = line.OwnerHq != null ? line.OwnerHq.GetInstanceID() : 0;
            int count = line.Curve.Length;
            Array.Clear(valid, 0, count);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = line.Base[i] + line.Inward[i] * (line.Offset[i] + extraDepth);
                if (!TrenchTerrain.TryGround(p, out Vector3 ground)) continue;
                if (!territory.OwnsPosition(faction, ground.x, ground.z)) continue;
                curveX[i] = ground.x;
                curveZ[i] = ground.z;
                height[i, 0] = ground.y;
                valid[i] = true;
            }

            int runs = TrenchTraceMath.SplitRuns(valid, count,
                Mathf.Max(1, Mathf.CeilToInt(TrenchTraceMath.MinRunLength / Mathf.Max(1f, line.StationSpacing))),
                runStarts, runLengths, MaximumRuns);
            if (runs <= 0) return false;
            int best = 0;
            for (int r = 1; r < runs; r++)
                if (runLengths[r] > runLengths[best]) best = r;
            int start = (int)runStarts[best];
            int length = (int)runLengths[best];
            trace = new Vector3[length];
            for (int j = 0; j < length; j++)
                trace[j] = new Vector3(curveX[start + j], height[start + j, 0], curveZ[start + j]);
            return true;
        }

        /// <summary>Short communication trenches joining the fire and support traces.</summary>
        private static void BuildLinks(TrenchLine line)
        {
            var links = new List<Vector3[]>(MaximumLinks);
            if (line.Support != null && line.Support.Length > 1)
            {
                for (int k = 1; k <= MaximumLinks; k++)
                {
                    int i = Mathf.Clamp(line.Curve.Length * k / (MaximumLinks + 1), 0, line.Curve.Length - 1);
                    int j = Nearest(line.Support, line.Curve[i]);
                    Vector3 a = line.Curve[i];
                    Vector3 b = line.Support[j];
                    float span = Vector3.Distance(a, b);
                    if (span < 8f || span > 260f) continue;
                    Vector3 mid = (a + b) * 0.5f;
                    if (!TrenchTerrain.TryGround(mid, out Vector3 ground)) continue;
                    links.Add(new[] { a, ground, b });
                }
            }
            line.Links = links.ToArray();
        }

        /// <summary>Two listening posts pushed out of the fire line toward the enemy.</summary>
        private static bool TryBuildSaps(TrenchLine line, ITerritoryIngress territory)
        {
            var spurs = new List<Vector3[]>(MaximumSaps);
            int faction = line.OwnerHq != null ? line.OwnerHq.GetInstanceID() : 0;
            for (int k = 0; k < MaximumSaps; k++)
            {
                float fraction = 0.5f + (k - (MaximumSaps - 1) * 0.5f) * TrenchTraceMath.SapLateralFraction;
                int i = Mathf.Clamp(Mathf.RoundToInt(fraction * (line.Curve.Length - 1)), 0,
                    line.Curve.Length - 1);
                Vector3 anchor = line.Curve[i];
                Vector3 forward = line.Threat[i].normalized;
                Vector3 end = anchor + forward * TrenchTraceMath.SapDepth;
                if (!TrenchTerrain.TryGround(end, out Vector3 head)) continue;
                if (!territory.OwnsPosition(faction, head.x, head.z)) continue;
                Vector3 mid = anchor + forward * (TrenchTraceMath.SapDepth * 0.5f);
                Vector3 midGround = TrenchTerrain.TryGround(mid, out Vector3 sampled) ? sampled : mid;
                spurs.Add(new[] { anchor, midGround, head });
            }
            if (spurs.Count == 0) return false;
            line.Spurs = spurs.ToArray();
            return true;
        }

        private static int Nearest(Vector3[] path, Vector3 target)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            for (int i = 0; i < path.Length; i++)
            {
                float dSq = (path[i] - target).sqrMagnitude;
                if (dSq < bestSq) { bestSq = dSq; best = i; }
            }
            return best;
        }

        private static bool WithinCorridor(TrenchLine line, Vector3 p)
        {
            Vector3[] anchors = line.Anchors;
            if (anchors == null) return false;
            float limit = CorridorWidth * CorridorWidth;
            for (int i = 0; i < anchors.Length; i++)
            {
                Vector3 delta = anchors[i] - p;
                delta.y = 0f;
                if (delta.sqrMagnitude <= limit) return true;
            }
            return false;
        }

        /// <summary>Anchors roughly every <paramref name="spacing"/> metres of ditch.</summary>
        public static Vector3[] SampleAnchors(Vector3[] curve, float spacing)
        {
            if (curve == null || curve.Length == 0) return Array.Empty<Vector3>();
            anchorScratch.Clear();
            anchorScratch.Add(curve[0]);
            float since = 0f;
            for (int i = 1; i < curve.Length && anchorScratch.Count < 64; i++)
            {
                since += Vector3.Distance(curve[i - 1], curve[i]);
                if (since < spacing) continue;
                anchorScratch.Add(curve[i]);
                since = 0f;
            }
            Vector3 last = curve[curve.Length - 1];
            if (Vector3.Distance(anchorScratch[anchorScratch.Count - 1], last) > spacing * 0.5f)
                anchorScratch.Add(last);
            return anchorScratch.ToArray();
        }
    }
}
