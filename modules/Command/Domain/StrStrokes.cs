using System;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Everything one lane bar needs: its rectangle, the value, the trend and the five inks.</summary>
    internal struct StrLane
    {
        /// <summary>Track rectangle in canvas units, +Y up.</summary>
        public float X, Y, W, H;
        /// <summary>Hostile share 0..1. NaN draws the hatched "not observed" track and no notch.</summary>
        public float Value;
        /// <summary>Change of Value over the last review, -1..1. NaN draws no ghost trail.</summary>
        public float Trend;
        /// <summary>Own end, hostile end, notch, ghost trail and sleeper tick colours.</summary>
        public Rgba Own, Hostile, Notch, Ghost, Tick;
    }

    /// <summary>
    /// Stroke helpers the shared kit does not have, with the kit's conventions: canvas units, +Y up, a non-finite input adds
    /// nothing and returns true, false once the buffer is full. The kit file is not edited (it has a sibling copy in the Wing build).
    /// </summary>
    internal static class StrStrokes
    {
        /// <summary>Most quads LaneBar writes for a track up to 440 wide and 12 high.</summary>
        public const int LaneQuads = 64;

        private const float Root2 = 1.4142135f, Cos30 = 0.8660254f;
        private const float HatchSpacing = 8f, HatchWidth = 1.5f, TickWidth = 1f, NotchWidth = 2.5f, NotchHead = 4f, GhostWidth = 1.5f;
        private const int MaxGhostDashes = 20;

        /// <summary>Dashed segment whose pattern starts phase canvas units into the dash cycle (so a caller can march it).</summary>
        public static bool DashedLine(AvQuadBuffer b, float ax, float ay, float bx, float by,
                                      float dash, float gap, float phase, float width, Rgba c)
        {
            if (Bad(ax) || Bad(ay) || Bad(bx) || Bad(by) || Bad(dash) || Bad(gap) || Bad(phase) || Bad(width)) return true;
            if (dash <= 0f || gap < 0f) return true;

            float dx = bx - ax, dy = by - ay;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return true;

            float cycle = dash + gap;
            if (len / cycle > 8192f) return true;   // a pattern finer than that is a smear, not a line
            float p = phase % cycle;
            if (p < 0f) p += cycle;

            float ux = dx / len, uy = dy / len;
            for (float t = -p; t < len; t += cycle)
            {
                float t0 = Math.Max(t, 0f), t1 = Math.Min(t + dash, len);
                if (t1 - t0 <= 1e-4f) continue;
                if (!AvStrokes.Line(b, ax + ux * t0, ay + uy * t0, ax + ux * t1, ay + uy * t1, width, c)) return false;
            }
            return true;
        }

        /// <summary>Solid shaft and a solid triangular head ending exactly at (bx, by); the head is one quad.</summary>
        public static bool Arrow(AvQuadBuffer b, float ax, float ay, float bx, float by,
                                 float width, float head, Rgba c)
        {
            if (!Shape(ax, ay, bx, by, width, head, out float ux, out float uy, out float h, out float len)) return true;
            float baseX = bx - ux * h, baseY = by - uy * h;
            bool ok = true;
            if (h < len) ok &= AvStrokes.Line(b, ax, ay, baseX, baseY, width, c);
            return ok & Head(b, bx, by, baseX, baseY, ux, uy, h, c);
        }

        /// <summary>Dashed shaft (with phase) and a solid head ending exactly at (bx, by).</summary>
        public static bool DashedArrow(AvQuadBuffer b, float ax, float ay, float bx, float by,
                                       float dash, float gap, float phase, float width, float head, Rgba c)
        {
            if (!Shape(ax, ay, bx, by, width, head, out float ux, out float uy, out float h, out float len)) return true;
            float baseX = bx - ux * h, baseY = by - uy * h;
            bool ok = true;
            if (h < len) ok &= DashedLine(b, ax, ay, baseX, baseY, dash, gap, phase, width, c);
            return ok & Head(b, bx, by, baseX, baseY, ux, uy, h, c);
        }

        /// <summary>45 degree lines rising to the right, spacing apart measured across the lines, clipped to the rectangle.</summary>
        public static bool HatchRect(AvQuadBuffer b, float x, float y, float w, float h,
                                     float spacing, float width, Rgba c)
        {
            if (Bad(x) || Bad(y) || Bad(w) || Bad(h) || Bad(spacing) || Bad(width)) return true;
            if (w <= 0f || h <= 0f || spacing <= 0f || width <= 0f) return true;

            // The lines are y = x + k. The intercepts run from the bottom-right corner to the top-left one; a half step
            // keeps the first and last line off the corner points, where they would have no length.
            float x1 = x + w, y1 = y + h;
            float kMin = y - x1, kMax = y1 - x;
            int n = Math.Max(1, (int)Math.Ceiling((kMax - kMin) / (spacing * Root2)));
            float step = (kMax - kMin) / n;
            for (int i = 0; i < n; i++)
            {
                float k = kMin + step * (i + 0.5f);
                float sx = Math.Max(x, y - k), ex = Math.Min(x1, y1 - k);
                if (ex - sx < 1e-3f) continue;
                if (!AvStrokes.Line(b, sx, sx + k, ex, ex + k, width, c)) return false;
            }
            return true;
        }

        /// <summary>Hexagon outline, point up: the strike target glyph.</summary>
        public static bool Hex(AvQuadBuffer b, float cx, float cy, float r, float width, Rgba c)
        {
            if (Bad(cx) || Bad(cy) || Bad(r) || Bad(width) || r <= 0f) return true;
            float wx = r * Cos30, hy = r * 0.5f;
            bool ok = true;
            ok &= AvStrokes.Line(b, cx, cy + r, cx + wx, cy + hy, width, c);
            ok &= AvStrokes.Line(b, cx + wx, cy + hy, cx + wx, cy - hy, width, c);
            ok &= AvStrokes.Line(b, cx + wx, cy - hy, cx, cy - r, width, c);
            ok &= AvStrokes.Line(b, cx, cy - r, cx - wx, cy - hy, width, c);
            ok &= AvStrokes.Line(b, cx - wx, cy - hy, cx - wx, cy + hy, width, c);
            ok &= AvStrokes.Line(b, cx - wx, cy + hy, cx, cy + r, width, c);
            return ok;
        }

        /// <summary>The tug-of-war lane: gradient track, sleeper ticks every 10 percent, notch with a triangle head, dashed ghost trail; hatched when Value is NaN.</summary>
        public static bool LaneBar(AvQuadBuffer b, in StrLane lane)
        {
            if (Bad(lane.X) || Bad(lane.Y) || Bad(lane.W) || Bad(lane.H) || lane.W <= 0f || lane.H <= 0f) return true;
            float x0 = lane.X, y0 = lane.Y, x1 = x0 + lane.W, y1 = y0 + lane.H;

            // Not observed: no side, no notch, no trail; the hatch says "no data" without claiming a number.
            if (float.IsNaN(lane.Value) || float.IsInfinity(lane.Value))
                return HatchRect(b, x0, y0, lane.W, lane.H, HatchSpacing, HatchWidth, lane.Tick);

            float v = lane.Value < 0f ? 0f : lane.Value > 1f ? 1f : lane.Value;
            bool ok = b.Add(x0, y0, x1, y0, x1, y1, x0, y1, lane.Own, lane.Hostile, lane.Hostile, lane.Own);
            for (int k = 1; k <= 9; k++)
            {
                float tx = x0 + lane.W * k * 0.1f;
                ok &= AvStrokes.Line(b, tx, y0, tx, y1, TickWidth, lane.Tick);
            }

            float nx = x0 + v * lane.W;
            if (!Bad(lane.Trend) && Math.Abs(lane.Trend) > 0f)
            {
                float g = v - lane.Trend;
                float gx = x0 + (g < 0f ? 0f : g > 1f ? 1f : g) * lane.W;
                float dist = Math.Abs(nx - gx);
                if (dist > 1f)
                {
                    float cycle = Math.Max(5f, dist / MaxGhostDashes);
                    ok &= DashedLine(b, gx, (y0 + y1) * 0.5f, nx, (y0 + y1) * 0.5f, cycle * 0.6f, cycle * 0.4f, 0f, GhostWidth, lane.Ghost);
                }
            }

            ok &= AvStrokes.Line(b, nx, y0 - 2f, nx, y1 + 2f, NotchWidth, lane.Notch);
            ok &= b.Add(nx, y1, nx + NotchHead, y1 + 2f * NotchHead, nx - NotchHead, y1 + 2f * NotchHead, nx, y1,
                        lane.Notch, lane.Notch, lane.Notch, lane.Notch);
            return ok;
        }

        // The head is a triangle written as one quad with the tip repeated; its base is the arrow's last h units.
        private static bool Head(AvQuadBuffer b, float tipX, float tipY, float baseX, float baseY, float ux, float uy, float h, Rgba c)
        {
            float hw = h * 0.5f, nx = -uy * hw, ny = ux * hw;
            return b.Add(tipX, tipY, baseX + nx, baseY + ny, baseX - nx, baseY - ny, tipX, tipY, c, c, c, c);
        }

        private static bool Shape(float ax, float ay, float bx, float by, float width, float head,
                                  out float ux, out float uy, out float h, out float len)
        {
            ux = uy = h = len = 0f;
            if (Bad(ax) || Bad(ay) || Bad(bx) || Bad(by) || Bad(width) || Bad(head) || head <= 0f) return false;
            len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            if (len < 1e-4f) return false;
            ux = (bx - ax) / len; uy = (by - ay) / len;
            h = Math.Min(head, len);
            return true;
        }

        private static bool Bad(float v) => float.IsNaN(v) || float.IsInfinity(v);
    }
}
