using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The scope's drawing surface (HUD v8): the prototype's <c>HUD.*</c> helpers on the kit's <see cref="AvStrokes"/> and a pooled
    /// <see cref="ScopeText"/>, so a page ports from <c>pages/&lt;id&gt;.js</c> almost line for line. Coordinates are DESIGN UNITS (1 unit =
    /// 1 px at 1080p, origin top-left of the surface, y down); the canvas scales them by the board unit and flips them onto the mesh. Tones are
    /// <see cref="ScopeTone"/>s (the player's HUD theme). Compass angles are degrees clockwise from up, as in the prototype. Text always
    /// draws above every stroke of the same surface. Everything draws into pre-sized buffers and scratch arrays: a steady frame allocates
    /// nothing (feed strings through <see cref="ScopeStr"/>). Nothing needs clipping: the surface's container is a RectMask2D.
    /// <para>Polygons are loaded into a scratch path first (<see cref="Load"/> or <see cref="Poly"/>), then stroked, filled and hatched:
    /// <c>g.Load(jet, cx, cy, s); g.FillPoly(Ink, .07f); g.Path(Ink, .95f);</c></para>
    /// </summary>
    internal sealed partial class ScopeCanvas
    {
        private const int MaxPoints = 64;
        /// <summary>Em fractions a word is lifted by (the native mono face's line box against the canvas middle baseline).</summary>
        private const float LineNudge = 0.07f;

        private readonly AvQuadBuffer q;
        private readonly ScopeText text;
        private readonly float[] px = new float[MaxPoints], py = new float[MaxPoints];
        private readonly float[] ysort = new float[MaxPoints + 2], cx0 = new float[MaxPoints], cx1 = new float[MaxPoints], cxm = new float[MaxPoints];
        private readonly string surface;
        private ScopePalette pal;
        private float u = 1f, height, ox, oy, lift, pixel = 1f;

        /// <summary><paramref name="name"/> names the surface in the buffer-overflow log (one line per surface: the shell, the page, the overlay and the frame each fill their own buffer).</summary>
        public ScopeCanvas(AvQuadBuffer quads, ScopeText words, string name = "scope") { q = quads; text = words; surface = name; }

        /// <summary>Decorative furniture (the caption shelf) is drawn: <see cref="BoardEffective.Furniture"/>, set by the board before it paints. Everything else furniture-like (brackets, ticks, markers) stays at every declutter level.</summary>
        public bool Furniture { get; set; } = true;

        /// <summary>
        /// Start a frame: clear the buffer and the word pool. <paramref name="heightPx"/> is the surface's height in canvas units and <paramref name="devicePixel"/> the size of one
        /// DEVICE pixel in the same units (1 when the canvas is drawn 1:1; a CanvasScaler or a camera makes it something else). Hairlines, dashes and rectangle edges are laid on
        /// that grid, so a 1 px line is one device pixel wide and does not shimmer. The surface's own origin is on the grid (the board rounds where it puts it).
        /// </summary>
        public void Begin(in ScopePalette palette, float unit, float heightPx, float devicePixel = 1f)
        {
            pal = palette;
            u = unit;
            pixel = devicePixel > 0.001f ? devicePixel : 1f;
            height = heightPx;
            ox = oy = lift = 0f;
            chipSet = false;
            q.Clear();
            text?.Begin();
        }

        public void End()
        {
            text?.End();
            BufferGuard.Check(q, surface);
        }

        /// <summary>Move the origin (design units from the surface's top-left): a page strip or a gauge cell draws as if at (0, 0).</summary>
        public void Origin(float x, float y) { ox = x; oy = y; }

        /// <summary>Draw everything this many design units higher than it says, whatever the origin (the page body is the painters' own, lifted under the page's header shelf): until the next <see cref="Begin"/>.</summary>
        public void Raise(float units) { lift = units; }

        // ------------------------------------------------------------------------------------------------ maths

        /// <summary>Offset of a compass bearing at radius r: bearing 0 = up, clockwise (the prototype's HUD.polar).</summary>
        public static void Polar(float bearingDeg, float r, out float dx, out float dy)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            dx = Mathf.Sin(a) * r;
            dy = -Mathf.Cos(a) * r;
        }

        public static float Seg(float t, float a, float b) => Scalar.Clamp01((t - a) / (b - a));

        public static float EaseOut(float t) { t = 1f - Scalar.Clamp01(t); return 1f - t * t * t; }

        /// <summary>0..1 sine at <paramref name="hz"/>.</summary>
        public static float Pulse(float t, float hz) => 0.5f + 0.5f * Mathf.Sin(t * hz * 6.28318f);

        /// <summary>Square wave: true for the first <paramref name="duty"/> of each period.</summary>
        public static bool Blink(float t, float hz, float duty = 0.5f) => (t * hz % 1f + 1f) % 1f < duty;

        /// <summary>Stateless noise in [0, 1) for an integer-ish n (the prototype's HUD.hash): deterministic flicker, never Random.</summary>
        public static float Hash(float n)
        {
            unchecked
            {
                uint a = (uint)((int)Mathf.Floor(n) * 2654435761u + 12345u);
                a += 0x6d2b79f5u;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return Math.Min(0.99999994f, (float)((t ^ (t >> 14)) / 4294967296.0));
            }
        }

        // ------------------------------------------------------------------------------------------------ device mapping

        private float X(float x) => (ox + x) * u;

        private float Y(float y) => height - (oy + y - lift) * u;

        private Rgba C(ScopeTone tone, float alpha) => pal.Of(tone, alpha);

        /// <summary>A stroke width in canvas units: whole device pixels, at least one.</summary>
        private float Lw(float w) => Math.Max(pixel, Mathf.Round(w * u / pixel) * pixel);

        /// <summary>A coordinate of an axis-aligned line snapped to the device pixel grid (an odd number of pixels wide to a pixel centre).</summary>
        private float Snap(float v, float lw) => ((int)Math.Round(lw / pixel) & 1) == 1 ? Mathf.Floor(v / pixel) * pixel + pixel * 0.5f : Mathf.Round(v / pixel) * pixel;

        /// <summary>
        /// The y of a horizontal line on the device pixel grid: the row is counted from the TOP of the surface, where the design counts it (LAYOUT.md 0: a line at y 6 is the row from 6 to 7, its centre at floor(y) + 0.5). The canvas
        /// counts from its bottom, where the same floor would land the line one row too high.
        /// </summary>
        private float SnapY(float v, float lw) => height - Snap(height - v, lw);

        /// <summary>An edge of a rectangle on the device pixel grid. Half a pixel goes UP: Mathf.Round takes it to the even pixel, so a 3 high bar centred on a whole unit came out 2 or 4 high by the parity of its row.</summary>
        private float Edge(float v) => Mathf.Floor(v / pixel + 0.5f) * pixel;

        // ------------------------------------------------------------------------------------------------ lines

        /// <summary>A line. <paramref name="dash"/> and <paramref name="gap"/> (design units, 0 = solid) make it dashed; <paramref name="offset"/> slides the
        /// pattern (animate with <c>-t * 12</c>).</summary>
        public void Line(float x1, float y1, float x2, float y2, ScopeTone tone = Ink, float alpha = 1f, float w = 1f, float dash = 0f, float gap = 0f, float offset = 0f)
        {
            float lw = Lw(w), ax = X(x1), ay = Y(y1), bx = X(x2), by = Y(y2);
            if (ax == bx) ax = bx = Snap(ax, lw);
            else if (ay == by) ay = by = SnapY(ay, lw);
            Rgba c = C(tone, alpha);
            if (dash <= 0f) { AvStrokes.Line(q, ax, ay, bx, by, lw, c); return; }
            DashedPx(ax, ay, bx, by, Math.Max(pixel, Edge(dash * u)), Math.Max(pixel, Edge(gap * u)), Edge(offset * u), lw, c);      // whole pixels: a 1 px dash never lands half on a pixel and drops out
        }

        /// <summary>A line with the prototype's default dash (4 on, 3 off).</summary>
        public void Dashed(float x1, float y1, float x2, float y2, ScopeTone tone = Ink, float alpha = 1f, float w = 1f, float offset = 0f) =>
            Line(x1, y1, x2, y2, tone, alpha, w, 4f, 3f, offset);

        private void DashedPx(float ax, float ay, float bx, float by, float dash, float gap, float offset, float lw, Rgba c)
        {
            float dx = bx - ax, dy = by - ay, len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f || dash <= 0f) return;
            float ux = dx / len, uy = dy / len, period = dash + gap;
            float pos = -(((offset % period) + period) % period);
            for (; pos < len; pos += period)
            {
                float s0 = Math.Max(pos, 0f), s1 = Math.Min(pos + dash, len);
                if (s1 > s0) AvStrokes.Line(q, ax + ux * s0, ay + uy * s0, ax + ux * s1, ay + uy * s1, lw, c);
            }
        }

        /// <summary>An arrow: a line with an open head at (x2, y2).</summary>
        public void Arrow(float x1, float y1, float x2, float y2, ScopeTone tone = Ink, float alpha = 1f, float w = 1f, float head = 5f)
        {
            Line(x1, y1, x2, y2, tone, alpha, w);
            float a = Mathf.Atan2(y2 - y1, x2 - x1);
            Line(x2 - Mathf.Cos(a - 0.5f) * head, y2 - Mathf.Sin(a - 0.5f) * head, x2, y2, tone, alpha, w);
            Line(x2 - Mathf.Cos(a + 0.5f) * head, y2 - Mathf.Sin(a + 0.5f) * head, x2, y2, tone, alpha, w);
        }

        /// <summary>A polyline from oldest to newest whose alpha steps up toward the newest point (a missile's trail). <paramref name="xy"/> holds x, y pairs.</summary>
        public void Trail(float[] xy, int points, ScopeTone tone = Ink, float alpha = 1f, float w = 1f, int steps = 6)
        {
            int n = points - 1;
            for (int i = 0; i < n; i++)
            {
                float k = (float)Math.Ceiling((i + 1f) / n * steps) / steps;
                Line(xy[2 * i], xy[2 * i + 1], xy[2 * i + 2], xy[2 * i + 3], tone, alpha * (0.12f + 0.88f * k), w);
            }
        }

        // ------------------------------------------------------------------------------------------------ rects, rings, arcs

        /// <summary>A filled rectangle.</summary>
        public void Fill(float x, float y, float w, float h, ScopeTone tone = Ink, float alpha = 1f)
        {
            if (w <= 0f || h <= 0f || !KeepFill(x, y, w, h, tone, alpha)) return;
            float x0 = Edge(X(x)), y0 = Edge(Y(y + h)), x1 = Math.Max(Edge(X(x + w)), x0 + pixel), y1 = Math.Max(Edge(Y(y)), y0 + pixel);
            AvStrokes.Fill(q, x0, y0, x1 - x0, y1 - y0, C(tone, alpha));
        }

        /// <summary>
        /// A 1 px outlined box (width <paramref name="lw"/> 0: no outline), optionally filled in <paramref name="fill"/> at <paramref name="fillAlpha"/>
        /// and with 45 degree chamfers on any corner (there are no rounded corners on this board).
        /// </summary>
        public void Box(float x, float y, float w, float h, ScopeTone tone = Ink, float alpha = 1f, float lw = 1f, ScopeTone fill = Bg, float fillAlpha = 0f,
            float chamTL = 0f, float chamTR = 0f, float chamBR = 0f, float chamBL = 0f)
        {
            if (OpenBox(x, y, w, h, tone, alpha, lw, fill, fillAlpha, chamTL + chamTR + chamBR + chamBL > 0f)) return;
            if (fillAlpha > 0f)
            {
                if (chamTL + chamTR + chamBR + chamBL <= 0f) Fill(x, y, w, h, fill, fillAlpha);
                else
                {
                    BoxPath(x, y, w, h, chamTL, chamTR, chamBR, chamBL, 0f, out int n);
                    FillPoly(n, fill, fillAlpha);
                }
            }
            if (lw <= 0f) return;
            if (lw == 1f && chamTL + chamTR + chamBR + chamBL <= 0f) { Outline(x, y, w, h, tone, alpha); return; }
            float a = lw % 2f == 1f ? 0.5f : 0f;
            BoxPath(x, y, w, h, chamTL, chamTR, chamBR, chamBL, a, out int m);
            Path(m, tone, alpha, lw);
        }

        /// <summary>
        /// A 1 unit outline of a square-cornered box as four filled strips (the top and bottom rows whole, the sides between them), so every pixel of it is covered once, corners included. A stroked path puts its
        /// corner pixels on the knife edge of two butt-ended lines, and which of them the GPU fills varied: a 4 x 4 lamp came out with two opposite corners missing.
        /// </summary>
        private void Outline(float x, float y, float w, float h, ScopeTone tone, float alpha)
        {
            if (w <= 2f || h <= 2f) { Fill(x, y, w, h, tone, alpha); return; }
            Fill(x, y, w, 1f, tone, alpha);
            Fill(x, y + h - 1f, w, 1f, tone, alpha);
            Fill(x, y + 1f, 1f, h - 2f, tone, alpha);
            Fill(x + w - 1f, y + 1f, 1f, h - 2f, tone, alpha);
        }

        private void BoxPath(float x, float y, float w, float h, float tl, float tr, float br, float bl, float a, out int n)
        {
            float x0 = x + a, y0 = y + a, x1 = x + w - a, y1 = y + h - a;
            n = 0;
            Add(ref n, x0 + tl, y0); Add(ref n, x1 - tr, y0); if (tr > 0f) Add(ref n, x1, y0 + tr);
            Add(ref n, x1, y1 - br); if (br > 0f) Add(ref n, x1 - br, y1);
            Add(ref n, x0 + bl, y1); if (bl > 0f) Add(ref n, x0, y1 - bl);
            Add(ref n, x0, y0 + tl);
        }

        private void Add(ref int n, float x, float y)
        {
            if (n >= MaxPoints) return;
            px[n] = x; py[n] = y; n++;
        }

        /// <summary>A ring (or a disc with <paramref name="fillAlpha"/> above 0 in <paramref name="fill"/>); dashed with <paramref name="dash"/> / <paramref name="gap"/>.</summary>
        public void Ring(float cx, float cy, float r, ScopeTone tone = Ink, float alpha = 1f, float w = 1f, ScopeTone fill = Ink, float fillAlpha = 0f, float dash = 0f, float gap = 0f, float offset = 0f)
        {
            float rp = r * u, cxp = X(cx), cyp = Y(cy);
            if (rp <= 0f) return;
            if (fillAlpha > 0f && KeepPoly(fill, fillAlpha)) AvStrokes.Ring(q, cxp, cyp, rp * 0.5f, Segs(rp, 360f), rp, C(fill, fillAlpha));
            if (w <= 0f) return;
            if (dash <= 0f) AvStrokes.Ring(q, cxp, cyp, rp, Segs(rp, 360f), Lw(w), C(tone, alpha));
            else DashedArcPx(cxp, cyp, rp, 90f, 90f - 360f, dash * u, gap * u, offset * u, Lw(w), C(tone, alpha));
        }

        /// <summary>An arc from compass degrees <paramref name="a0"/> clockwise to <paramref name="a1"/>.</summary>
        public void Arc(float cx, float cy, float r, float a0, float a1, ScopeTone tone = Ink, float alpha = 1f, float w = 1f, float dash = 0f, float gap = 0f, float offset = 0f)
        {
            if (a1 < a0) a1 += 360f;
            float rp = r * u;
            if (rp <= 0f || a1 - a0 < 0.01f) return;
            if (dash <= 0f) AvStrokes.Arc(q, X(cx), Y(cy), rp, 90f - a0, 90f - a1, Segs(rp, a1 - a0), Lw(w), C(tone, alpha));
            else DashedArcPx(X(cx), Y(cy), rp, 90f - a0, 90f - a1, dash * u, gap * u, offset * u, Lw(w), C(tone, alpha));
        }

        private static int Segs(float rp, float degrees) => Math.Max(6, Math.Min(256, (int)(rp * degrees * 0.0174533f / 6f) + 1));

        private void DashedArcPx(float cx, float cy, float rp, float th0, float th1, float dash, float gap, float offset, float lw, Rgba c)
        {
            float span = th0 - th1, len = Math.Abs(span) * 0.0174533f * rp, period = dash + gap;
            if (len < 1e-3f || dash <= 0f) return;
            float dir = span >= 0f ? -1f : 1f, pos = -(((offset % period) + period) % period);
            for (; pos < len; pos += period)
            {
                float s0 = Math.Max(pos, 0f), s1 = Math.Min(pos + dash, len);
                if (s1 <= s0) continue;
                float t0 = th0 + dir * s0 / rp * 57.29578f, t1 = th0 + dir * s1 / rp * 57.29578f;
                AvStrokes.Arc(q, cx, cy, rp, t0, t1, Math.Max(1, (int)((s1 - s0) / 6f) + 1), lw, c);
            }
        }

        // ------------------------------------------------------------------------------------------------ polygons

        /// <summary>Transform the unit polygon <paramref name="pts"/> (x, y pairs, <paramref name="n"/> points) into the scratch path: scaled, optionally mirrored
        /// on x, rotated clockwise by <paramref name="rotDeg"/> and moved to (<paramref name="x"/>, <paramref name="y"/>). Returns the point count.</summary>
        public int Load(float[] pts, int n, float x, float y, float scale = 1f, float rotDeg = 0f, bool mirrorX = false)
        {
            n = Math.Min(n, MaxPoints);
            float a = rotDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            for (int i = 0; i < n; i++)
            {
                float vx = pts[2 * i] * scale * (mirrorX ? -1f : 1f), vy = pts[2 * i + 1] * scale;
                px[i] = x + vx * ca - vy * sa;
                py[i] = y + vx * sa + vy * ca;
            }
            return n;
        }

        /// <summary>Like <see cref="Load"/> but scaled by <paramref name="sx"/> across and <paramref name="sy"/> down (a store's silhouette: long and thin) with every vertex on a pixel centre, so its lines stay crisp.</summary>
        public int LoadXY(float[] pts, int n, float x, float y, float sx, float sy)
        {
            n = Math.Min(n, MaxPoints);
            for (int i = 0; i < n; i++)
            {
                px[i] = Mathf.Round(x + pts[2 * i] * sx - 0.5f) + 0.5f;
                py[i] = Mathf.Round(y + pts[2 * i + 1] * sy - 0.5f) + 0.5f;
            }
            return n;
        }

        /// <summary>Load raw points already in surface design units.</summary>
        public int Poly(float[] xy, int n) => Load(xy, n, 0f, 0f);

        /// <summary>Stroke the scratch path (closed unless <paramref name="close"/> is false).</summary>
        public void Path(int n, ScopeTone tone = Ink, float alpha = 1f, float w = 1f, bool close = true, float dash = 0f, float gap = 0f)
        {
            int segs = close ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int j = (i + 1) % n;
                Line(px[i], py[i], px[j], py[j], tone, alpha, w, dash, gap);
            }
        }

        /// <summary>Fill the scratch path (even-odd; any simple polygon, concave too) as horizontal trapezoids.</summary>
        public void FillPoly(int n, ScopeTone tone, float alpha)
        {
            if (n < 3 || !KeepPoly(tone, alpha)) return;
            int m = 0;
            for (int i = 0; i < n; i++) ysort[m++] = py[i];
            Array.Sort(ysort, 0, m);
            Rgba c = C(tone, alpha);
            for (int k = 0; k + 1 < m; k++)
            {
                float y0 = ysort[k], y1 = ysort[k + 1];
                if (y1 - y0 < 1e-3f) continue;
                float ym = (y0 + y1) * 0.5f;
                int cn = 0;
                for (int i = 0; i < n && cn < MaxPoints; i++)
                {
                    int j = (i + 1) % n;
                    float ay = py[i], by = py[j];
                    if (!((ay <= ym && by > ym) || (by <= ym && ay > ym))) continue;
                    float dx = px[j] - px[i], dy = by - ay;
                    cx0[cn] = px[i] + dx * (y0 - ay) / dy;
                    cx1[cn] = px[i] + dx * (y1 - ay) / dy;
                    cxm[cn] = px[i] + dx * (ym - ay) / dy;
                    cn++;
                }
                for (int a = 1; a < cn; a++)       // insertion sort by x at mid height
                    for (int b = a; b > 0 && cxm[b] < cxm[b - 1]; b--)
                    {
                        (cx0[b], cx0[b - 1]) = (cx0[b - 1], cx0[b]);
                        (cx1[b], cx1[b - 1]) = (cx1[b - 1], cx1[b]);
                        (cxm[b], cxm[b - 1]) = (cxm[b - 1], cxm[b]);
                    }
                for (int a = 0; a + 1 < cn; a += 2)
                    q.Add(X(cx0[a]), Y(y0), X(cx0[a + 1]), Y(y0), X(cx1[a + 1]), Y(y1), X(cx1[a]), Y(y1), c, c, c, c);
            }
        }

        /// <summary>Hatch the scratch path with 45 degree lines <paramref name="spacing"/> apart (rising to the right, as the prototype's damage zones).</summary>
        public void Hatch(int n, float spacing, ScopeTone tone, float alpha, float w = 1f)
        {
            if (n < 3 || spacing <= 0f) return;
            float s0 = float.MaxValue, s1 = float.MinValue;
            for (int i = 0; i < n; i++) { float s = px[i] + py[i]; if (s < s0) s0 = s; if (s > s1) s1 = s; }
            for (float s = (float)Math.Ceiling(s0 / spacing) * spacing; s < s1; s += spacing)
            {
                int cn = 0;
                for (int i = 0; i < n && cn < MaxPoints; i++)
                {
                    int j = (i + 1) % n;
                    float fa = px[i] + py[i] - s, fb = px[j] + py[j] - s;
                    if ((fa < 0f) == (fb < 0f)) continue;
                    cxm[cn++] = px[i] + (px[j] - px[i]) * (fa / (fa - fb));
                }
                for (int a = 1; a < cn; a++)
                    for (int b = a; b > 0 && cxm[b] < cxm[b - 1]; b--) (cxm[b], cxm[b - 1]) = (cxm[b - 1], cxm[b]);
                for (int a = 0; a + 1 < cn; a += 2) Line(cxm[a], s - cxm[a], cxm[a + 1], s - cxm[a + 1], tone, alpha, w);
            }
        }

        // ------------------------------------------------------------------------------------------------ words

        /// <summary>The width in design units of <paramref name="s"/> at <paramref name="size"/> (the face is monospaced; a '.' between two digits is <see cref="PageInk.DotAdvance"/> wide).</summary>
        public static float TextW(ReadOnlySpan<char> s, float size = 12f) => (s.Length * ScopeText.DesignAdvance - PageInk.Decimals(s) * (ScopeText.DesignAdvance - PageInk.DotAdvance)) * size;

        /// <summary>One word. (<paramref name="x"/>, <paramref name="y"/>) is the anchor by <paramref name="align"/>; y is the MIDDLE of the line. Returns the width.</summary>
        public float Text(ReadOnlySpan<char> s, float x, float y, ScopeTone tone = Ink, float size = 12f, ScopeAlign align = ScopeAlign.Left, bool bold = false, float alpha = 1f)
        {
            if (Open) PageText(s, x, y, size, ref tone, ref bold, ref alpha);
            if ((tone == Dim || tone == Faint) && alpha < ScopePalette.DimTextFloor) alpha = ScopePalette.DimTextFloor;      // DIM text stays readable: 0.62 of the ink at the least
            Rgba c = C(tone, alpha);
            // TMP centres the line box, the canvas "middle" baseline the em box: the glyphs sit a little low unless nudged up.
            text?.Draw(s, X(x), Y2(y) - size * u * LineNudge, size * u, align, new Color(c.R, c.G, c.B, c.A), bold);
            return TextW(s, size);
        }

        private float Y2(float y) => (oy + y - lift) * u;

        /// <summary>Like <see cref="Text"/> but shrunk (to <paramref name="minSize"/>) and then cut with an ellipsis to fit <paramref name="maxW"/>.</summary>
        public float FitText(ReadOnlySpan<char> s, float x, float y, float maxW, ScopeTone tone = Ink, float size = 12f, float minSize = 12f, ScopeAlign align = ScopeAlign.Left, bool bold = false, float alpha = 1f)
        {
            while (size > minSize && TextW(s, size) > maxW) size -= 1f;
            if (TextW(s, size) > maxW)
            {
                int keep = Math.Max(1, (int)(maxW / (ScopeText.DesignAdvance * size)) - 1);
                if (keep < s.Length)
                {
                    Span<char> cut = stackalloc char[keep + 1];
                    s.Slice(0, keep).CopyTo(cut);
                    cut[keep] = '…';
                    return Text(cut, x, y, tone, size, align, bold, alpha);
                }
            }
            return Text(s, x, y, tone, size, align, bold, alpha);
        }
    }
}
