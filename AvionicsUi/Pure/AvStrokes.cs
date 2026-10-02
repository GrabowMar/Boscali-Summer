using System;

namespace NOAvionics
{
    /// <summary>
    /// A fixed-capacity buffer of textured quads (4 vertices each), the shared vertex format
    /// every HUD stroke writes into. Allocation-free once constructed: <see cref="Add"/> never
    /// grows the arrays, it just stops and flags <see cref="Overflowed"/> once <see cref="Count"/>
    /// reaches <see cref="Capacity"/>. Quad <c>q</c> occupies vertices <c>[4q .. 4q+3]</c>.
    /// </summary>
    public sealed class AvQuadBuffer
    {
        public AvQuadBuffer(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            X = new float[4 * capacity];
            Y = new float[4 * capacity];
            C = new Rgba[4 * capacity];
        }

        public int Capacity { get; }

        public int Count { get; private set; }

        public bool Overflowed { get; private set; }

        public readonly float[] X;
        public readonly float[] Y;
        public readonly Rgba[] C;

        /// <summary>Resets the buffer to empty without touching its arrays' capacity.</summary>
        public void Clear()
        {
            Count = 0;
            Overflowed = false;
        }

        /// <summary>
        /// Appends one quad. At capacity it sets <see cref="Overflowed"/> and returns false
        /// without writing anything.
        /// </summary>
        public bool Add(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3,
                         Rgba c0, Rgba c1, Rgba c2, Rgba c3)
        {
            if (Count >= Capacity)
            {
                Overflowed = true;
                return false;
            }

            int i = Count * 4;
            X[i] = x0; Y[i] = y0; C[i] = c0;
            X[i + 1] = x1; Y[i + 1] = y1; C[i + 1] = c1;
            X[i + 2] = x2; Y[i + 2] = y2; C[i + 2] = c2;
            X[i + 3] = x3; Y[i + 3] = y3; C[i + 3] = c3;
            Count++;
            return true;
        }
    }

    /// <summary>
    /// Allocation-free stroke and fill geometry into a caller-owned <see cref="AvQuadBuffer"/>.
    /// Canvas units, +Y up. Every call returns false once the buffer fills; a degenerate input
    /// (NaN coordinates, a zero-length segment, a non-positive size) is not an error, it just
    /// adds nothing and returns true.
    /// </summary>
    public static class AvStrokes
    {
        public static bool Line(AvQuadBuffer b, float ax, float ay, float bx, float by, float width, Rgba c, float feather = 0f)
        {
            if (IsNaN(ax) || IsNaN(ay) || IsNaN(bx) || IsNaN(by)) return true;

            float dx = bx - ax, dy = by - ay;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return true;

            float nx = -dy / len, ny = dx / len;
            float hw = width * 0.5f;

            bool ok = AddStrip(b, ax, ay, bx, by, nx, ny, hw, -hw, c, c);
            if (feather > 0f)
            {
                Rgba fade = c.WithAlpha(0f);
                ok &= AddStrip(b, ax, ay, bx, by, nx, ny, hw, hw + feather, c, fade);
                ok &= AddStrip(b, ax, ay, bx, by, nx, ny, -hw, -(hw + feather), c, fade);
            }
            return ok;
        }

        public static bool DashedLine(AvQuadBuffer b, float ax, float ay, float bx, float by, float dash, float gap, float width, Rgba c)
        {
            if (IsNaN(ax) || IsNaN(ay) || IsNaN(bx) || IsNaN(by)) return true;
            if (dash <= 0f || gap < 0f) return true;

            float dx = bx - ax, dy = by - ay;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return true;

            float ux = dx / len, uy = dy / len;
            bool ok = true;
            float t = 0f;
            while (t < len)
            {
                float t1 = Math.Min(t + dash, len);
                ok &= Line(b, ax + ux * t, ay + uy * t, ax + ux * t1, ay + uy * t1, width, c);
                t += dash + gap;
            }
            return ok;
        }

        public static bool Polyline(AvQuadBuffer b, float[] xs, float[] ys, int count, bool closed, float width, Rgba c)
        {
            if (xs == null || ys == null) return true;
            int n = Math.Min(count, Math.Min(xs.Length, ys.Length));
            if (n < 2) return true;

            bool ok = true;
            for (int i = 0; i < n - 1; i++)
                ok &= Line(b, xs[i], ys[i], xs[i + 1], ys[i + 1], width, c);
            if (closed)
                ok &= Line(b, xs[n - 1], ys[n - 1], xs[0], ys[0], width, c);
            return ok;
        }

        public static bool Arc(AvQuadBuffer b, float cx, float cy, float radius, float fromDeg, float toDeg, int segments, float width, Rgba c)
        {
            if (IsNaN(cx) || IsNaN(cy) || IsNaN(radius) || IsNaN(fromDeg) || IsNaN(toDeg)) return true;
            if (radius <= 0f) return true;

            int segs = segments < 1 ? 1 : segments > 256 ? 256 : segments;
            float hw = width * 0.5f;

            double fromRad = fromDeg * Math.PI / 180.0;
            double stepRad = (toDeg - fromDeg) * Math.PI / 180.0 / segs;

            bool ok = true;
            for (int i = 0; i < segs; i++)
            {
                double a0 = fromRad + stepRad * i;
                double a1 = fromRad + stepRad * (i + 1);
                float cosA = (float)Math.Cos(a0), sinA = (float)Math.Sin(a0);
                float cosB = (float)Math.Cos(a1), sinB = (float)Math.Sin(a1);

                float outerAx = cx + cosA * (radius + hw), outerAy = cy + sinA * (radius + hw);
                float outerBx = cx + cosB * (radius + hw), outerBy = cy + sinB * (radius + hw);
                float innerBx = cx + cosB * (radius - hw), innerBy = cy + sinB * (radius - hw);
                float innerAx = cx + cosA * (radius - hw), innerAy = cy + sinA * (radius - hw);

                ok &= b.Add(outerAx, outerAy, outerBx, outerBy, innerBx, innerBy, innerAx, innerAy, c, c, c, c);
            }
            return ok;
        }

