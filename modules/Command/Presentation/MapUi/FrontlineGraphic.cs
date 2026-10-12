using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The theater front as one vector line: an anti-aliased stroke per ordered trace,
    /// sampled in map pixels so it never magnifies the overlay texture into blocks.
    /// Strong opposing ground contact adds an amber halo; the line stays pale. Contested ground
    /// still reads from the hatched squares beneath the line.
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

        private static readonly Color32 Casing = new Color32(4, 10, 14, 205);
        private static readonly Color32 CoolGlow = new Color32(70, 190, 220, 80);
        private static readonly Color32 HotGlow = new Color32(255, 168, 72, 130);
        private static readonly Color32 TickInk = new Color32(150, 220, 235, 190);

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

            RankTraces(traceCount);
            int stationCount = Sample(traceCount);
            Paint(vh, traceCount, stationCount, 1f / scale);
        }

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
        /// <paramref name="density"/> pixels travelled.
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
        /// Soft glow, dark casing, then the crisp core, each one continuous feathered ribbon:
        /// the front stays legible over terrain, over the forward-band tint and over the
        /// trench trace beneath it, with no stair-stepped edge at any zoom.
        /// </summary>
        private void Paint(VertexHelper vh, int traceCount, int stationCount, float toLocal)
        {
            for (int t = 0; t < traceCount; t++)
            {
                int start = bounds[t], end = Mathf.Min(bounds[t + 1], stationCount);
                int count = end - start;
                if (count < 2) continue;
                int ticks = (count - 2) / TickEvery + 1;
                if (vh.currentVertCount + count * MapStroke.RibbonVertices * 3 +
                    ticks * MapStroke.SegmentVertices > MaximumVertices) return;

                bool hot = pressures[order[t]] >= .35f;
                MapStroke.Ribbon(vh, stations, start, end, toLocal, .5f, hot ? 13f : 9f, hot ? HotGlow : CoolGlow);
                MapStroke.Ribbon(vh, stations, start, end, toLocal, HalfWidth + 1.5f, 1.6f, Casing);
                for (int i = start; i + 1 < end; i += TickEvery)
                {
                    Vector2 along = (stations[i + 1] - stations[i]).normalized;
                    Vector2 reach = new Vector2(-along.y, along.x) * 3.4f;
                    MapStroke.Segment(vh, (stations[i] - reach) * toLocal, (stations[i] + reach) * toLocal,
                        .5f * toLocal, toLocal, TickInk);
                }
                MapStroke.Ribbon(vh, stations, start, end, toLocal, HalfWidth, 1f, Ink);
            }
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
