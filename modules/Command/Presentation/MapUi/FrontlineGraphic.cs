using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The theater front as one vector line: our blue washing out on our side, their red on
    /// theirs, a dark casing with a bright core between them, and teeth pointing into enemy
    /// ground. Heat shows as an amber glow and core. A trace with no clear side keeps a cool glow.
    ///
    /// <para>Shape is decided in world space (corner-cut smoothing, teeth at fixed metres along
    /// the trace) once per front change, so the line and its teeth hold still while the camera
    /// pans, zooms or orbits; only the projection runs per view. Bounded by
    /// <see cref="MaximumSamples"/> and a hard vertex ceiling, hottest trace first.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DefaultExecutionOrder(101)] // Invalidate after MapUiManager commits the displayed relief pose.
    internal sealed class FrontlineGraphic : MaskableGraphic
    {
        private const int MaximumTraces = FrontlineTraceLimits.MaximumTraces;
        // Two corner-cut passes at most quadruple the points.
        private const int MaximumSmooth = FrontlineTraceLimits.MaximumPoints * 4;
        private const int MaximumSamples = 1600;
        private const int MaximumVertices = 30000;
        private const float StationStep = 5f;
        private const float WashWidth = 10f, CasingHalf = 2.1f, CoreHalf = .75f;
        // Teeth sit at fixed metres along a trace. Zoomed out, every other one drops (spacing doubles) rather than
        // sliding, so the survivors stay put. Size follows the projection's scale, clamped.
        private const float ToothWorld = 240f, ToothSpacingWorld = 600f, ToothGapPx = 16f, ToothMinPx = 4f, ToothMaxPx = 9f;
        private const float HotPressure = .35f;

        private static readonly Color32 Casing = new Color32(3, 8, 12, 225);
        private static readonly Color32 CoolGlow = new Color32(90, 200, 230, 70);
        private static readonly Color32 HotGlow = new Color32(255, 160, 60, 120);
        private static readonly Color32 OwnWash = Alpha(TacticalSectorGrid.FriendlyTint, 120);
        private static readonly Color32 TheirWash = Alpha(TacticalSectorGrid.HostileTint, 120);
        private static readonly Color32 OwnTooth = Lift(TacticalSectorGrid.FriendlyTint, .25f);
        private static readonly Color32 HotCore = new Color32(255, 222, 170, 250);

        /// <summary>The front's own ink; the map legend swatch reads it from here.</summary>
        internal static readonly Color32 Ink = new Color32(228, 241, 245, 245);

        private readonly FrontlineTracePoint[] raw = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
        private readonly int[] lengths = new int[MaximumTraces];
        private readonly float[] pressures = new float[MaximumTraces];
        private readonly int[] order = new int[MaximumTraces];
        private readonly int[] ourSide = new int[MaximumTraces];   // +1 ours left of travel, -1 right, 0 unclear
        private readonly bool[] closed = new bool[MaximumTraces];
        private readonly int[] smoothStart = new int[MaximumTraces];
        private readonly int[] bounds = new int[MaximumTraces + 1];

        private readonly Vector2[] pass = new Vector2[FrontlineTraceLimits.MaximumPoints * 2];
        private readonly Vector2[] world = new Vector2[MaximumSmooth];
        private readonly Vector2[] projected = new Vector2[MaximumSmooth];
        private readonly Vector2[] stations = new Vector2[MaximumSamples];
        private readonly float[] stationMetres = new float[MaximumSamples];
        private readonly float[] pixelsPerMetre = new float[MaximumSamples];

        private TacticalSectorGrid grid;
        private ulong drawnFrontlineHash;
        private bool reliefWasDrawing;
        private int reliefRevision;
        private int traceCount;   // smoothed traces held in world[] for the drawn hash

        private void LateUpdate()
        {
            bool drawing = MfdTerrainRelief.IsDrawing;
            int revision = MfdTerrainRelief.ViewRevision;
            if (drawing == reliefWasDrawing && revision == reliefRevision) return;
            reliefWasDrawing = drawing;
            reliefRevision = revision;
            SetVerticesDirty();
        }

        /// <summary>Source of the next rebuild; null draws nothing. An unchanged front skips the rebuild.</summary>
        public void SetSource(TacticalSectorGrid source)
        {
            if (source == grid && (source == null || source.FrontlineHash == drawnFrontlineHash)) return;
            grid = source;
            drawnFrontlineHash = source != null ? source.FrontlineHash : 0UL;
            traceCount = source != null ? Prepare(source) : 0;
            SetVerticesDirty();
        }

        /// <summary>World-space work, once per front change: copy, vote sides, smooth, rank.</summary>
        private int Prepare(TacticalSectorGrid source)
        {
            int count = source.CopyFrontlineTraces(raw, lengths, pressures);
            int rawAt = 0;
            for (int i = 0; i < count; i++)
            {
                int n = lengths[i], last = rawAt + n - 1;
                ourSide[i] = VoteSide(source, rawAt, n);
                float dx = raw[rawAt].X - raw[last].X, dz = raw[rawAt].Z - raw[last].Z;
                closed[i] = n > 3 && dx * dx + dz * dz < 1f;
                // Each trace owns a 4n slot of world[]: two corner cuts there -> pass[] -> back.
                int slot = smoothStart[i] = rawAt * 4;
                for (int p = 0; p < n; p++) world[slot + p] = new Vector2(raw[rawAt + p].X, raw[rawAt + p].Z);
                int once = CornerCut(world, slot, n, pass, 0, closed[i]);
                lengths[i] = CornerCut(pass, 0, once, world, slot, closed[i]);
                rawAt += n;
            }

            // Hottest first: a bounded station budget keeps the fighting.
            for (int i = 0; i < count; i++)
            {
                int at = i - 1;
                while (at >= 0 && pressures[order[at]] < pressures[i]) { order[at + 1] = order[at]; at--; }
                order[at + 1] = i;
            }
            return count;
        }

        /// <summary>
        /// Chaikin corner cutting: each segment becomes its 1/4 and 3/4 points, so the marching-squares
        /// staircase rounds into a curve that never leaves the original hull. Open traces keep their ends.
        /// </summary>
        private static int CornerCut(Vector2[] src, int from, int n, Vector2[] dst, int to, bool loop)
        {
            if (n < 3) { System.Array.Copy(src, from, dst, to, n); return n; }
            int w = to;
            if (!loop) dst[w++] = src[from];
            for (int p = from; p + 1 < from + n; p++)
            {
                Vector2 a = src[p], b = src[p + 1];
                dst[w++] = a * .75f + b * .25f;
                dst[w++] = a * .25f + b * .75f;
            }
            dst[w] = loop ? dst[to] : src[from + n - 1];
            return w + 1 - to;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (grid == null || traceCount <= 0 || !isActiveAndEnabled) return;
            float worldSizeX = grid.WorldSizeX, worldSizeZ = grid.WorldSizeY;
            if (!(worldSizeX > 0f) || !(worldSizeZ > 0f)) return;
            Rect rect = rectTransform.rect;
            if (rect.width < 4f || rect.height < 4f) return;
            float scale = Mathf.Abs(transform.lossyScale.x);
            if (!(scale > 1e-4f)) scale = 1f;

            float total = 0f;
            for (int t = 0; t < traceCount; t++)
            {
                int s = smoothStart[t], e = s + lengths[t];
                for (int p = s; p < e; p++)
                {
                    projected[p] = ToPixels(world[p], rect, worldSizeX, worldSizeZ, scale);
                    if (p > s) total += (projected[p] - projected[p - 1]).magnitude;
                }
            }
            // Zoomed in, a coarser step keeps the whole front inside the station budget.
            float density = Mathf.Max(StationStep, total / MaximumSamples);

            int written = 0;
            for (int t = 0; t < traceCount; t++)
            {
                int index = order[t];
                bounds[t] = written;
                if (written < MaximumSamples && lengths[index] >= 2)
                    written = Sample(smoothStart[index], lengths[index], density, written);
                bounds[t + 1] = written;
            }
            Paint(vh, 1f / scale);
        }

        /// <summary>Stations every <paramref name="density"/> pixels, each carrying its metres along the trace and the local scale.</summary>
        private int Sample(int from, int count, float density, int written)
        {
            float travel = 0f, metres = 0f, scaleHere = 0f;
            for (int p = from; p + 1 < from + count && written < MaximumSamples; p++)
            {
                Vector2 a = projected[p], delta = projected[p + 1] - a;
                float segment = delta.magnitude, span = (world[p + 1] - world[p]).magnitude;
                if (span > 1f) scaleHere = segment / span;
                if (segment > .01f)
                {
                    float at = travel;
                    for (; at < segment && written < MaximumSamples; at += density)
                    {
                        float k = at / segment;
                        stations[written] = a + delta * k;
                        stationMetres[written] = metres + span * k;
                        pixelsPerMetre[written++] = scaleHere;
                    }
                    travel = at - segment;
                }
                metres += span;
            }
            if (written < MaximumSamples)
            {
                stations[written] = projected[from + count - 1];
                stationMetres[written] = metres;
                pixelsPerMetre[written++] = scaleHere;
            }
            return written;
        }

        /// <summary>Back to front: glow, both washes, casing, core, teeth.</summary>
        private void Paint(VertexHelper vh, float toLocal)
        {
            for (int t = 0; t < traceCount; t++)
            {
                int start = bounds[t], end = bounds[t + 1], count = end - start;
                if (count < 2) continue;
                if (vh.currentVertCount + count * 24 > MaximumVertices) return;

                int index = order[t], side = ourSide[index];
                bool hot = pressures[index] >= HotPressure, loop = closed[index];
                if (hot || side == 0)
                    MapStroke.Ribbon(vh, stations, start, end, toLocal, .5f, hot ? 16f : 10f, hot ? HotGlow : CoolGlow, loop);
                if (side != 0)
                {
                    MapStroke.Band(vh, stations, start, end, toLocal, side, WashWidth, OwnWash, loop);
                    MapStroke.Band(vh, stations, start, end, toLocal, -side, WashWidth, TheirWash, loop);
                }
                MapStroke.Ribbon(vh, stations, start, end, toLocal, CasingHalf, 1.2f, Casing, loop);
                MapStroke.Ribbon(vh, stations, start, end, toLocal, CoreHalf, .9f, hot ? HotCore : Ink, loop);
                if (side != 0) Teeth(vh, start, end, side, toLocal);
            }
        }

        /// <summary>
        /// A tooth wherever the trace crosses a multiple of the spacing, the spacing doubled until
        /// neighbours keep <see cref="ToothGapPx"/> apart on screen. Fixed in metres, so they never swim.
        /// </summary>
        private void Teeth(VertexHelper vh, int start, int end, int side, float toLocal)
        {
            for (int i = start + 1; i + 1 < end; i++)
            {
                float ppm = pixelsPerMetre[i];
                if (!(ppm > 1e-6f)) continue;
                float spacing = ToothSpacingWorld;
                while (spacing * ppm < ToothGapPx && spacing < 1e6f) spacing *= 2f;
                if (Mathf.Floor(stationMetres[i] / spacing) == Mathf.Floor(stationMetres[i - 1] / spacing)) continue;

                float reach = Mathf.Clamp(ToothWorld * ppm, ToothMinPx, ToothMaxPx), half = reach * .55f;
                Vector2 along = (stations[i + 1] - stations[i - 1]).normalized;
                Vector2 into = new Vector2(along.y, -along.x) * side;   // into their ground
                Vector2 foot = stations[i] + into * (CasingHalf * .5f);
                Vector2 tip = foot + into * (CasingHalf + reach);
                Triangle(vh, (foot - along * (half + 1.1f)) * toLocal, (foot + along * (half + 1.1f)) * toLocal,
                    (tip + into * 1.3f) * toLocal, Casing);
                Triangle(vh, (foot - along * half) * toLocal, (foot + along * half) * toLocal, tip * toLocal, OwnTooth);
            }
        }

        /// <summary>Which side of a trace is ours, voted across a few segments by the cells either side.</summary>
        private int VoteSide(TacticalSectorGrid source, int start, int count)
        {
            if (count < 2) return 0;
            float reach = Mathf.Max(250f, source.CellSize * 1.2f);
            int vote = 0, stride = Mathf.Max(1, (count - 1) / 8);
            for (int p = start; p + 1 < start + count; p += stride)
            {
                float dx = raw[p + 1].X - raw[p].X, dz = raw[p + 1].Z - raw[p].Z, len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 1f) continue;
                float mx = (raw[p].X + raw[p + 1].X) * .5f, mz = (raw[p].Z + raw[p + 1].Z) * .5f;
                float lx = -dz / len * reach, lz = dx / len * reach;
                vote += Side(source, mx + lx, mz + lz) - Side(source, mx - lx, mz - lz);
            }
            return vote > 0 ? 1 : vote < 0 ? -1 : 0;
        }

        private static int Side(TacticalSectorGrid source, float x, float z)
        {
            if (!source.WorldToCell(x, z, out int col, out int row)) return 0;
            SectorControl s = source.GetSectorControl(col, row);
            return s == SectorControl.Friendly ? 1 : s == SectorControl.Hostile ? -1 : 0;
        }

        private static Color32 Alpha(Color32 c, byte a) { c.a = a; return c; }

        private static Color32 Lift(Color32 c, float toWhite)
            => new Color32((byte)(c.r + (255 - c.r) * toWhite), (byte)(c.g + (255 - c.g) * toWhite), (byte)(c.b + (255 - c.b) * toWhite), 255);

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

        private static Vector2 ToPixels(Vector2 point, Rect rect, float worldSizeX, float worldSizeZ, float scale)
        {
            if (MfdTerrainRelief.TryProject(point.x, point.y, rect, out Vector2 projected))
                return projected * scale;
            float u = Mathf.Clamp01(point.x / worldSizeX + 0.5f);
            float v = Mathf.Clamp01(point.y / worldSizeZ + 0.5f);
            return new Vector2(rect.xMin + u * rect.width, rect.yMin + v * rect.height) * scale;
        }
    }
}
