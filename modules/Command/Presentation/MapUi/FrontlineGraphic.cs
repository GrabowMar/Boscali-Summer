using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The theater front as one vector line, drawn the way HOI4 draws a front: a dark casing with
    /// our blue on our side and their red on theirs, and teeth along it pointing into enemy ground
    /// (amber where the fighting is hot). Which side is ours is voted per trace from the sector
    /// field; a trace with no clear side keeps the plain pale line. Sampled in map pixels so it
    /// never magnifies the overlay texture into blocks.
    ///
    /// <para>Rebuilds on the sector-grid cadence, bounded by <see cref="MaximumSamples"/> and
    /// a hard vertex ceiling, hottest trace first.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DefaultExecutionOrder(101)] // Invalidate after MapUiManager commits the displayed relief pose.
    internal sealed class FrontlineGraphic : MaskableGraphic
    {
        private const int MaximumTraces = FrontlineTraceLimits.MaximumTraces;
        private const int MaximumSamples = 1200;
        private const int MaximumVertices = 16000;
        private const float StationStep = 7f;
        private const float HalfWidth = 1f;
        private const int TickEvery = 12;
        private const int VoteSegments = 8;
        private const float StripeOffset = 1.7f, StripeHalf = 1.3f, ToothHalf = 4.5f, ToothReach = 8f;

        private static readonly Color32 Casing = new Color32(4, 10, 14, 205);
        private static readonly Color32 CoolGlow = new Color32(70, 190, 220, 80);
        private static readonly Color32 HotGlow = new Color32(255, 168, 72, 130);
        private static readonly Color32 TickInk = new Color32(150, 220, 235, 190);
        private static readonly Color32 OwnStripe = Alpha(TacticalSectorGrid.FriendlyTint, 235);
        private static readonly Color32 TheirStripe = Alpha(TacticalSectorGrid.HostileTint, 235);
        private static readonly Color32 HotTooth = new Color32(255, 176, 64, 245);

        /// <summary>The front's own ink; the map legend swatch reads it from here.</summary>
        internal static readonly Color32 Ink = new Color32(228, 241, 245, 245);

        private readonly FrontlineTracePoint[] points = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] lengths = new int[MaximumTraces];
        private readonly float[] pressures = new float[MaximumTraces];
        private readonly int[] order = new int[MaximumTraces];
        private readonly int[] starts = new int[MaximumTraces + 1];
        private readonly int[] bounds = new int[MaximumTraces + 1];
        private readonly Vector2[] projected = new Vector2[FrontlineTraceLimits.MaximumPoints];
        private readonly Vector2[] stations = new Vector2[MaximumSamples];
        private readonly Vector2[] shifted = new Vector2[MaximumSamples];
        // +1: our ground lies left of the trace's direction, -1: right, 0: no clear side.
        private readonly int[] ourSide = new int[MaximumTraces];

        private TacticalSectorGrid grid;
        private ulong drawnFrontlineHash;
        private bool reliefWasDrawing;
        private int reliefRevision;

        private void LateUpdate()
        {
            bool drawing = MfdTerrainRelief.IsDrawing;
            int revision = MfdTerrainRelief.ViewRevision;
            if (drawing == reliefWasDrawing && revision == reliefRevision) return;
            reliefWasDrawing = drawing;
            reliefRevision = revision;
            SetVerticesDirty();
        }

        /// <summary>Source of the next rebuild; null draws nothing. An unchanged front skips the mesh rebuild.</summary>
        public void SetSource(TacticalSectorGrid source)
        {
            if (source == grid)
            {
                if (source == null) return;
                if (source.FrontlineHash == drawnFrontlineHash) return;
            }
            grid = source;
            drawnFrontlineHash = source != null ? source.FrontlineHash : 0UL;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (grid == null || !isActiveAndEnabled) return;

            float worldSizeX = grid.WorldSizeX, worldSizeZ = grid.WorldSizeY;
            if (!(worldSizeX > 0f) || !(worldSizeZ > 0f)) return;

            Rect rect = rectTransform.rect;
            if (rect.width < 4f || rect.height < 4f) return;

            float scale = Mathf.Abs(transform.lossyScale.x);
            if (!(scale > 1e-4f)) scale = 1f;

            int traceCount = grid.CopyFrontlineTraces(points, lengths, pressures);
            if (traceCount <= 0) return;

            // Trace i occupies lengths[i] points continuing where the previous trace ended,
            // so the point cursor must stay in the buffer's own order, not the ranked one.
            starts[0] = 0;
            for (int i = 0; i < traceCount; i++) starts[i + 1] = starts[i] + lengths[i];
            // Project once per source point; both length measurement and sampling reuse it.
            for (int i = 0; i < starts[traceCount]; i++)
                projected[i] = ToPixels(points[i], rect, worldSizeX, worldSizeZ, scale);
            for (int i = 0; i < traceCount; i++) ourSide[i] = VoteSide(starts[i], lengths[i]);

            RankTraces(traceCount);
            int stationCount = Sample(traceCount);
            Paint(vh, traceCount, stationCount, 1f / scale);
        }

        /// <summary>
        /// Which side of a trace is ours, voted in world space across a few of its segments by the
        /// cells a cell and a bit to either side. World and map share handedness, so a world left
        /// is a map left.
        /// </summary>
        private int VoteSide(int start, int count)
        {
            if (count < 2) return 0;
            float reach = Mathf.Max(250f, grid.CellSize * 1.2f);
            int vote = 0, stride = Mathf.Max(1, (count - 1) / VoteSegments);
            for (int p = start; p + 1 < start + count; p += stride)
            {
                float dx = points[p + 1].X - points[p].X, dz = points[p + 1].Z - points[p].Z, len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 1f) continue;
                float mx = (points[p].X + points[p + 1].X) * .5f, mz = (points[p].Z + points[p + 1].Z) * .5f;
                float lx = -dz / len * reach, lz = dx / len * reach;
                vote += Side(mx + lx, mz + lz) - Side(mx - lx, mz - lz);
            }
            return vote > 0 ? 1 : vote < 0 ? -1 : 0;
        }

        private int Side(float x, float z)
        {
            if (!grid.WorldToCell(x, z, out int col, out int row)) return 0;
            SectorControl s = grid.GetSectorControl(col, row);
            return s == SectorControl.Friendly ? 1 : s == SectorControl.Hostile ? -1 : 0;
        }

        private static Color32 Alpha(Color32 c, byte a) { c.a = a; return c; }

        /// <summary>Hottest trace first: a bounded station budget keeps the fighting.</summary>
        private void RankTraces(int traceCount)
        {
            for (int i = 0; i < traceCount; i++) order[i] = i;
            for (int i = 1; i < traceCount; i++)
            {
                int candidate = order[i];
                float pressure = pressures[candidate];
                int at = i - 1;
                while (at >= 0 && pressures[order[at]] < pressure)
                {
                    order[at + 1] = order[at];
                    at--;
                }
                order[at + 1] = candidate;
            }
        }

        /// <summary>
        /// Walks every selected trace in map-pixel space, placing a station every
        /// <see cref="StationStep"/> pixels travelled (more on a long front, to stay inside the station budget).
        /// </summary>
        private int Sample(int traceCount)
        {
            int written = 0;

            // Zoomed in, the whole front is far longer on screen than the station budget. Widening
            // every step keeps the entire front drawn, a little coarser, instead of spending the
            // budget on its first traces and dropping the rest.
            float density = Mathf.Max(StationStep, TotalPixels(traceCount) /
                MaximumSamples);

            for (int t = 0; t < traceCount; t++)
            {
                int index = order[t];
                int count = lengths[index];
                bounds[t] = written;
                if (written < MaximumSamples && count >= 2)
                {
                    written = SampleTrace(starts[index], count, density, written);
                    Smooth(bounds[t], written);
                }
                bounds[t + 1] = written;
            }
            return written;
        }

        private float TotalPixels(int traceCount)
        {
            float total = 0f;
            for (int t = 0; t < traceCount; t++)
            {
                int index = order[t];
                for (int p = starts[index]; p + 1 < starts[index + 1]; p++)
                {
                    total += (projected[p + 1] - projected[p]).magnitude;
                }
            }
            return total;
        }

        /// <summary>
        /// The contour is a chain of cell-edge crossings, so its raw shape is a staircase with
        /// cell-sized treads. Four weighted passes turn it into a fluid tactical vector curve.
        /// </summary>
        private void Smooth(int start, int end)
        {
            for (int pass = 0; pass < 4 && end - start > 2; pass++)
            {
                Vector2 previous = stations[start];
                for (int i = start + 1; i < end - 1; i++)
                {
                    Vector2 current = stations[i];
                    stations[i] = (previous + current * 2f + stations[i + 1]) * 0.25f;
                    previous = current;
                }
            }
        }

        private int SampleTrace(int offset, int count, float density, int written)
        {
            int start = written;
            float travel = 0f;

            for (int p = offset; p + 1 < offset + count && written < MaximumSamples; p++)
            {
                Vector2 a = projected[p];
                Vector2 b = projected[p + 1];
                Vector2 delta = b - a;
                float segment = delta.magnitude;
                if (segment < 0.01f) continue;

                float at = travel;
                while (at < segment && written < MaximumSamples)
                {
                    stations[written++] = a + delta * (at / segment);
                    at += density;
                }
                travel = at - segment;
            }
            Vector2 end = projected[offset + count - 1];
            if (written < MaximumSamples && (written == start ||
                (end - stations[written - 1]).sqrMagnitude > .01f))
                stations[written++] = end;
            return written;
        }

        /// <summary>
        /// Soft glow, dark casing, then either the HOI4 front (our stripe, their stripe, teeth into
        /// their ground) or, with no clear side, the crisp pale core with cross ticks. Every stroke is
        /// a continuous feathered ribbon, legible over terrain, the forward-band tint and the trench
        /// trace beneath it, with no stair-stepped edge at any zoom.
        /// </summary>
        private void Paint(VertexHelper vh, int traceCount, int stationCount, float toLocal)
        {
            for (int t = 0; t < traceCount; t++)
            {
                int start = bounds[t], end = Mathf.Min(bounds[t + 1], stationCount);
                int count = end - start;
                if (count < 2) continue;
                int ticks = (count - 2) / TickEvery + 1;
                if (vh.currentVertCount + count * MapStroke.RibbonVertices * 4 +
                    ticks * MapStroke.SegmentVertices > MaximumVertices) return;

                bool hot = pressures[order[t]] >= .35f;
                int side = ourSide[order[t]];
                MapStroke.Ribbon(vh, stations, start, end, toLocal, .5f, hot ? 13f : 9f, hot ? HotGlow : CoolGlow);
                if (side == 0)
                {
                    MapStroke.Ribbon(vh, stations, start, end, toLocal, HalfWidth + 1.5f, 1.6f, Casing);
                    for (int i = start; i + 1 < end; i += TickEvery)
                    {
                        Vector2 along = (stations[i + 1] - stations[i]).normalized;
                        Vector2 reach = new Vector2(-along.y, along.x) * 3.4f;
                        MapStroke.Segment(vh, (stations[i] - reach) * toLocal, (stations[i] + reach) * toLocal,
                            .5f * toLocal, toLocal, TickInk);
                    }
                    MapStroke.Ribbon(vh, stations, start, end, toLocal, HalfWidth, 1f, Ink);
                    continue;
                }

                MapStroke.Ribbon(vh, stations, start, end, toLocal, StripeOffset + StripeHalf + 1f, 1.6f, Casing);
                Shift(start, end, side * StripeOffset);
                MapStroke.Ribbon(vh, shifted, start, end, toLocal, StripeHalf, 1f, OwnStripe);
                Shift(start, end, -side * StripeOffset);
                MapStroke.Ribbon(vh, shifted, start, end, toLocal, StripeHalf, 1f, TheirStripe);
                // Teeth: small triangles standing on their stripe and pointing into their ground.
                Color32 tooth = hot ? HotTooth : OwnStripe;
                for (int i = start + TickEvery / 2; i + 1 < end; i += TickEvery)
                {
                    Vector2 along = (stations[i + 1] - stations[i - 1]).normalized;
                    Vector2 into = new Vector2(along.y, -along.x) * side;   // right of travel when ours is left
                    Vector2 foot = stations[i] + into * StripeOffset;
                    Triangle(vh, (foot - along * ToothHalf) * toLocal, (foot + along * ToothHalf) * toLocal,
                        (foot + into * ToothReach) * toLocal, tooth);
                }
            }
        }

        /// <summary>The stations moved sideways by a fixed pixel distance (left of travel for a positive one).</summary>
        private void Shift(int start, int end, float by)
        {
            for (int i = start; i < end; i++)
            {
                Vector2 along = (stations[Mathf.Min(i + 1, end - 1)] - stations[Mathf.Max(i - 1, start)]).normalized;
                shifted[i] = stations[i] + new Vector2(-along.y, along.x) * by;
            }
        }

        private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 ink)
        {
            int first = vh.currentVertCount;
            UIVertex vert = UIVertex.simpleVert;
            vert.color = ink;
            vert.position = a; vh.AddVert(vert);
            vert.position = b; vh.AddVert(vert);
            vert.position = c; vh.AddVert(vert);
            vh.AddTriangle(first, first + 1, first + 2);
        }

        private static Vector2 ToPixels(FrontlineTracePoint point, Rect rect, float worldSizeX, float worldSizeZ, float scale)
        {
            float u = Mathf.Clamp01(point.X / worldSizeX + 0.5f);
            float v = Mathf.Clamp01(point.Z / worldSizeZ + 0.5f);
            if (MfdTerrainRelief.TryProject(point.X, point.Z, rect, out Vector2 projected))
                return projected * scale;
            return new Vector2(rect.xMin + u * rect.width, rect.yMin + v * rect.height) * scale;
        }
    }
}
