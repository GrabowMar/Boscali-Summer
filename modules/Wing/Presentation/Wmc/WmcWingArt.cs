using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>A top-left, y-down drawing surface over one kit <see cref="AvQuadBuffer"/> (the kit's meshes are bottom-left, y-up):
    /// the squadron insignia, rank chevrons, ribbons, the barcode and the aircraft outline are all drawn with it. No texture, no font.</summary>
    internal readonly struct WingInk
    {
        private static readonly float[] Xs = new float[48], Ys = new float[48];
        private readonly AvQuadBuffer buffer;
        private readonly float height;

        public WingInk(AvQuadBuffer b, float h)
        {
            buffer = b;
            height = h;
        }

        public static Rgba C(Color c) => new Rgba(c.r, c.g, c.b, c.a);

        public void Line(float x0, float y0, float x1, float y1, float w, Color c) =>
            AvStrokes.Line(buffer, x0, height - y0, x1, height - y1, w, C(c));

        public void Rect(float x, float y, float w, float h, Color c) => AvStrokes.Fill(buffer, x, height - y - h, w, h, C(c));

        /// <summary>A polyline through (xs[i], ys[i]) (top-left space).</summary>
        public void Poly(float[] xs, float[] ys, int n, bool closed, float w, Color c)
        {
            n = Mathf.Min(n, Xs.Length);
            for (int i = 0; i < n; i++)
            {
                Xs[i] = xs[i];
                Ys[i] = height - ys[i];
            }
            AvStrokes.Polyline(buffer, Xs, Ys, n, closed, w, C(c));
        }

        public void Diamond(float cx, float cy, float r, float w, Color c) => AvStrokes.Diamond(buffer, cx, height - cy, r, w, C(c));

        /// <summary>Rank stripes: 1 Rookie, 2 Wingman, 3 Veteran, 3 with a bar above for Ace, 3 with a diamond above for Legend.
        /// Centred on (cx, cy), about <paramref name="size"/> wide.</summary>
        public void Rank(float cx, float cy, float size, WingRank rank, Color c)
        {
            int stripes = rank == WingRank.Rookie ? 1 : rank == WingRank.Wingman ? 2 : 3;
            float half = size * 0.5f, drop = size * 0.28f, pitch = Mathf.Max(2.2f, size * 0.24f);
            float top = cy - ((stripes - 1) * pitch) * 0.5f;
            for (int i = 0; i < stripes; i++)
            {
                float y = top + i * pitch;
                Line(cx - half, y + drop, cx, y, 1.3f, c);
                Line(cx, y, cx + half, y + drop, 1.3f, c);
            }
            if (rank == WingRank.Ace) Line(cx - half * 0.7f, top - pitch * 0.9f, cx + half * 0.7f, top - pitch * 0.9f, 1.3f, c);
            if (rank == WingRank.Legend) Diamond(cx, top - pitch * 1.1f, size * 0.16f, 1.2f, c);
        }

        /// <summary>The squadron's shield: outline, a chevron and a star (a diamond), in <paramref name="edge"/> and <paramref name="accent"/>.</summary>
        public void Insignia(float x, float y, float w, float h, Color edge, Color accent)
        {
            float[] px = { x, x + w, x + w, x + w * 0.5f, x };
            float[] py = { y, y, y + h * 0.58f, y + h, y + h * 0.58f };
            Poly(px, py, 5, true, 1.4f, edge);
            float cx = x + w * 0.5f;
            Line(x + w * 0.2f, y + h * 0.62f, cx, y + h * 0.38f, 1.6f, accent);
            Line(cx, y + h * 0.38f, x + w * 0.8f, y + h * 0.62f, 1.6f, accent);
            Line(x + w * 0.2f, y + h * 0.76f, cx, y + h * 0.52f, 1.6f, accent);
            Line(cx, y + h * 0.52f, x + w * 0.8f, y + h * 0.76f, 1.6f, accent);
            Diamond(cx, y + h * 0.2f, w * 0.12f, 1.2f, accent);
        }

        /// <summary>A paper clip on the portrait's top edge.</summary>
        public void Clip(float x, float y, Color c)
        {
            float[] px = { x, x, x + 9f, x + 9f, x + 3f, x + 3f, x + 6f };
            float[] py = { y + 14f, y + 2f, y + 2f, y + 18f, y + 18f, y + 6f, y + 6f };
            Poly(px, py, 7, false, 1.2f, c);
        }

        /// <summary>One ribbon: a field of <paramref name="field"/> with two stripes of <paramref name="stripe"/>.</summary>
        public void Ribbon(float x, float y, float w, float h, Color field, Color stripe)
        {
            Rect(x, y, w, h, field);
            Rect(x + w * 0.3f, y, w * 0.12f, h, stripe);
            Rect(x + w * 0.58f, y, w * 0.12f, h, stripe);
        }

        /// <summary>Barcode bars (widths in units, one unit of gap after each) from x; returns the width drawn.</summary>
        public float Barcode(float x, float y, float h, int[] widths, int n, float unit, Color c)
        {
            float cx = x;
            for (int i = 0; i < n; i++)
            {
                Rect(cx, y, widths[i] * unit, h, c);
                cx += (widths[i] + 1) * unit;
            }
            return cx - x;
        }
    }

    /// <summary>A generic top-view aircraft outline in unit space (x −1..1 across the wings, y 1 nose … −1 tail), mirrored about the spine.
    /// The AIRCRAFT page's damage map draws the real parts over it; the outline is only a drawing.</summary>
    internal static class WingSilhouette
    {
        private static readonly float[] HalfX = { 0f, 0.07f, 0.1f, 0.11f, 0.88f, 0.88f, 0.14f, 0.12f, 0.4f, 0.4f, 0.08f, 0f };
        private static readonly float[] HalfY = { 1f, 0.78f, 0.4f, 0.16f, -0.12f, -0.3f, -0.36f, -0.62f, -0.8f, -0.92f, -0.9f, -0.94f };

        public static void Draw(in WingInk ink, float x, float y, float w, float h, Color c)
        {
            int half = HalfX.Length;
            var xs = new float[half * 2 - 2];
            var ys = new float[half * 2 - 2];
            float cx = x + w * 0.5f, cy = y + h * 0.5f, sx = w * 0.5f, sy = h * 0.5f;
            int n = 0;
            for (int i = 0; i < half; i++)
            {
                xs[n] = cx + HalfX[i] * sx;
                ys[n++] = cy - HalfY[i] * sy;
            }
            for (int i = half - 2; i >= 1; i--)
            {
                xs[n] = cx - HalfX[i] * sx;
                ys[n++] = cy - HalfY[i] * sy;
            }
            ink.Poly(xs, ys, n, true, 1.2f, c);
        }

        /// <summary>A part's place on the drawing: <paramref name="px"/> is its sideways offset over the widest, <paramref name="pz"/> its length
        /// offset over the longest, both −1..1 (nose positive).</summary>
        public static Vector2 Place(float x, float y, float w, float h, float px, float pz) =>
            new Vector2(x + w * 0.5f + px * w * 0.44f, y + h * 0.5f - pz * h * 0.47f);
    }
}
