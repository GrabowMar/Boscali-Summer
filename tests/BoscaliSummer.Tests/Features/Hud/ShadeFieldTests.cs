using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The TAC BOARD's cloud field (LAYOUT.md 2): one field over all the blocks, the MAX of their round fall-offs (never the sum, so no dark seam where two blocks stand close), a warped edge, no cut edge where it is baked.</summary>
    internal static class ShadeFieldTests
    {
        public static void Run()
        {
            Conforms();
            Overlaps();
            Area();
            Baked();
        }

        private static BoardGeometry Board(BoardLayout layout, float pageH, bool pwr = true, bool lamps = true) => ScopeLayout.Compute(new BoardBlocks
        {
            Layout = layout, Hero = true, Pwr = pwr, Exp = true, Lamps = lamps, PageH = pageH, GaugeLines = pageH > ScopeLayout.Compact ? 2 : 1, Ticker = true,
        });

        private static BoardGeometry[] Boards() => new[]
        {
            Board(BoardLayout.Wide, ScopeLayout.Compact), Board(BoardLayout.Wide, ScopeLayout.Full), Board(BoardLayout.Wide, 187f, false), Board(BoardLayout.Wide, ScopeLayout.Compact, true, false),
            Board(BoardLayout.Stack, ScopeLayout.Compact), Board(BoardLayout.Stack, ScopeLayout.Full),
        };

        /// <summary>
        /// LAYOUT.md 2 written out from its text, in doubles: the distance of a block is the Euclidean one outside its rectangle from the point warped by 18 x (sin(0.021 y + 0.9) + 0.6 sin(0.057 y + 2.4)) across and
        /// 12.6 sin(0.017 x + 1.3) down, over L = 200 (1 +- 0.12) (the +- from two sines of x and y); the fall-off is (u + smoothstep(u)) / 2 for u = 1 - d / L, the blocks combine with MAX, the core falls to 65 % at y 380.
        /// </summary>
        private static double Reference(BoardGeometry g, double x, double y)
        {
            double fade = 200 * (1 + 0.12 * 0.5 * (Math.Sin(0.043 * x + 0.021 * y + 1.7) + Math.Sin(0.017 * x - 0.052 * y + 4.1))), max = 0;
            double wx = x + 18 * (Math.Sin(0.021 * y + 0.9) + 0.6 * Math.Sin(0.057 * y + 2.4)), wy = y + 12.6 * Math.Sin(0.017 * x + 1.3);
            for (int i = 0; i < BoardGeometry.BlockCount; i++)
            {
                BoardRect r = g.Block(i);
                if (!r.Present) continue;
                double dx = Math.Max(Math.Max(r.X - wx, 0), wx - r.Right), dy = Math.Max(Math.Max(r.Y - wy, 0), wy - r.Bottom), d = Math.Sqrt(dx * dx + dy * dy);
                if (d < fade) { double u = 1 - d / fade; max = Math.Max(max, 0.5 * u + 0.5 * u * u * (3 - 2 * u)); }
            }
            double mottle = 1 - 0.12 * (0.5 + 0.5 * Math.Sin(0.027 * x + 0.019 * y + 0.6) * Math.Sin(0.033 * y - 0.011 * x + 2.2));
            return max * mottle * (1 - 0.35 * Math.Min(1, Math.Max(0, y / 380)));
        }

        /// <summary>The field is the formula of the spec: round corners (a Chebyshev distance would not match at the corners), 200 units of fall-off varying by 12 %, a warped edge, MAX over the blocks, the core falling with y, never above that core.</summary>
        private static void Conforms()
        {
            TestAssert.Near(ScopeLayout.CloudFeather, 200f, "The fall-off is 200 units");
            TestAssert.Near(ScopeLayout.CloudWobble, 0.12f, "...varying by 12 %");
            TestAssert.Near(ShadeField.Reach, 260f, "Wobble and warp reach 200 x 1.12 + 2 x 18 = 260 units");
            TestAssert.Near(ShadeField.Profile(0f), 0f, "The fall-off is nothing at its end");
            TestAssert.Near(ShadeField.Profile(1f), 1f, "...and full at the block's own edge");
            TestAssert.Near(ShadeField.Profile(0.5f), 0.5f, "...and half way is half (the smoothstep and the line cross there)");
            float steepest = 0f;
            for (float u = 0f; u < 1f; u += 0.001f) steepest = Math.Max(steepest, (ShadeField.Profile(u + 0.001f) - ShadeField.Profile(u)) / 0.001f);
            TestAssert.That(steepest < 1.26f && steepest > 1.2f, "The steepest slope of the fall-off is 1.25, the smoothstep's 1.5 would read as an edge: " + steepest);
            foreach (BoardGeometry g in Boards())
            {
                BoardRect a = ShadeField.Area(g);
                int dark = 0;
                for (float y = a.Y - 8f; y < a.Bottom + 8f; y += 7f)
                    for (float x = a.X - 8f; x < a.Right + 8f; x += 7f)
                    {
                        float v = ShadeField.At(g, x, y);
                        TestAssert.That(Math.Abs(v - Reference(g, x, y)) < 0.002, "The field follows LAYOUT.md 2 at (" + x + ", " + y + "): " + v + " / " + Reference(g, x, y));
                        TestAssert.That(v <= ShadeField.Fall(y) + 0.00001f && v >= 0f, "Never darker than one core at (" + x + ", " + y + "): " + v);
                        if (v > 0.5f) dark++;
                    }
                TestAssert.That(dark > 500, "There is a core to look at: " + dark);
            }
            TestAssert.Near(ShadeField.Fall(0f), 1f, "The core is full at the top of the box");
            TestAssert.Near(ShadeField.Fall(380f), 0.65f, "...65 % at box y 380");
            TestAssert.Near(ShadeField.Fall(900f), 0.65f, "...and stays there");
            // The warped edge: a vertical edge of a block meanders (about +-29 units along y), it is never a ruled line: the field half way down the fade, walked along the block's left edge, is not constant.
            var tall = new BoardGeometry { Width = 916f, Hero = new BoardRect(400f, 6f, 272f, 600f) };
            float lo = float.MaxValue, hi = float.MinValue;
            for (float y = 100f; y < 500f; y += 4f) { float v = ShadeField.At(tall, 400f - 100f, y) / ShadeField.Fall(y); lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
            TestAssert.That(hi - lo > 0.1f, "A vertical edge is not a ruled line: the same distance from it reads " + lo + " to " + hi);
        }

        /// <summary>Where two blocks stand close the field is the stronger of the two, never their sum: HERO and PWR BUS are 12 apart and each reaches the gap, yet the gap is no darker than one cloud.</summary>
        private static void Overlaps()
        {
            var hero = new BoardGeometry { Width = 916f, Hero = new BoardRect(0f, 6f, 272f, 152f) };
            var pwr = new BoardGeometry { Width = 916f, Pwr = new BoardRect(284f, 6f, 180f, 72f) };
            var both = new BoardGeometry { Width = 916f, Hero = hero.Hero, Pwr = pwr.Pwr };
            for (float y = -20f; y <= 120f; y += 4f)
                for (float x = 150f; x <= 400f; x += 4f)
                    TestAssert.That(Math.Abs(ShadeField.At(both, x, y) - Math.Max(ShadeField.At(hero, x, y), ShadeField.At(pwr, x, y))) < 0.000001f, "Two blocks give the MAX of their clouds at (" + x + ", " + y + ")");
            float fromHero = ShadeField.At(hero, 278f, 40f), fromPwr = ShadeField.At(pwr, 278f, 40f);
            TestAssert.That(fromHero > 0.3f && fromPwr > 0.3f, "Both blocks reach the middle of the gap: " + fromHero + ", " + fromPwr);
            TestAssert.Near(ShadeField.At(both, 278f, 40f), Math.Max(fromHero, fromPwr), "...and the gap has the stronger one, not both");

            BoardGeometry g = Board(BoardLayout.Wide, ScopeLayout.Full);
            TestAssert.That(ShadeField.SameBlocks(g, Board(BoardLayout.Wide, ScopeLayout.Full)), "The same blocks are the same field");
            TestAssert.That(!ShadeField.SameBlocks(g, Board(BoardLayout.Wide, 150f)), "A page of another height is another field");
            TestAssert.That(!ShadeField.SameBlocks(g, Board(BoardLayout.Stack, ScopeLayout.Full)) && !ShadeField.SameBlocks(g, Board(BoardLayout.Wide, ScopeLayout.Full, false)) && !ShadeField.SameBlocks(g, default), "Another layout, a block gone, nothing: other fields");
        }

        /// <summary>The bake area holds every block and its whole cloud, on the texel grid, and the field is nothing along its rim, so the texture has no straight cut edge (200 units of padding would leave one: the wobble and the warp reach further).</summary>
        private static void Area()
        {
            TestAssert.That(!ShadeField.Area(new BoardGeometry()).Present, "No block, no field");
            foreach (BoardGeometry g in Boards())
            {
                BoardRect a = ShadeField.Area(g);
                const float cell = ScopeLayout.CloudCell;
                TestAssert.That(a.Present && a.X % cell == 0f && a.Y % cell == 0f && a.W % cell == 0f && a.H % cell == 0f, "The area is on the texel grid: " + a.X + ", " + a.Y + ", " + a.W + ", " + a.H);
                for (int i = 0; i < BoardGeometry.BlockCount; i++)
                {
                    BoardRect r = g.Block(i);
                    if (!r.Present) continue;
                    TestAssert.That(a.X <= r.X - ShadeField.Reach && a.Y <= r.Y - ShadeField.Reach && a.Right >= r.Right + ShadeField.Reach && a.Bottom >= r.Bottom + ShadeField.Reach, "The area holds block " + i + " and all its cloud");
                }
                float worst = 0f;
                for (float x = a.X + cell / 2f; x < a.Right; x += cell) worst = Math.Max(worst, Math.Max(ShadeField.At(g, x, a.Y + cell / 2f), ShadeField.At(g, x, a.Bottom - cell / 2f)));
                for (float y = a.Y + cell / 2f; y < a.Bottom; y += cell) worst = Math.Max(worst, Math.Max(ShadeField.At(g, a.X + cell / 2f, y), ShadeField.At(g, a.Right - cell / 2f, y)));
                TestAssert.That(worst < 0.002f, "The field is nothing along the rim of its bake (no cut edge): " + worst);
            }
        }

        /// <summary>The fast bake is the formula: every texel is round(At x 255) at the texel's centre (to one step: the sines are summed by the angle formula), row 0 the top; one scratch serves boards of every size, big then small then big.</summary>
        private static void Baked()
        {
            BoardGeometry[] boards = Boards();
            foreach (int index in new[] { 1, 4, 0, 5, 2 })
            {
                BoardGeometry g = boards[index];
                BoardRect a = ShadeField.Area(g);
                ShadeField.Texels(a, out int w, out int h);
                TestAssert.That(w * ScopeLayout.CloudCell == a.W && h * ScopeLayout.CloudCell == a.H && w > 100 && h > 60, "The bake is whole texels of 4 units: " + w + " x " + h);
                var bytes = new byte[w * h];
                ShadeField.Bake(g, a, bytes);
                int worst = 0, dark = 0;
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        int want = (int)(ShadeField.At(g, a.X + (i + 0.5f) * ScopeLayout.CloudCell, a.Y + (j + 0.5f) * ScopeLayout.CloudCell) * 255f + 0.5f);
                        worst = Math.Max(worst, Math.Abs(bytes[j * w + i] - want));
                        if (bytes[j * w + i] > 100) dark++;
                    }
                TestAssert.That(worst <= 1, "Board " + index + ": every texel of the bake is the field's (worst " + worst + ")");
                TestAssert.That(dark > 2000, "Board " + index + ": there is a core in it: " + dark);
            }
        }
    }
}
