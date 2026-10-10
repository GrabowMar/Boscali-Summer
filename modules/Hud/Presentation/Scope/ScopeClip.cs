namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// Clipping for a page that draws a window inside its body (the OBJECTIVE plot, the pennants): the scope has no clip stack, so a stroke or a fill that leaves the
    /// window is cut here, exactly. Pure; no allocation (callers own the scratch arrays).
    /// </summary>
    internal static class ScopeClip
    {
        /// <summary>Liang-Barsky: cut the segment to the window; false when none of it is inside.</summary>
        public static bool Segment(float l, float t, float r, float b, ref float x0, ref float y0, ref float x1, ref float y1)
        {
            float dx = x1 - x0, dy = y1 - y0, t0 = 0f, t1 = 1f;
            if (!Edge(-dx, x0 - l, ref t0, ref t1) || !Edge(dx, r - x0, ref t0, ref t1) || !Edge(-dy, y0 - t, ref t0, ref t1) || !Edge(dy, b - y0, ref t0, ref t1)) return false;
            float ax = x0, ay = y0;
            x0 = ax + t0 * dx; y0 = ay + t0 * dy;
            x1 = ax + t1 * dx; y1 = ay + t1 * dy;
            return true;
        }

        private static bool Edge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }

        /// <summary>Sutherland-Hodgman against one half-plane: edge 0 keeps x &gt;= v, 1 keeps x &lt;= v, 2 keeps y &gt;= v, 3 keeps y &lt;= v. Returns the points written to <paramref name="dst"/>.</summary>
        public static int Half(float[] src, int n, float[] dst, int edge, float v)
        {
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ax = src[2 * i], ay = src[2 * i + 1], bx = src[2 * j], by = src[2 * j + 1];
                float da = Dist(edge, ax, ay, v), db = Dist(edge, bx, by, v);
                if (da >= 0f && m < dst.Length / 2) { dst[2 * m] = ax; dst[2 * m + 1] = ay; m++; }
                if ((da >= 0f) != (db >= 0f) && m < dst.Length / 2)
                {
                    float u = da / (da - db);
                    dst[2 * m] = ax + (bx - ax) * u;
                    dst[2 * m + 1] = ay + (by - ay) * u;
                    m++;
                }
            }
            return m;
        }

        private static float Dist(int edge, float x, float y, float v) => edge == 0 ? x - v : edge == 1 ? v - x : edge == 2 ? y - v : v - y;

        /// <summary>The polygon cut to a window: four passes through <paramref name="tmp"/> and back into <paramref name="dst"/> (both at least twice the points of the polygon plus eight).</summary>
        public static int Rect(float[] src, int n, float[] tmp, float[] dst, float l, float t, float r, float b)
        {
            int m = Half(src, n, tmp, 0, l);
            m = Half(tmp, m, dst, 1, r);
            m = Half(dst, m, tmp, 2, t);
            return Half(tmp, m, dst, 3, b);
        }
    }
}
