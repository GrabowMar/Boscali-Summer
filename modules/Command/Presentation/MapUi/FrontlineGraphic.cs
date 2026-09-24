using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// The theater front as one plain vector line: a single anti-aliased stroke per ordered
    /// front trace, one colour and one width at every zoom, sampled in map pixels so it never
    /// magnifies the overlay texture into blocks. Contested ground reads from the hatched
    /// squares under the line, not from the line's colour or shape.
    ///
    /// <para>Rebuilds on the sector-grid cadence, bounded by <see cref="MaximumSamples"/> and
    /// a hard vertex ceiling, hottest trace first.</para>
    /// </summary>
    internal sealed class FrontlineGraphic : MaskableGraphic
    {
        private const int MaximumTraces = FrontlineTraceLimits.MaximumTraces;
        private const int MaximumSamples = 1200;
        private const int MaximumVertices = 16000;
        private const float StationStep = 7f;
        private const float HalfWidth = 1.6f;

        private static readonly Color32 OuterUnder = new Color32(6, 8, 12, 175);
        private static readonly Color32 InnerGlow = new Color32(185, 215, 240, 75);

        /// <summary>The front's own ink; the map legend swatch reads it from here.</summary>
        internal static readonly Color32 Ink = new Color32(240, 245, 252, 235);

        private readonly FrontlineTracePoint[] points = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] lengths = new int[MaximumTraces];
        private readonly float[] pressures = new float[MaximumTraces];
        private readonly int[] order = new int[MaximumTraces];
        private readonly int[] starts = new int[MaximumTraces + 1];
        private readonly int[] bounds = new int[MaximumTraces + 1];
        private readonly Vector2[] stations = new Vector2[MaximumSamples];

        private TacticalSectorGrid grid;

        /// <summary>Source of the next rebuild; null draws nothing.</summary>
        public void SetSource(TacticalSectorGrid source)
        {
            grid = source;
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

            RankTraces(traceCount);
            int stationCount = Sample(traceCount, rect, worldSizeX, worldSizeZ, scale);
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
        private int Sample(int traceCount, Rect rect, float worldSizeX, float worldSizeZ, float scale)
        {
            int written = 0;

            // Zoomed in, the whole front is far longer on screen than the station budget. Widening
            // every step keeps the entire front drawn, a little coarser, instead of spending the
            // budget on its first traces and dropping the rest.
            float density = Mathf.Max(StationStep, TotalPixels(traceCount, rect, worldSizeX, worldSizeZ, scale) /
                MaximumSamples);

            for (int t = 0; t < traceCount; t++)
            {
                int index = order[t];
                int count = lengths[index];
                bounds[t] = written;
                if (written < MaximumSamples && count >= 2)
                {
                    written = SampleTrace(starts[index], count, rect, worldSizeX, worldSizeZ, scale, density, written);
                    Smooth(bounds[t], written);
                }
                bounds[t + 1] = written;
            }
            return written;
        }

        private float TotalPixels(int traceCount, Rect rect, float worldSizeX, float worldSizeZ, float scale)
        {
            float total = 0f;
            for (int t = 0; t < traceCount; t++)
            {
                int index = order[t];
                for (int p = starts[index]; p + 1 < starts[index + 1]; p++)
                {
                    Vector2 a = ToPixels(points[p], rect, worldSizeX, worldSizeZ, scale);
                    Vector2 b = ToPixels(points[p + 1], rect, worldSizeX, worldSizeZ, scale);
                    total += (b - a).magnitude;
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

        private int SampleTrace(int offset, int count, Rect rect, float worldSizeX, float worldSizeZ,
            float scale, float density, int written)
        {
            float travel = 0f;

            for (int p = offset; p + 1 < offset + count && written < MaximumSamples; p++)
            {
                Vector2 a = ToPixels(points[p], rect, worldSizeX, worldSizeZ, scale);
                Vector2 b = ToPixels(points[p + 1], rect, worldSizeX, worldSizeZ, scale);
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
            return written;
        }

        private void Paint(VertexHelper vh, int traceCount, int stationCount, float toLocal)
        {
            for (int t = 0; t < traceCount; t++)
            {
                int start = bounds[t], end = Mathf.Min(bounds[t + 1], stationCount);
                for (int i = start; i + 1 < end; i++)
                {
                    if (vh.currentVertCount > MaximumVertices) return;
                    AddStroke(vh, stations[i], stations[i + 1], toLocal);
                }
            }
        }

        /// <summary>
        /// Outer dark halo, soft tactical glow, then the crisp core: the front stays legible
        /// over terrain, over the forward-band tint and over the trench trace beneath it.
        /// </summary>
        private static void AddStroke(VertexHelper vh, Vector2 a, Vector2 b, float toLocal)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.05f) return;

            Vector2 side = new Vector2(-delta.y, delta.x) / length * HalfWidth;
            AddQuad(vh, a, b, side * 2.5f, toLocal, OuterUnder);
            AddQuad(vh, a, b, side * 1.5f, toLocal, InnerGlow);
            AddQuad(vh, a, b, side, toLocal, Ink);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 offset, float toLocal, Color32 ink)
        {
            var vert = UIVertex.simpleVert;
            vert.color = ink;
            int index = vh.currentVertCount;
            vert.position = (a - offset) * toLocal;
            vh.AddVert(vert);
            vert.position = (a + offset) * toLocal;
            vh.AddVert(vert);
            vert.position = (b + offset) * toLocal;
            vh.AddVert(vert);
            vert.position = (b - offset) * toLocal;
            vh.AddVert(vert);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private static Vector2 ToPixels(FrontlineTracePoint point, Rect rect, float worldSizeX, float worldSizeZ, float scale)
        {
            float u = Mathf.Clamp01(point.X / worldSizeX + 0.5f);
            float v = Mathf.Clamp01(point.Z / worldSizeZ + 0.5f);
            return new Vector2(rect.xMin + u * rect.width, rect.yMin + v * rect.height) * scale;
        }
    }
}
