using System.Collections.Generic;

namespace NOAvionics
{
    /// <summary>Pure geometry for the Portal widgets (hazard stripes, heat grid, equalizer bars, dial ticks).</summary>
    public static class AvPortalMath
    {
        /// <summary>Sutherland-Hodgman clip of a convex polygon to an axis-aligned rectangle.</summary>
        public static AvV2[] ClipToRect(AvV2[] poly, float x0, float y0, float x1, float y1)
        {
            var cur = new List<AvV2>(poly);
            for (int edge = 0; edge < 4 && cur.Count > 0; edge++)
            {
                var next = new List<AvV2>(cur.Count + 2);
                for (int i = 0; i < cur.Count; i++)
                {
                    AvV2 a = cur[i], b = cur[(i + 1) % cur.Count];
                    bool ai = Inside(a, edge, x0, y0, x1, y1), bi = Inside(b, edge, x0, y0, x1, y1);
                    if (ai) next.Add(a);
                    if (ai != bi) next.Add(Cross(a, b, edge, x0, y0, x1, y1));
                }
                cur = next;
            }
            return cur.ToArray();
        }

        private static bool Inside(AvV2 p, int edge, float x0, float y0, float x1, float y1)
        {
            switch (edge)
            {
                case 0: return p.X >= x0;
                case 1: return p.X <= x1;
                case 2: return p.Y >= y0;
                default: return p.Y <= y1;
            }
        }

        private static AvV2 Cross(AvV2 a, AvV2 b, int edge, float x0, float y0, float x1, float y1)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            if (edge < 2) { float x = edge == 0 ? x0 : x1, t = dx == 0f ? 0f : (x - a.X) / dx; return new AvV2(x, a.Y + dy * t); }
            float y = edge == 2 ? y0 : y1, u = dy == 0f ? 0f : (y - a.Y) / dy;
            return new AvV2(a.X + dx * u, y);
        }

        /// <summary>45-degree hazard stripes (band = half the period) covering a rectangle, each clipped to it.</summary>
        public static List<AvV2[]> HazardStripes(float x0, float y0, float x1, float y1, float period)
        {
            var list = new List<AvV2[]>();
            if (period < 2f || x1 <= x0 || y1 <= y0) return list;
            float h = y1 - y0, band = period * 0.5f;
            for (float x = x0 - h; x < x1; x += period)
            {
                AvV2[] s = ClipToRect(new[]
                {
                    new AvV2(x, y0), new AvV2(x + band, y0), new AvV2(x + band + h, y1), new AvV2(x + h, y1),
                }, x0, y0, x1, y1);
                if (s.Length >= 3) list.Add(s);
            }
            return list;
        }

        /// <summary>Left/bottom origin of heat-grid cell i (row-major, row 0 on top of a rectangle of height totalH).</summary>
        public static AvV2 HeatCell(int index, int cols, float cellW, float cellH, float gap, float totalH)
        {
            int c = index % cols, r = index / cols;
            return new AvV2(c * (cellW + gap), totalH - (r + 1) * cellH - r * gap);
        }

        public static int HeatRows(int count, int cols) => cols <= 0 ? 0 : (count + cols - 1) / cols;

        /// <summary>Stable fake hardware serial for a console id (decoration only): "SN 04A7".</summary>
        public static string Serial(string id)
        {
            uint h = 2166136261u;
            foreach (char c in id ?? "") h = (h ^ c) * 16777619u;
            return "SN " + (h & 0xFFFFu).ToString("X4");
        }

        /// <summary>Angle in degrees of dial tick i of n, starting at the top and running clockwise.</summary>
        public static float TickDegrees(int index, int count) => count <= 0 ? 0f : 90f - 360f * index / count;
    }
}
