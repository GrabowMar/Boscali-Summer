using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>A rectangle in box units: x to the right and y down from the board box's top-left corner. No size means the block is not there.</summary>
    internal struct BoardRect
    {
        public float X, Y, W, H;

        public BoardRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }

        public readonly float Right => X + W;
        public readonly float Bottom => Y + H;
        public readonly bool Present => W > 0f && H > 0f;
    }

    /// <summary>Which blocks of the board exist this tick, and the two sizes that move: the page's height and the number of gauge lines.</summary>
    internal struct BoardBlocks
    {
        public BoardLayout Layout;
        /// <summary>HERO (the weapon), PWR BUS (the airframe has a capacitor) and EXPENDABLES (it has dispensers).</summary>
        public bool Hero, Pwr, Exp;
        public bool Lamps;
        /// <summary>The page's current height in board units, header included (animated between <see cref="ScopeLayout.Compact"/> and <see cref="ScopeLayout.Full"/>); 0: no page.</summary>
        public float PageH;
        /// <summary>0: no gauge is live; else the lines the live gauges pack into, 1 or 2 (<see cref="ScopeLayout.GaugeLines"/>).</summary>
        public int GaugeLines;
        public bool Ticker;
    }

    /// <summary>The box (its origin is its top-left corner, <see cref="Width"/> x <see cref="Height"/>) and the rectangle of every block in it; 0 high: no block.</summary>
    internal struct BoardGeometry
    {
        public float Width, Height;
        public BoardRect Hero, Pwr, Exp, Lamps, Page, Gauges, Ticker;

        /// <summary>The blocks, in the order of <see cref="Block"/>: the shade's cloud field (<see cref="ShadeField"/>) is the strongest of their fall-offs.</summary>
        public const int BlockCount = 7;

        public readonly BoardRect Box => new BoardRect(0f, 0f, Width, Height);

        public readonly BoardRect Block(int i)
        {
            switch (i)
            {
                case 0: return Hero;
                case 1: return Pwr;
                case 2: return Exp;
                case 3: return Lamps;
                case 4: return Page;
                case 5: return Gauges;
                case 6: return Ticker;
                default: return default;
            }
        }

    }

    /// <summary>
    /// Pure layout of the TAC BOARD (LAYOUT.md of the AR visor design): in board units, 1 unit = 1 px at 1080p, scaled by the board unit (the screen's height over 1080, never under
    /// <see cref="MinScale"/>, times the player's Scale). Two arrangements: WIDE (three columns, HERO | SAT | WING) and STACK (the classic column). Every number is in this one table, each with the
    /// LAYOUT.md section it comes from, so a later change to the design is a change here and nowhere else. No UnityEngine.
    /// </summary>
    internal static class ScopeLayout
    {
        // ---- LAYOUT.md 0: the anchor (the box's top-right corner) and the smallest unit: below 1080p a 12 unit word is 10 px, never smaller.
        public const float Margin = 8f, MarginTop = 6f, MinScale = 0.85f;

        // ---- LAYOUT.md 2: the shade. ONE cloud field behind all the blocks (ShadeField): each block's fall-off is a smoothstep over CloudFeather units outside it, the fall-offs combine with MAX, the core is CloudAlpha at the top of
        // the box and falls by CloudFall over CloudFallY units of box y; the fade distance varies by +-CloudWobble and the core is up to CloudMottle lighter in places; baked one texel per CloudCell units. The band hangs from the top of the screen.
        public const float CloudAlpha = 0.60f, CloudFeather = 72f, CloudFall = 0.5f, CloudFallY = 380f, CloudWobble = 0.35f, CloudMottle = 0.12f, CloudCell = 4f;
        public const float BandAlpha = 0.55f, BandHeight = 190f, BandRamp = 160f, BandFadeTop = 60f;

        // ---- LAYOUT.md 3 (WIDE): box 916 wide. HERO 0..272, gap 12, SAT 284..464, gap 12, WING 476..916.
        public const float WideW = 916f;
        /// <summary>The y of HERO and of the first SAT block (the top of their caption row); the lamp line is above, at 0.</summary>
        public const float BlockTop = 6f;
        public const float HeroX = 0f, HeroW = 272f, HeroH = 152f;
        /// <summary>PWR BUS and EXPENDABLES: x 284, 180 x 72 each, <see cref="BlockGap"/> apart (EXPENDABLES at y 88, or at y 6 with no PWR BUS).</summary>
        public const float SatX = 284f, SatW = 180f, SatH = 72f;
        public const float WingX = 476f, WingW = 440f;
        public const float LampH = 12f;
        /// <summary>The page starts under the lamp line (y 18) whether or not the lamp line is there.</summary>
        public const float LampToPage = 6f, WingBodyY = LampH + LampToPage;
        /// <summary>Between blocks that stand beside or above each other (HERO and SAT 12 in WIDE; PWR BUS and EXPENDABLES, HERO and SAT in STACK).</summary>
        public const float BlockGap = 10f;
        /// <summary>What hangs under the page: the gauge line 8 under it, the ticker 8 under it or 6 under the gauge line.</summary>
        public const float PageGap = 8f, GaugeGap = 6f;
        /// <summary>The gauge line: one line of 12, or two with a row pitch of 18; the gauges are <see cref="GaugeSpace"/> apart. The ticker is one line of 12.</summary>
        public const float GaugeLine1 = 12f, GaugeLine2 = 30f, GaugePitch = 18f, GaugeSpace = 16f, TickerH = 12f;

        // ---- LAYOUT.md 4 (STACK): box 448, every block 440 wide at x 4 (HERO and the lamp line, page, gauges, ticker), the SAT blocks 216 wide in two halves.
        public const float StackW = 448f, StackX = 4f, StackInnerW = 440f;
        public const float StackHeroH = 108f, StackHalfW = 216f, StackHalfX = 228f;

        // ---- LAYOUT.md 3.5: the page. Full page and the compact SYSTEMS strip (header included), the header band of each and the strip under a full page's body.
        public const float Full = 300f, Compact = 100f;
        public const float Head = 28f, HeadCompact = 22f, Strip = 26f;
        public const float WipeSeconds = 0.16f;

        public static BoardGeometry Compute(in BoardBlocks b)
        {
            bool stack = b.Layout == BoardLayout.Stack;
            var g = new BoardGeometry { Width = stack ? StackW : WideW };
            float wingX = stack ? StackX : WingX, wingW = stack ? StackInnerW : WingW, y;
            if (stack)
            {
                y = BlockTop;
                if (b.Hero) { g.Hero = new BoardRect(StackX, y, StackInnerW, StackHeroH); y = g.Hero.Bottom + BlockGap; }
                if (b.Pwr || b.Exp)
                {
                    if (b.Pwr) g.Pwr = new BoardRect(StackX, y, StackHalfW, SatH);
                    if (b.Exp) g.Exp = new BoardRect(b.Pwr ? StackHalfX : StackX, y, StackHalfW, SatH);
                    y += SatH + BlockGap;
                }
                if (b.Lamps) { g.Lamps = new BoardRect(StackX, y, StackInnerW, LampH); y = g.Lamps.Bottom + LampToPage; }
            }
            else
            {
                if (b.Hero) g.Hero = new BoardRect(HeroX, BlockTop, HeroW, HeroH);
                if (b.Pwr) g.Pwr = new BoardRect(SatX, BlockTop, SatW, SatH);
                if (b.Exp) g.Exp = new BoardRect(SatX, b.Pwr ? BlockTop + SatH + BlockGap : BlockTop, SatW, SatH);
                if (b.Lamps) g.Lamps = new BoardRect(WingX, 0f, WingW, LampH);
                y = WingBodyY;
            }
            // The page never depends on what hangs under it.
            if (b.PageH > 0f) { g.Page = new BoardRect(wingX, y, wingW, b.PageH); y = g.Page.Bottom + PageGap; }
            if (b.GaugeLines > 0) { g.Gauges = new BoardRect(wingX, y, wingW, b.GaugeLines > 1 ? GaugeLine2 : GaugeLine1); y = g.Gauges.Bottom + GaugeGap; }
            if (b.Ticker) g.Ticker = new BoardRect(wingX, y, wingW, TickerH);

            for (int i = 0; i < BoardGeometry.BlockCount; i++)
            {
                BoardRect r = g.Block(i);
                if (r.Present && r.Bottom > g.Height) g.Height = r.Bottom;
            }
            return g;
        }

        /// <summary>The gauge line's width is shared by its gauges in order, <see cref="GaugeSpace"/> apart, none split: how many lines the <paramref name="widths"/> take, 0 for none. Never more than 2 (the gauges drop their sub-words instead).</summary>
        public static int GaugeLines(ReadOnlySpan<float> widths)
        {
            if (widths.Length == 0) return 0;
            int lines = 1;
            float x = 0f;
            for (int i = 0; i < widths.Length; i++)
            {
                if (x > 0f && x + GaugeSpace + widths[i] > WingW) { lines++; x = 0f; }
                x += (x > 0f ? GaugeSpace : 0f) + widths[i];
            }
            return Math.Min(lines, 2);
        }

        /// <summary>
        /// Where the box's top-right corner goes, measured in from the screen's top-right corner in the units of <paramref name="baseUnit"/> (the board unit without the player's Scale): the margins,
        /// moved left by <paramref name="offsetX"/> and down by <paramref name="offsetY"/> (also in base units), then held so the whole box (<paramref name="boxW"/> x <paramref name="boxH"/> board units
        /// of <paramref name="unit"/> each, the unit with the Scale) stays on a screen of <paramref name="screenW"/> x <paramref name="screenH"/>.
        /// </summary>
        public static void Anchor(float screenW, float screenH, float baseUnit, float unit, float offsetX, float offsetY, float boxW, float boxH, out float fromRight, out float fromTop)
        {
            fromRight = Math.Min((Margin + offsetX) * baseUnit, Math.Max(0f, screenW - boxW * unit));
            fromTop = Math.Min((MarginTop + offsetY) * baseUnit, Math.Max(0f, screenH - boxH * unit));
        }

        /// <summary>The page's body height: its height less the strip under a full page's body.</summary>
        public static float Body(bool compact, bool strip) => (compact ? Compact : Full) - (strip && !compact ? Strip : 0f);

        public static float SafeTop(bool compact) => (compact ? HeadCompact : Head) + 2f;
    }
}