        public static bool Ring(AvQuadBuffer b, float cx, float cy, float radius, int segments, float width, Rgba c) =>
            Arc(b, cx, cy, radius, 0f, 360f, segments, width, c);

        public static bool DashedRing(AvQuadBuffer b, float cx, float cy, float radius, int dashes, float dutyCycle, float width, Rgba c)
        {
            if (dashes < 1) return true;

            float step = 360f / dashes;
            float dashSpan = step * dutyCycle;

            bool ok = true;
            for (int i = 0; i < dashes; i++)
            {
                float from = i * step;
                ok &= Arc(b, cx, cy, radius, from, from + dashSpan, 2, width, c);
            }
            return ok;
        }

        public static bool Bracket(AvQuadBuffer b, float x, float y, float w, float h, float arm, float width, Rgba c)
        {
            if (IsNaN(x) || IsNaN(y) || IsNaN(w) || IsNaN(h)) return true;

            float a = arm;
            float halfW = w * 0.5f, halfH = h * 0.5f;
            if (a > halfW) a = halfW;
            if (a > halfH) a = halfH;

            bool ok = true;
            ok &= Line(b, x, y, x + a, y, width, c);
            ok &= Line(b, x, y, x, y + a, width, c);
            ok &= Line(b, x + w, y, x + w - a, y, width, c);
            ok &= Line(b, x + w, y, x + w, y + a, width, c);
            ok &= Line(b, x + w, y + h, x + w - a, y + h, width, c);
            ok &= Line(b, x + w, y + h, x + w, y + h - a, width, c);
            ok &= Line(b, x, y + h, x + a, y + h, width, c);
            ok &= Line(b, x, y + h, x, y + h - a, width, c);
            return ok;
        }

        public static bool Chevron(AvQuadBuffer b, float cx, float cy, float size, float angleDeg, float width, Rgba c)
        {
            if (IsNaN(cx) || IsNaN(cy) || IsNaN(size) || IsNaN(angleDeg)) return true;

            double rad = angleDeg * Math.PI / 180.0;
            float dx = (float)Math.Cos(rad), dy = (float)Math.Sin(rad);
            float nx = -dy, ny = dx;
            float half = size * 0.5f;

            float tipX = cx + dx * half, tipY = cy + dy * half;
            float baseX = cx - dx * half, baseY = cy - dy * half;

            bool ok = true;
            ok &= Line(b, baseX + nx * half, baseY + ny * half, tipX, tipY, width, c);
            ok &= Line(b, baseX - nx * half, baseY - ny * half, tipX, tipY, width, c);
            return ok;
        }

        public static bool Diamond(AvQuadBuffer b, float cx, float cy, float r, float width, Rgba c)
        {
            bool ok = true;
            ok &= Line(b, cx + r, cy, cx, cy + r, width, c);
            ok &= Line(b, cx, cy + r, cx - r, cy, width, c);
            ok &= Line(b, cx - r, cy, cx, cy - r, width, c);
            ok &= Line(b, cx, cy - r, cx + r, cy, width, c);
            return ok;
        }

        public static bool Cross(AvQuadBuffer b, float cx, float cy, float r, float width, Rgba c)
        {
            bool ok = true;
            ok &= Line(b, cx - r, cy, cx + r, cy, width, c);
            ok &= Line(b, cx, cy - r, cx, cy + r, width, c);
            return ok;
        }

        public static bool Fill(AvQuadBuffer b, float x, float y, float w, float h, Rgba c)
        {
            if (IsNaN(x) || IsNaN(y) || IsNaN(w) || IsNaN(h)) return true;
            return b.Add(x, y, x + w, y, x + w, y + h, x, y + h, c, c, c, c);
        }

        /// <summary>
        /// Adds one strip quad: the near edge at offset <paramref name="r0"/> along the normal
        /// (colour <paramref name="c0"/>), the far edge at <paramref name="r1"/> (colour
        /// <paramref name="c1"/>). A single strip with <c>r0 = -r1</c> and <c>c0 == c1</c> is a
        /// plain stroke core; a strip fading to a transparent colour is a feather.
        /// </summary>
        private static bool AddStrip(AvQuadBuffer b, float ax, float ay, float bx, float by, float nx, float ny,
                                      float r0, float r1, Rgba c0, Rgba c1) =>
            b.Add(ax + nx * r0, ay + ny * r0, bx + nx * r0, by + ny * r0,
                  bx + nx * r1, by + ny * r1, ax + nx * r1, ay + ny * r1,
                  c0, c0, c1, c1);

        private static bool IsNaN(float v) => float.IsNaN(v);
    }
}
