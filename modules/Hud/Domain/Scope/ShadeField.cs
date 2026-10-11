using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// The TAC BOARD's cloud field (LAYOUT.md 2): ONE soft dark field behind every block, not a cloud per block. A point's strength is the strongest of the blocks' fall-offs, never their sum, so where two blocks
    /// stand close the shade is no darker than over one. A fall-off is a smoothstep of the distance outside the block (Euclidean: round corners) over <see cref="ScopeLayout.CloudFeather"/> units, the distance varied by a
    /// slow wobble so no edge is a straight line; the core is lightened in places by a mottle and falls with box y the way the sun band does. Pure maths in box units; the presenter bakes it into one small texture
    /// (a texel is <see cref="ScopeLayout.CloudCell"/> units) over <see cref="Area"/> whenever the blocks change. No UnityEngine.
    /// </summary>
    internal static class ShadeField
    {
        /// <summary>How far outside a block its cloud can reach: the wobble shortens the fade distance to Feather / (1 - Wobble) at worst, so the field is 0 beyond this.</summary>
        public const float Reach = ScopeLayout.CloudFeather / (1f - ScopeLayout.CloudWobble);

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

        /// <summary>The field at box point (<paramref name="x"/>, <paramref name="y"/>), 0..1: multiply by the core alpha (<see cref="ScopeLayout.CloudAlpha"/> x the pilot's Shade) for the shade's alpha.</summary>
        public static float At(in BoardGeometry geo, float x, float y)
        {
            float wobble = 1f + ScopeLayout.CloudWobble * 0.5f * (MathF.Sin(0.043f * x + 0.021f * y + 1.7f) + MathF.Sin(0.017f * x - 0.052f * y + 4.1f));
            float best = 0f;
            for (int i = 0; i < BoardGeometry.BlockCount; i++)
            {
                BoardRect r = geo.Block(i);
                if (!r.Present) continue;
                float dx = Math.Max(Math.Max(r.X - x, 0f), x - r.Right), dy = Math.Max(Math.Max(r.Y - y, 0f), y - r.Bottom);
                float q = MathF.Sqrt(dx * dx + dy * dy) * wobble / ScopeLayout.CloudFeather;      // 0 at the block, 1 where its cloud ends
                if (q >= 1f) continue;
                float u = 1f - q, v = u * u * (3f - 2f * u);
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
    }
}
