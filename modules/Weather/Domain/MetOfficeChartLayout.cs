using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// What the synoptic chart's labels must stay clear of, with no engine in it so a test can prove it. A label is a text box next to a marker; the obstacles are everything the chart draws as
    /// mesh: HARD ones a label may never touch (the theater frame and the markers) and SOFT ones a label may only straddle as a last resort, on its opaque plate (the fronts,
    /// the storm cells and the camera's dashed heading line). The placer itself is <see cref="MetChartPlanner"/>; this class is the geometry it and its tests share.
    /// </summary>
    internal static class MetChartLayout
    {
        /// <summary>Label slots, in placement order: the theater, the camera, the anchor, then the six set-pieces.</summary>
        public const int Theater = 0, Camera = 1, Anchor = 2, FirstSet = 3, Labels = FirstSet + MetOfficeModel.SetCount;

        /// <summary>A label line is 16 px; the theater label is two ("THEATER" over its size), because one line is wider than the corridor between two fronts.</summary>
        public const float LineH = 16f;

        /// <summary>Capacity of the obstacle list: the frame, four fronts sampled every 6 px, the cells and the markers fit with room to spare.</summary>
        public const int MaxObstacles = 480;

        /// <summary>A leader (the dashed line from a marker to a label that could not stand beside it) is never longer than this, in chart pixels; past it the label takes its short form.</summary>
        public const float LeaderMax = 60f;

        /// <summary>Height of a label of <paramref name="text"/>: 16 px a line less the two it shares (30 for the theater's two lines).</summary>
        public static float Height(string text)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            return LineH + (lines - 1) * (LineH - 2f);
        }

        /// <summary>Half the side of the box a marker blocks (the ring of a set-piece, the crosshair of the anchor, the camera's arrow); the leader leaves the marker at this distance. 0 for the theater, whose marker is the frame's corner.</summary>
        public static float MarkerHalf(int slot) => slot == Theater ? 0f : slot == Camera ? 9f : slot == Anchor ? 12f : 10f;

        /// <summary>A label closer than this to its marker needs no leader.</summary>
        public static float LeaderAfter(int slot) => slot == Theater ? 10f : MarkerHalf(slot) + 9f;

        public static float Px(float km, float ox, float plot) => ox + MetChartMath.PxX(MetChartMath.Clamp(km), plot);
        public static float Py(float km, float oy, float plot) => oy + MetChartMath.PxY(MetChartMath.Clamp(km), plot);

        private static void Add(MetBox[] into, ref int n, float cx, float cy, float w, float h)
        {
            if (n < into.Length) into[n++] = new MetBox(cx - w * 0.5f, cy - h * 0.5f, w, h);
        }

        /// <summary>The marker a label slot hangs on, in chart pixels, and whether its first choice is below it. False for a slot with no marker (no frame, no camera, no anchor, nothing placed).</summary>
        public static bool Marker(MetOfficeModel m, int slot, float ox, float oy, float plot, out float x, out float y, out bool below)
        {
            x = y = 0f; below = false;
            if (slot == Theater)
            {
                if (!(m.HalfXKm > 0f)) return false;
                x = Px(-m.HalfXKm, ox, plot); y = Py(m.HalfZKm, oy, plot);                    // the frame's north-west corner; first choice: above it
                return true;
            }
            if (slot == Camera) { x = Px(m.CamX, ox, plot); y = Py(m.CamZ, oy, plot); below = true; return m.HasCamera; }   // first choice: right of and below the camera
            if (slot == Anchor) { x = Px(m.AnchorX, ox, plot); y = Py(m.AnchorZ, oy, plot); return m.HasAnchor; }           // first choice: right of and above the anchor, so never on the camera label
            int i = slot - FirstSet;
            if (i < 0 || i >= m.Sets.Length || !m.Sets[i].Present || !m.Sets[i].Placed) return false;
            x = Px(m.Sets[i].X, ox, plot); y = Py(m.Sets[i].Z, oy, plot);
            return true;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the boxes of everything drawn as mesh inside the plot (origin <paramref name="ox"/>, <paramref name="oy"/>, side <paramref name="plot"/> pixels), the HARD ones first
        /// (<paramref name="hard"/> of them: the four frame edges at 3 px, the markers at twice their <see cref="MarkerHalf"/>) and then the SOFT ones: every front sampled at least every 6 px as a 9 px box
        /// (5 px for a squall line, which has no symbols), the cells at their drawn radius and the camera's dashed heading line. Returns the count. A cell with a non-finite position or radius is not drawn, so it is no obstacle.
        /// </summary>
        public static int Obstacles(MetOfficeModel m, float ox, float oy, float plot, MetBox[] into, out int hard)
        {
            int n = 0;
            float hx = m.HalfXKm, hz = m.HalfZKm;
            if (hx > 0f && hz > 0f)
            {
                float x0 = Px(-hx, ox, plot), x1 = Px(hx, ox, plot), y0 = Py(hz, oy, plot), y1 = Py(-hz, oy, plot);
                Add(into, ref n, (x0 + x1) * 0.5f, y0, x1 - x0 + 3f, 3f); Add(into, ref n, (x0 + x1) * 0.5f, y1, x1 - x0 + 3f, 3f);
                Add(into, ref n, x0, (y0 + y1) * 0.5f, 3f, y1 - y0 + 3f); Add(into, ref n, x1, (y0 + y1) * 0.5f, 3f, y1 - y0 + 3f);
            }
            for (int i = 0; i < m.Sets.Length; i++)
                if (m.Sets[i].Present && m.Sets[i].Placed) Add(into, ref n, Px(m.Sets[i].X, ox, plot), Py(m.Sets[i].Z, oy, plot), 2f * MarkerHalf(FirstSet), 2f * MarkerHalf(FirstSet));
            if (m.HasAnchor) Add(into, ref n, Px(m.AnchorX, ox, plot), Py(m.AnchorZ, oy, plot), 2f * MarkerHalf(Anchor), 2f * MarkerHalf(Anchor));
            if (m.HasCamera)
            {
                float cx = Px(m.CamX, ox, plot), cy = Py(m.CamZ, oy, plot), a = MetChartMath.Fin(m.CamHeadingDeg) * (float)(Math.PI / 180.0);
                Add(into, ref n, cx, cy, 2f * MarkerHalf(Camera), 2f * MarkerHalf(Camera));
            }
            hard = n;
            for (int i = 0; i < m.FrontCount && i < m.Fronts.Length; i++)
            {
                MetFront f = m.Fronts[i];
                float side = f.Kind == MetFrontKind.Squall ? 5f : 9f;
                for (int p = 0; p + 1 < f.Count; p++)
                {
                    float ax = Px(f.X[p], ox, plot), ay = Py(f.Z[p], oy, plot), bx = Px(f.X[p + 1], ox, plot), by = Py(f.Z[p + 1], oy, plot);
                    float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    int steps = Math.Max(1, (int)Math.Ceiling(len / 6f));
                    for (int s = 0; s < steps; s++) Add(into, ref n, ax + (bx - ax) * s / steps, ay + (by - ay) * s / steps, side, side);
                }
                if (f.Count > 0) Add(into, ref n, Px(f.X[f.Count - 1], ox, plot), Py(f.Z[f.Count - 1], oy, plot), side, side);
            }
            for (int i = 0; i < m.CellCount && i < m.Cells.Length; i++)
            {
                MetCell c = m.Cells[i];
                if (!MetChartMath.IsFinite(c.X) || !MetChartMath.IsFinite(c.Z) || !MetChartMath.IsFinite(c.Radius)) continue;
                float d = Math.Min(30f, Math.Max(14f, 2f * Math.Max(2.5f, c.Radius / (2f * MetChartMath.RangeKm) * plot) + 2f));
                Add(into, ref n, Px(c.X, ox, plot), Py(c.Z, oy, plot), d, d);
            }
            if (m.HasCamera)
            {
                float cx = Px(m.CamX, ox, plot), cy = Py(m.CamZ, oy, plot), a = MetChartMath.Fin(m.CamHeadingDeg) * (float)(Math.PI / 180.0);
                for (float r = 14f; r <= 26f; r += 6f) Add(into, ref n, cx + (float)Math.Sin(a) * r, cy - (float)Math.Cos(a) * r, 6f, 6f);   // the dashed heading line: thin, so a label may straddle it
            }
            return n;
        }

        // ---- What the chart draws, as one number --------------------------------------------------------------------------------------------

        private static ulong Mix(ulong h, int v) => (h ^ (uint)v) * 1099511628211UL;

        /// <summary>A coordinate in km at <paramref name="perKm"/> steps per km; non-finite sits at the centre and the chart's edge clamps it, so the int never overflows.</summary>
        private static int Q(float km, float perKm) => (int)Math.Round(MetChartMath.Clamp(km) * perKm);

        /// <summary>A number of kilometres as the label prints it, whole; non-finite is 0, and a huge one cannot overflow the int.</summary>
        private static int Whole(float v) => MetChartMath.IsFinite(v) ? (int)Math.Round(v < -1e6f ? -1e6f : v > 1e6f ? 1e6f : v) : 0;

        /// <summary>
        /// A hash of everything the chart reads from the model, quantised to what the eye (and the label text) can tell apart: the camera to a kilometre and a degree, the fronts to 200 m, the wind to
        /// the numbers the strip prints. Two models with the same signature draw the same chart and print the same labels, so the window re-places and re-paints only when it changes, and idle jitter
        /// (the camera creeping, a float's last bit) changes nothing. Allocates nothing.
        /// </summary>
        public static ulong Signature(MetOfficeModel m)
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, m.WindFromDeg);
            float mps = m.WindMps;
            if (!(mps >= 0.3f) || float.IsInfinity(mps)) h = Mix(h, -1);
            else { h = Mix(h, (int)Math.Round(mps)); h = Mix(h, (int)Math.Round(mps * 1.943844f)); h = Mix(h, (int)Math.Round(mps * 1.943844f / 5f, MidpointRounding.AwayFromZero)); }
            h = Mix(h, Q(m.HalfXKm, 10f)); h = Mix(h, Q(m.HalfZKm, 10f)); h = Mix(h, Whole(m.HalfXKm * 2f)); h = Mix(h, Whole(m.HalfZKm * 2f));
            h = Mix(h, m.HasCamera ? 1 : 0);
            if (m.HasCamera) { h = Mix(h, Q(m.CamX, 1f)); h = Mix(h, Q(m.CamZ, 1f)); h = Mix(h, (int)Math.Round(MetChartMath.Fin(m.CamHeadingDeg))); }
            h = Mix(h, m.HasAnchor ? 1 : 0);
            if (m.HasAnchor)
            {
                h = Mix(h, Q(m.AnchorX, 2f)); h = Mix(h, Q(m.AnchorZ, 2f));
                float dx = m.AnchorX - m.CamX, dz = m.AnchorZ - m.CamZ, off = m.HasCamera ? (float)Math.Sqrt(dx * dx + dz * dz) : 0f;
                h = Mix(h, off >= 1f && off < 1000f ? (int)Math.Round(off) : 0);   // the label's "+30 KM"
            }
            h = Mix(h, m.FrontCount);
            for (int i = 0; i < m.FrontCount && i < m.Fronts.Length; i++)
            {
                MetFront f = m.Fronts[i];
                h = Mix(h, (int)f.Kind); h = Mix(h, f.Count);
                h = Mix(h, Q(f.DirX, 10f)); h = Mix(h, Q(f.DirZ, 10f));
                for (int p = 0; p < f.Count; p++) { h = Mix(h, Q(f.X[p], 5f)); h = Mix(h, Q(f.Z[p], 5f)); }
            }
            h = Mix(h, m.CellCount);
            for (int i = 0; i < m.CellCount && i < m.Cells.Length; i++)
            {
                MetCell c = m.Cells[i];
                if (!MetChartMath.IsFinite(c.X) || !MetChartMath.IsFinite(c.Z) || !MetChartMath.IsFinite(c.Radius)) { h = Mix(h, -7); continue; }
                h = Mix(h, Q(c.X, 5f)); h = Mix(h, Q(c.Z, 5f)); h = Mix(h, Q(c.Radius, 2f));
            }
            for (int i = 0; i < m.Sets.Length; i++)
            {
                MetSetPiece s = m.Sets[i];
                if (!s.Present || !s.Placed) { h = Mix(h, 0); continue; }
                h = Mix(h, Q(s.X, 5f)); h = Mix(h, Q(s.Z, 5f));
                h = Mix(h, MetChartMath.IsFinite(s.Strength) ? (int)Math.Round((s.Strength < 0f ? 0f : s.Strength > 1f ? 1f : s.Strength) * 100f, MidpointRounding.AwayFromZero) : -1);
            }
            return h;
        }

        // ---- Segments and boxes -------------------------------------------------------------------------------------------------------------

        /// <summary>True when the segment runs through the interior of <paramref name="b"/> grown by <paramref name="margin"/> on every side (touching a corner does not count).</summary>
        public static bool SegmentHitsBox(float x0, float y0, float x1, float y1, MetBox b, float margin)
        {
            float t0 = 0f, t1 = 1f, dx = x1 - x0, dy = y1 - y0;
            float lx = b.X - margin, rx = b.Right + margin, ty = b.Y - margin, by = b.Bottom + margin;
            return Clip(-dx, x0 - lx, ref t0, ref t1) && Clip(dx, rx - x0, ref t0, ref t1) && Clip(-dy, y0 - ty, ref t0, ref t1) && Clip(dy, by - y0, ref t0, ref t1) && t1 - t0 > 1e-4f;
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Math.Abs(p) < 1e-9f) return q > 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }

        /// <summary>Distance between two segments: 0 when they cross.</summary>
        public static float SegmentDistance(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
        {
            float d1 = Side(cx, cy, dx, dy, ax, ay), d2 = Side(cx, cy, dx, dy, bx, by), d3 = Side(ax, ay, bx, by, cx, cy), d4 = Side(ax, ay, bx, by, dx, dy);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return 0f;
            return Math.Min(Math.Min(PointSegment(ax, ay, cx, cy, dx, dy), PointSegment(bx, by, cx, cy, dx, dy)), Math.Min(PointSegment(cx, cy, ax, ay, bx, by), PointSegment(dx, dy, ax, ay, bx, by)));
        }

        private static float Side(float ax, float ay, float bx, float by, float px, float py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        private static float PointSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            float t = l2 < 1e-9f ? 0f : ((px - ax) * dx + (py - ay) * dy) / l2;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            float qx = ax + dx * t - px, qy = ay + dy * t - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        /// <summary>The distance from a marker to the nearest point of a label box, and that point.</summary>
        public static float Gap(MetBox b, float mx, float my, out float nx, out float ny)
        {
            nx = mx < b.X ? b.X : mx > b.Right ? b.Right : mx;
            ny = my < b.Y ? b.Y : my > b.Bottom ? b.Bottom : my;
            return (float)Math.Sqrt((nx - mx) * (nx - mx) + (ny - my) * (ny - my));
        }
    }
}
