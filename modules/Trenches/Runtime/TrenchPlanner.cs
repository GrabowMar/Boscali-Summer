using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Framework.Contracts;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Runtime
{
    /// <summary>
    /// Turns a Command front trace into natural trench positions: a Bezier chain through the
    /// sparse contour, an owned-side offset chosen for defensible ground (relief over the
    /// ground either side, dry, near its intended depth), trimmed where the terrain refuses
    /// to be dug, then grown stage by stage into a field belt. All array sizes are fixed; the
    /// pure rules live in <see cref="TrenchTraceMath"/>.
    /// </summary>
    internal static class TrenchPlanner
    {
        public const float MaximumLineLength = 2400f;
        public const float AnchorSpacing = 60f;
        public const float CorridorWidth = 80f;
        private const int MaximumRuns = 8;
        private const int MaximumLinks = 3;
        private const int MaximumSaps = 2;

        private static readonly float[] traceX = new float[FrontlineTraceLimits.MaximumPoints];
        private static readonly float[] traceZ = new float[FrontlineTraceLimits.MaximumPoints];
        private static readonly float[] traceArc = new float[FrontlineTraceLimits.MaximumPoints];
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
        /// Plans one position-length window of a trace, starting at raw trace point
        /// <paramref name="windowStartStation"/>. False means the ground, ownership or length
        /// refused a position; <paramref name="nextStation"/> is 0 once the trace is
        /// exhausted, so one bad stretch never stalls the scan. A band the terrain refuses
        /// (a beachhead) retries once deeper landward before the window is given up.
        /// <paramref name="foliageAt"/> and <paramref name="roadDistanceAt"/> nudge the route
        /// toward forest edges and off road surfaces; null keeps the relief-only siting.
        /// </summary>
        public static bool TryPlanWindow(int lineId, string name, FactionHQ owner, float pressure,
            FrontlineTracePoint[] points, int pointOffset, int pointCount, int windowStartStation,
            ITerritoryIngress territory, out TrenchLine line, out int nextStation, out TrenchRefusal refusal,
            Func<float, float, bool> foliageAt = null, Func<float, float, float> roadDistanceAt = null)
        {
            line = null;
            nextStation = 0;
            refusal = TrenchRefusal.None;
            if (points == null || territory == null || owner == null || pointCount < 2)
            {
                refusal = TrenchRefusal.TooShort;
                return false;
            }

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
                traceArc[i] = traceLength;
            }
            if (traceLength < TrenchTraceMath.MinRunLength)
            {
                refusal = TrenchRefusal.TooShort;
                return false;
            }

            // A window is one position: the raw contour points it spans, up to
            // MaximumLineLength of front, resampled at the curve station spacing. Resampling
            // the whole trace at once (the first shape of this planner) had to widen the
            // spacing to traceLength / MaximumCurvePoints to fit the station buffer — about
            // 150m on a real fifty-kilometre front — so a "curve" degenerated into a handful
            // of straight slabs and the minimum run collapsed to a single station.
            if (windowStartStation < 0 || windowStartStation >= pointCount - 2) windowStartStation = 0;
            int first = windowStartStation;
            bool closed = TrenchTraceMath.IsClosed(traceX, traceZ, pointCount);
            int usable = closed ? pointCount - 1 : pointCount;
            int last = TrenchTraceMath.WindowEnd(traceArc, usable, first, MaximumLineLength);
            if (last <= first)
            {
                refusal = TrenchRefusal.NoStations;
                return false;
            }
            nextStation = last + 1 < usable ? last + 1 : 0;

            int stations = TrenchTraceMath.Resample(traceX, traceZ, first, last - first + 1,
                TrenchTraceMath.CurveSpacing, closed && first == 0 && last == usable - 1,
                stationX, stationZ);
            if (stations < 4)
            {
                refusal = TrenchRefusal.NoStations;
                return false;
            }
            int count = stations;
            float spacing = Math.Max(TrenchTraceMath.CurveSpacing,
                (traceArc[last] - traceArc[first]) / Math.Max(1, count - 1));

            // Two depth bands: the fire depth, then one band deeper landward. A beachhead
            // refuses every candidate at the fire depth (wet ground) or fragments into
            // unbuildable shreds; the second band digs it instead of dropping the window.
            int runStart = 0, runCount = 0;
            float depthCenter = TrenchTraceMath.FireDepth;
            bool placed = false;
            for (int pass = 0; pass < 2 && !placed; pass++)
            {
                depthCenter = TrenchTraceMath.FireDepth + pass * TrenchTraceMath.BeachFallbackExtraDepth;
                if (!SearchOffsets(territory, owner, count, depthCenter, foliageAt, roadDistanceAt,
                    out refusal))
                {
                    // Only refused ground retries deeper: a missing side never resolves landward.
                    if (pass > 0 || refusal != TrenchRefusal.NoGround) return false;
                    continue;
                }
                int runs = TrenchTraceMath.SplitRuns(valid, count,
                    Mathf.Max(1, Mathf.CeilToInt(TrenchTraceMath.MinRunLength / spacing)),
                    runStarts, runLengths, MaximumRuns);
                if (runs <= 0)
                {
                    if (pass > 0) { refusal = TrenchRefusal.NoRun; return false; }
                    continue;
                }
                int best = 0;
                for (int r = 1; r < runs; r++)
                    if (runLengths[r] > runLengths[best]) best = r;
                runStart = (int)runStarts[best];
                runCount = (int)runLengths[best];
                placed = true;
            }
            if (!placed) return false;

            var baseCurve = new Vector3[runCount];
            var inward = new Vector3[runCount];
            var offset = new float[runCount];
            var curve = new Vector3[runCount];
            var threat = new Vector3[runCount];
            float sector = (TrenchTraceMath.DepthSearchLevels - 1) * 0.5f;
            for (int j = 0; j < runCount; j++)
            {
                int s = runStart + j;
                float depth = depthCenter +
                    (route[s] - sector) * TrenchTraceMath.DepthSearchStep;
                baseCurve[j] = new Vector3(stationX[s], 0f, stationZ[s]);
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
                Diggable(territory, faction, p) && WithinCorridor(built, p);
            line = built;
            return true;
        }

        /// <summary>
        /// Owned-side offset search over the window's resampled stations (window-local
        /// indices — never offset by the window start, or the position is fitted to the
        /// wrong stretch of front and reads stale stations past the buffer's end). Five
        /// candidate depths around <paramref name="depthCenter"/>, scored by defensible
        /// ground — relief over the land either side, hollows penalised — plus distance from
        /// the intended line, smoothed by the undulation penalty so the position settles onto
        /// a crest or knoll instead of the lowest hollow it can reach. Foliage and road
        /// probes nudge the route toward forest edges and off road surfaces when present.
        /// </summary>
        private static bool SearchOffsets(ITerritoryIngress territory, FactionHQ owner, int count,
            float depthCenter, Func<float, float, bool> foliageAt,
            Func<float, float, float> roadDistanceAt, out TrenchRefusal refusal)
        {
            float sector = (TrenchTraceMath.DepthSearchLevels - 1) * 0.5f;
            int faction = owner.GetInstanceID();
            bool anySide = false, anyGround = false;
            for (int s = 0; s < count; s++)
            {
                valid[s] = false;
                TrenchTraceMath.Normal(stationX, stationZ, count, s, out float nx, out float nz);
                if (!TryInward(territory, owner, stationX[s], stationZ[s], nx, nz,
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
                anySide = true;
                inwardX[s] = ix;
                inwardZ[s] = iz;
                // The relief reference is sampled once per station (a fixed probe forward
                // into no man's land and one behind the line) rather than once per candidate,
                // so the choice between depths is driven by each candidate's own ground.
                float forwardY = ProbeHeight(stationX[s] - ix * TrenchTraceMath.ReliefProbeDistance,
                    stationZ[s] - iz * TrenchTraceMath.ReliefProbeDistance);
                float backY = ProbeHeight(stationX[s] + ix * TrenchTraceMath.ReliefProbeDistance,
                    stationZ[s] + iz * TrenchTraceMath.ReliefProbeDistance);
                for (int k = 0; k < TrenchTraceMath.DepthSearchLevels; k++)
                {
                    float depth = depthCenter + (k - sector) * TrenchTraceMath.DepthSearchStep;
                    float groundX = stationX[s] + ix * depth;
                    float groundZ = stationZ[s] + iz * depth;
                    // The chosen depth must stay on the faction's own side of the front, so a
                    // ragged trace leaves a gap instead of digging into the enemy's ground.
                    if (!TrenchTerrain.TryGround(groundX, groundZ, out Vector3 ground) ||
                        !Diggable(territory, faction, ground))
                    {
                        height[s, k] = float.NaN;
                        cost[s, k] = float.NaN;
                        continue;
                    }
                    height[s, k] = ground.y;
                    cost[s, k] = TrenchTraceMath.DefensibleCost(ground.y, forwardY, backY,
                        depth - depthCenter);
                    if (foliageAt != null)
                    {
                        bool inside = foliageAt(groundX, groundZ);
                        cost[s, k] += TrenchTraceMath.FoliageCost(inside,
                            !inside && NearFoliage(foliageAt, groundX, groundZ));
                    }
                    if (roadDistanceAt != null)
                        cost[s, k] += TrenchTraceMath.RoadCost(roadDistanceAt(groundX, groundZ));
                    anyGround = true;
                }
            }
            if (!anySide)
            {
                refusal = TrenchRefusal.NoSide;
                return false;
            }
            if (!anyGround)
            {
                refusal = TrenchRefusal.NoGround;
                return false;
            }

            for (int s = 0; s < count; s++) route[s] = -1;
            if (!TrenchTraceMath.PlanRoute(height, cost, count, TrenchTraceMath.UndulationWeight, route))
            {
                refusal = TrenchRefusal.NoGround;
                return false;
            }
            for (int s = 0; s < count; s++)
            {
                if (route[s] < 0) continue;
                valid[s] = true;
                float depth = depthCenter +
                    (route[s] - sector) * TrenchTraceMath.DepthSearchStep;
                curveX[s] = stationX[s] + inwardX[s] * depth;
                curveZ[s] = stationZ[s] + inwardZ[s] * depth;
            }
            refusal = TrenchRefusal.None;
            return true;
        }

        /// <summary>True when a forest stand touches the ground near a candidate.</summary>
        private static bool NearFoliage(Func<float, float, bool> foliageAt, float x, float z)
        {
            float d = TrenchTraceMath.FoliageEdgeProbeDistance;
            return foliageAt(x + d, z) || foliageAt(x - d, z) ||
                foliageAt(x, z + d) || foliageAt(x, z - d);
        }

        /// <summary>
        /// The side of the trace this faction actually holds: the sign of Command's signed
        /// control field, which still separates the sides inside the wide contested band a
        /// real front digs its fieldworks in.
        /// </summary>
        private static bool TryInward(ITerritoryIngress territory, FactionHQ owner, float x, float z,
            float nx, float nz, out float ix, out float iz)
        {
            int faction = owner.GetInstanceID();
            return TrenchTraceMath.TryResolveInward(x, z, nx, nz,
                (px, pz) => territory.TryGetHoldStrength(faction, px, pz, out float hold)
                    ? hold : float.NaN,
                out ix, out iz);
        }

        /// <summary>
        /// Ground this faction may dig: its own side or the near edge of its contested band.
        /// Firmer than the floor a dug line survives at (<see cref="TrenchTraceMath.HoldsOwnSide"/>),
        /// so a fresh position is not abandoned by the next sample of a moving front.
        /// </summary>
        private static bool Diggable(ITerritoryIngress territory, int faction, Vector3 ground)
            => territory.TryGetHoldStrength(faction, ground.x, ground.z, out float hold) &&
                TrenchTraceMath.CanDig(hold);

        /// <summary>Height of the terrain at a point, or NaN where the probe refuses (water, cliff).</summary>
        private static float ProbeHeight(float x, float z)
            => TrenchTerrain.TryGround(x, z, out Vector3 ground) ? ground.y : float.NaN;

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
                        if (!TryBuildBelt(line, territory, out Vector3[] support)) return false;
                        line.Support = support; // null once the stage advances without it
                    }
                    if (line.Links == null) BuildLinks(line);
                    line.SupportAnchors = SampleAnchors(line.Support, AnchorSpacing);
                    line.BeltRefusals = 0;
                    line.Stage = TrenchStage.Support;
                    return true;

                case TrenchStage.Support:
                    if (line.Redoubt == null)
                    {
                        if (!TryBuildBelt(line, territory, out Vector3[] redoubt)) return false;
                        line.Redoubt = redoubt;
                    }
                    line.RedoubtAnchors = SampleAnchors(line.Redoubt, AnchorSpacing);
                    line.BeltRefusals = 0;
                    line.Stage = TrenchStage.Redoubt;
                    return true;

                case TrenchStage.Redoubt:
                    if (line.Spurs == null && !TryBuildSaps(line, territory) && !NoteBeltRefusal(line)) return false;
                    line.BeltRefusals = 0;
                    line.Stage = TrenchStage.Saps;
                    return true;

                default:
                    return false; // A finished position adds nothing else.
            }
        }

        /// <summary>
        /// The belt trace the current stage adds, tried down the depth ladder: doctrine depth
        /// first, then shallower rungs, so a position on a narrow band still gets a rear line
        /// closer in. True with a null trace once the stage has refused often enough to
        /// advance without it: the belt never holds the defender budget hostage.
        /// </summary>
        private static bool TryBuildBelt(TrenchLine line, ITerritoryIngress territory, out Vector3[] trace)
        {
            trace = null;
            for (int rung = 0; rung < TrenchTraceMath.BeltLadderLength; rung++)
            {
                float depth = TrenchTraceMath.BeltDepth(line.Stage, rung);
                if (float.IsNaN(depth)) break;
                if (TryBuildBeltTrace(line, territory, depth - TrenchTraceMath.FireDepth, out trace)) return true;
            }
            trace = null;
            return NoteBeltRefusal(line);
        }

        /// <summary>Counts one refusal; true when the stage may now advance without its trace.</summary>
        private static bool NoteBeltRefusal(TrenchLine line)
        {
            line.BeltRefusals++;
            return TrenchTraceMath.AdvancesWithoutBelt(line.BeltRefusals);
        }

        /// <summary>
        /// A trace parallel to the fire line, further onto the owned side, trimmed to the
        /// ground that accepts it. A rear trace may be shorter than the fire line.
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
                if (!Diggable(territory, faction, ground)) continue;
                curveX[i] = ground.x;
                curveZ[i] = ground.z;
                height[i, 0] = ground.y;
                valid[i] = true;
            }

            int runs = TrenchTraceMath.SplitRuns(valid, count,
                Mathf.Max(1, Mathf.CeilToInt(TrenchTraceMath.BeltMinRunLength / Mathf.Max(1f, line.StationSpacing))),
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
                if (!Diggable(territory, faction, head)) continue;
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
