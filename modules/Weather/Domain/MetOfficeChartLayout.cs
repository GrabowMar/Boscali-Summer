using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// What the synoptic chart's labels must stay clear of, and the one order they are placed in, with no engine in it so a test can prove it. A label is a text box next to a marker; the obstacles
    /// are everything the chart draws as mesh (the theater frame, every front, the cells, the set-piece, anchor and camera markers) and the labels already placed. The text widths come from the caller
    /// (TextMeshPro at runtime, an estimate in the test); the placer itself is <see cref="MetChartMath.Place"/>.
    /// </summary>
    internal static class MetChartLayout
    {
        /// <summary>Label slots, in placement order: the theater, the camera, the anchor, then the six set-pieces.</summary>
        public const int Theater = 0, Camera = 1, Anchor = 2, FirstSet = 3, Labels = FirstSet + MetOfficeModel.SetCount;

        /// <summary>A label is one line, 16 px; the theater label is two ("THEATER" over its size), because one line is wider than the corridor between two fronts.</summary>
        public const float LabelH = 16f, TheaterH = 30f, MarkerBox = 14f;

        public static float Height(int slot) => slot == Theater ? TheaterH : LabelH;

        /// <summary>Capacity of the obstacle list: the frame, four fronts sampled every 6 px, the cells and the markers fit with room to spare.</summary>
        public const int MaxObstacles = 480;

        /// <summary>A label further than this from its marker gets a leader line.</summary>
        public const float LeaderAfter = 14f;

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
                x = Px(-m.HalfXKm, ox, plot) - 6f; y = Py(m.HalfZKm, oy, plot) + 4f;      // first choice: above the frame's north-west corner
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
        /// Fills <paramref name="into"/> with the boxes of everything drawn as mesh inside the plot (origin <paramref name="ox"/>, <paramref name="oy"/>, side <paramref name="plot"/> pixels):
        /// the four frame edges at 3 px, every front sampled at least every 6 px as a 9 px box (5 px for a squall line, which has no symbols), the cells at their drawn radius and the markers at 14 px.
        /// Returns the count. A cell with a non-finite position or radius is not drawn, so it is no obstacle.
        /// </summary>
        public static int Obstacles(MetOfficeModel m, float ox, float oy, float plot, MetBox[] into)
        {
            int n = 0;
            float hx = m.HalfXKm, hz = m.HalfZKm;
            if (hx > 0f && hz > 0f)
            {
                float x0 = Px(-hx, ox, plot), x1 = Px(hx, ox, plot), y0 = Py(hz, oy, plot), y1 = Py(-hz, oy, plot);
                Add(into, ref n, (x0 + x1) * 0.5f, y0, x1 - x0 + 3f, 3f); Add(into, ref n, (x0 + x1) * 0.5f, y1, x1 - x0 + 3f, 3f);
                Add(into, ref n, x0, (y0 + y1) * 0.5f, 3f, y1 - y0 + 3f); Add(into, ref n, x1, (y0 + y1) * 0.5f, 3f, y1 - y0 + 3f);
            }
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
                float d = Math.Min(30f, Math.Max(MarkerBox, 2f * Math.Max(2.5f, c.Radius / (2f * MetChartMath.RangeKm) * plot) + 2f));
                Add(into, ref n, Px(c.X, ox, plot), Py(c.Z, oy, plot), d, d);
            }
            for (int i = 0; i < m.Sets.Length; i++)
                if (m.Sets[i].Present && m.Sets[i].Placed) Add(into, ref n, Px(m.Sets[i].X, ox, plot), Py(m.Sets[i].Z, oy, plot), MarkerBox, MarkerBox);
            if (m.HasAnchor) Add(into, ref n, Px(m.AnchorX, ox, plot), Py(m.AnchorZ, oy, plot), MarkerBox, MarkerBox);
            if (m.HasCamera)
            {
                float cx = Px(m.CamX, ox, plot), cy = Py(m.CamZ, oy, plot), a = MetChartMath.Fin(m.CamHeadingDeg) * (float)(Math.PI / 180.0);
                Add(into, ref n, cx, cy, MarkerBox, MarkerBox);
                for (float r = 14f; r <= 26f; r += 6f) Add(into, ref n, cx + (float)Math.Sin(a) * r, cy - (float)Math.Cos(a) * r, 6f, 6f);   // the dashed heading line
            }
            return n;
        }

        /// <summary>
        /// Places every label that has a width (0 = no label) on its marker's first free spot and writes the boxes to <paramref name="boxes"/> (<see cref="Labels"/> long; a slot without a label gets a
        /// zero box). <paramref name="scratch"/> holds the obstacles and then each placed label (<see cref="MaxObstacles"/> + <see cref="Labels"/> long). The result is a pure function of the model and the widths.
        /// </summary>
        public static void Place(MetOfficeModel m, float ox, float oy, float plot, float[] widths, MetBox[] boxes, MetBox[] scratch)
        {
            int n = Obstacles(m, ox, oy, plot, scratch);
            var bounds = new MetBox(ox + 1f, oy + 1f, plot - 2f, plot - 2f);
            for (int slot = 0; slot < Labels; slot++)
            {
                boxes[slot] = default;
                if (widths[slot] <= 0f || !Marker(m, slot, ox, oy, plot, out float mx, out float my, out bool below)) continue;
                MetBox box = MetChartMath.Place(mx, my, widths[slot], Height(slot), below, bounds, scratch, n);
                scratch[n++] = box;
                boxes[slot] = box;
            }
        }

        /// <summary>The distance from a marker to the nearest point of its label box, and that point.</summary>
        public static float Gap(MetBox b, float mx, float my, out float nx, out float ny)
        {
            nx = mx < b.X ? b.X : mx > b.Right ? b.Right : mx;
            ny = my < b.Y ? b.Y : my > b.Bottom ? b.Bottom : my;
            return (float)Math.Sqrt((nx - mx) * (nx - mx) + (ny - my) * (ny - my));
        }
    }
}
