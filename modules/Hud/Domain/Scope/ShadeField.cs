using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The TAC BOARD's cloud field (LAYOUT.md 2): ONE soft dark field behind every block, not a cloud per block. A point's strength is the strongest of the blocks' fall-offs, never their sum, so where two blocks
    /// stand close the shade is no darker than over one. A fall-off is <see cref="Profile"/> of the distance outside the block (Euclidean: round corners) over <see cref="ScopeLayout.CloudFeather"/> units, the distance
    /// measured from a point warped by up to <see cref="ScopeLayout.CloudWarp"/> units and the fade length varied by a slow wobble, so no edge is a ruled line; the core is lightened in places by a mottle and falls with box y
    /// the way the sun band does. Pure maths in box units; the presenter bakes it into one small texture (a texel is <see cref="ScopeLayout.CloudCell"/> units) over <see cref="Area"/> whenever the blocks change. No UnityEngine.
    /// <see cref="At"/> is the formula; <see cref="Bake"/> is the same numbers for a whole texture, fast enough to bake in a frame.
    /// </summary>
    internal static class ShadeField
    {
        /// <summary>How far outside a block its cloud can reach: the wobble stretches the fade length to Feather x (1 + Wobble) at most and the warp moves the point by up to Warp each way, so the field is 0 beyond this (260).</summary>
        public const float Reach = ScopeLayout.CloudFeather * (1f + ScopeLayout.CloudWobble) + 2f * ScopeLayout.CloudWarp;

        /// <summary>The rectangle the field is baked over: the present blocks' bounding box grown by <see cref="Reach"/> (so the bake has no cut edge), on the texel grid (a texel keeps its box units while the blocks move). Not present: no block.</summary>
        public static BoardRect Area(in BoardGeometry geo)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < BoardGeometry.BlockCount; i++)
            {
                BoardRect r = geo.Block(i);
                if (!r.Present) continue;
                x0 = Math.Min(x0, r.X);
                y0 = Math.Min(y0, r.Y);
                x1 = Math.Max(x1, r.Right);
                y1 = Math.Max(y1, r.Bottom);
            }
            if (x0 > x1) return default;
            const float cell = ScopeLayout.CloudCell;
            x0 = MathF.Floor((x0 - Reach) / cell) * cell;
            y0 = MathF.Floor((y0 - Reach) / cell) * cell;
            x1 = MathF.Ceiling((x1 + Reach) / cell) * cell;
            y1 = MathF.Ceiling((y1 + Reach) / cell) * cell;
            return new BoardRect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>The texels across and down a bake over <paramref name="area"/> takes.</summary>
        public static void Texels(in BoardRect area, out int w, out int h)
        {
            w = (int)MathF.Round(area.W / ScopeLayout.CloudCell);
            h = (int)MathF.Round(area.H / ScopeLayout.CloudCell);
        }

        /// <summary>A block's fall-off at <paramref name="u"/> = 1 - distance / fade length (0..1): the mean of u and its smoothstep, whose steepest slope is 1.25 where the smoothstep's is 1.5 (the edge reads softer).</summary>
        public static float Profile(float u) => 0.5f * u + 0.5f * u * u * (3f - 2f * u);

        /// <summary>The field at box point (<paramref name="x"/>, <paramref name="y"/>), 0..1: multiply by the core alpha (<see cref="ScopeLayout.CloudAlpha"/> x the pilot's Shade) for the shade's alpha.</summary>
        public static float At(in BoardGeometry geo, float x, float y)
        {
            float fade = ScopeLayout.CloudFeather * (1f + ScopeLayout.CloudWobble * 0.5f * (MathF.Sin(0.043f * x + 0.021f * y + 1.7f) + MathF.Sin(0.017f * x - 0.052f * y + 4.1f)));
            float wx = x + ScopeLayout.CloudWarp * (MathF.Sin(0.021f * y + 0.9f) + 0.6f * MathF.Sin(0.057f * y + 2.4f)), wy = y + ScopeLayout.CloudWarp * 0.7f * MathF.Sin(0.017f * x + 1.3f);
            float best = 0f;
            for (int i = 0; i < BoardGeometry.BlockCount; i++)
            {
                BoardRect r = geo.Block(i);
                if (!r.Present) continue;
                float dx = Math.Max(Math.Max(r.X - wx, 0f), wx - r.Right), dy = Math.Max(Math.Max(r.Y - wy, 0f), wy - r.Bottom), d = MathF.Sqrt(dx * dx + dy * dy);
                if (d >= fade) continue;
                float v = Profile(1f - d / fade);
                if (v > best) best = v;
            }
            if (best <= 0f) return 0f;
            float mottle = 1f - ScopeLayout.CloudMottle * (0.5f + 0.5f * MathF.Sin(0.027f * x + 0.019f * y + 0.6f) * MathF.Sin(0.033f * y - 0.011f * x + 2.2f));
            return best * mottle * Fall(y);
        }

        /// <summary>The core's share at box y: 1 at the top, <c>1 - CloudFall</c> from <see cref="ScopeLayout.CloudFallY"/> down.</summary>
        public static float Fall(float y) => 1f - ScopeLayout.CloudFall * Math.Min(1f, Math.Max(0f, y / ScopeLayout.CloudFallY));

        /// <summary>The two geometries have the same blocks (what the field depends on): when they do, the baked field is still right.</summary>
        public static bool SameBlocks(in BoardGeometry a, in BoardGeometry b)
        {
            for (int i = 0; i < BoardGeometry.BlockCount; i++)
            {
                BoardRect p = a.Block(i), q = b.Block(i);
                if (p.X != q.X || p.Y != q.Y || p.W != q.W || p.H != q.H) return false;
            }
            return true;
        }

        // The sines of x and of y worked out once per column and per row for Bake (every sine in the formula has x and y added inside it, and sin(a + b) = sin a cos b + cos a sin b).
        private static float[] colX = new float[0], colWarp = new float[0], colS1, colC1, colS2, colC2, colS3, colC3, colS4, colC4;
        private static float[] rowY = new float[0], rowWarp = new float[0], rowS1, rowC1, rowS2, rowC2, rowS3, rowC3, rowS4, rowC4, rowFall;

        /// <summary>
        /// The field over <paramref name="area"/> as bytes: <paramref name="alpha"/> gets <see cref="Texels"/> w x h of them (<c>round(At * 255)</c> at each texel's centre, row 0 the top). The same numbers as <see cref="At"/>, made
        /// cheaper three ways: the sines of the columns and the rows are worked out once; the fall-off rises with nearness, so the NEAREST block's is the strongest (one square root and one profile per texel, not one per block); and
        /// a block that cannot reach a row is left out of it. A live WIDE board is 359 x 222 texels. The scratch is shared (the board calls from one thread).
        /// </summary>
        public static void Bake(in BoardGeometry geo, in BoardRect area, byte[] alpha)
        {
            const float cell = ScopeLayout.CloudCell, warp = ScopeLayout.CloudWarp;
            Texels(area, out int w, out int h);
            if (colX.Length < w) { colX = new float[w]; colWarp = new float[w]; colS1 = new float[w]; colC1 = new float[w]; colS2 = new float[w]; colC2 = new float[w]; colS3 = new float[w]; colC3 = new float[w]; colS4 = new float[w]; colC4 = new float[w]; }
            if (rowY.Length < h) { rowY = new float[h]; rowWarp = new float[h]; rowS1 = new float[h]; rowC1 = new float[h]; rowS2 = new float[h]; rowC2 = new float[h]; rowS3 = new float[h]; rowC3 = new float[h]; rowS4 = new float[h]; rowC4 = new float[h]; rowFall = new float[h]; }
            for (int i = 0; i < w; i++)
            {
                float x = area.X + (i + 0.5f) * cell;
                colX[i] = x;
                colWarp[i] = warp * 0.7f * MathF.Sin(0.017f * x + 1.3f);
                float a1 = 0.043f * x + 1.7f, a2 = 0.017f * x + 4.1f, a3 = 0.027f * x + 0.6f, a4 = 2.2f - 0.011f * x;
                colS1[i] = MathF.Sin(a1); colC1[i] = MathF.Cos(a1);
                colS2[i] = MathF.Sin(a2); colC2[i] = MathF.Cos(a2);
                colS3[i] = MathF.Sin(a3); colC3[i] = MathF.Cos(a3);
                colS4[i] = MathF.Sin(a4); colC4[i] = MathF.Cos(a4);
            }
            for (int j = 0; j < h; j++)
            {
                float y = area.Y + (j + 0.5f) * cell;
                rowY[j] = y;
                rowWarp[j] = warp * (MathF.Sin(0.021f * y + 0.9f) + 0.6f * MathF.Sin(0.057f * y + 2.4f));
                float b1 = 0.021f * y, b2 = -0.052f * y, b3 = 0.019f * y, b4 = 0.033f * y;
                rowS1[j] = MathF.Sin(b1); rowC1[j] = MathF.Cos(b1);
                rowS2[j] = MathF.Sin(b2); rowC2[j] = MathF.Cos(b2);
                rowS3[j] = MathF.Sin(b3); rowC3[j] = MathF.Cos(b3);
                rowS4[j] = MathF.Sin(b4); rowC4[j] = MathF.Cos(b4);
                rowFall[j] = Fall(y);
            }
            Span<float> left = stackalloc float[BoardGeometry.BlockCount], top = stackalloc float[BoardGeometry.BlockCount], right = stackalloc float[BoardGeometry.BlockCount], bottom = stackalloc float[BoardGeometry.BlockCount];
            Span<int> near = stackalloc int[BoardGeometry.BlockCount];
            int n = 0;
            for (int i = 0; i < BoardGeometry.BlockCount; i++)
            {
                BoardRect r = geo.Block(i);
                if (!r.Present) continue;
                left[n] = r.X; top[n] = r.Y; right[n] = r.Right; bottom[n] = r.Bottom;
                n++;
            }
            const float longest = ScopeLayout.CloudFeather * (1f + ScopeLayout.CloudWobble), warpDown = warp * 0.7f;
            for (int j = 0; j < h; j++)
            {
                float y = rowY[j], wx = rowWarp[j];
                int m = 0;
                for (int k = 0; k < n; k++)      // the blocks this row can be within reach of (the warp moves it by up to warpDown)
                    if (y + warpDown + longest > top[k] && y - warpDown - longest < bottom[k]) near[m++] = k;
                for (int i = 0; i < w; i++)
                {
                    float fade = ScopeLayout.CloudFeather * (1f + ScopeLayout.CloudWobble * 0.5f * (colS1[i] * rowC1[j] + colC1[i] * rowS1[j] + colS2[i] * rowC2[j] + colC2[i] * rowS2[j]));
                    float px = colX[i] + wx, py = y + colWarp[i], nearest = float.MaxValue, best = 0f;
                    for (int q = 0; q < m; q++)
                    {
                        int k = near[q];
                        float dx = px < left[k] ? left[k] - px : px > right[k] ? px - right[k] : 0f, dy = py < top[k] ? top[k] - py : py > bottom[k] ? py - bottom[k] : 0f, d2 = dx * dx + dy * dy;
                        if (d2 < nearest) nearest = d2;
                    }
                    if (nearest < fade * fade) best = Profile(1f - MathF.Sqrt(nearest) / fade);
                    float mottle = 1f - ScopeLayout.CloudMottle * (0.5f + 0.5f * (colS3[i] * rowC3[j] + colC3[i] * rowS3[j]) * (colS4[i] * rowC4[j] + colC4[i] * rowS4[j]));
                    alpha[j * w + i] = (byte)(best * mottle * rowFall[j] * 255f + 0.5f);
                }
            }
        }
    }
}
