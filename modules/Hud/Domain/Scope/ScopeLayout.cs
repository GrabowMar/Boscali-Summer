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
        /// <summary>PWR BUS has its cost row (an energy weapon is fitted): 72 high, else 54 (<see cref="ScopeLayout.CostRow"/> less).</summary>
        public bool PwrCost;
        /// <summary>The caption shelves are off (Lean, Minimal): EXPENDABLES has no shelf row, so in WIDE it is <see cref="ScopeLayout.ExpShelfRow"/> shorter and its words move up by that much (the stores painter).</summary>
        public bool NoShelves;
        public bool Lamps;
        /// <summary>The page's current height in board units, header included (animated between <see cref="ScopeLayout.Compact"/> and <see cref="ScopeLayout.Full"/>); 0: no page.</summary>
        public float PageH;
        /// <summary>0: no gauge is live; else the lines the live gauges pack into, 1 or 2 (<see cref="GaugeRow.Lay"/>).</summary>
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

        // ---- LAYOUT.md 2: the shade. ONE cloud field behind all the blocks (ShadeField): each block's fall-off is a softened smoothstep over CloudFeather units outside it (measured from a point warped by up to CloudWarp units,
        // so no edge is ruled), the fall-offs combine with MAX, the core is CloudAlpha at the top of the box and falls by CloudFall over CloudFallY units of box y; the fade distance varies by +-CloudWobble and the core is up to
        // CloudMottle lighter in places; baked one texel per CloudCell units. The band hangs from the top of the screen and ramps in over BandRamp units at its left.
        public const float CloudAlpha = 0.60f, CloudFeather = 200f, CloudFall = 0.35f, CloudFallY = 380f, CloudWobble = 0.12f, CloudMottle = 0.12f, CloudWarp = 18f, CloudCell = 4f;
        public const float BandAlpha = 0.55f, BandHeight = 190f, BandRamp = 280f, BandFadeTop = 60f;

        // ---- LAYOUT.md 3 (WIDE): box 916 wide. HERO 0..272, gap 12, SAT 284..464, gap 12, WING 476..916.
        public const float WideW = 916f;
        /// <summary>The y of HERO and of the first SAT block (the top of their caption row); the lamp line is above, at 0.</summary>
        public const float BlockTop = 6f;
        public const float HeroX = 0f, HeroW = 272f, HeroH = 152f;
        /// <summary>PWR BUS and EXPENDABLES: x 284, 180 x 72 each, <see cref="BlockGap"/> apart (EXPENDABLES at y 88, or at y 6 with no PWR BUS); see <see cref="CostRow"/> and <see cref="ExpShelfRow"/> for when they are shorter.</summary>
        public const float SatX = 284f, SatW = 180f, SatH = 72f;
        /// <summary>PWR BUS without its cost row is <see cref="CostRow"/> shorter; EXPENDABLES without its shelf row is <see cref="ExpShelfRow"/> shorter (LAYOUT.md 3).</summary>
        public const float CostRow = 18f, ExpShelfRow = 19f;
        public const float WingX = 476f, WingW = 440f;
        public const float LampH = 12f;
        /// <summary>The lamp line sits at the top of the caption row of the blocks beside it (y 6); the page starts under it (y 24) whether or not the lamp line is there.</summary>
        public const float LampToPage = 6f, WingBodyY = BlockTop + LampH + LampToPage;
        /// <summary>Between blocks that stand beside or above each other (HERO and SAT 12 in WIDE; PWR BUS and EXPENDABLES, HERO and SAT in STACK).</summary>
        public const float BlockGap = 10f;
        /// <summary>What hangs under the page: the gauge line 8 under it, the ticker 8 under it or 6 under the gauge line.</summary>
        public const float PageGap = 8f, GaugeGap = 6f;
        /// <summary>The gauge line: one line of 12, or two with a row pitch of 18; the gauges are <see cref="GaugeSpace"/> apart. The ticker is one line of 12.</summary>
        public const float GaugeLine1 = 12f, GaugeLine2 = 30f, GaugePitch = 18f, GaugeSpace = 16f, TickerH = 12f;

        // ---- LAYOUT.md 4 (STACK): box 448, every block 440 wide at x 4 (HERO and the lamp line, page, gauges, ticker), the SAT blocks 216 wide in two halves.
        public const float StackW = 448f, StackX = 4f, StackInnerW = 440f;
        public const float StackHeroH = 108f, StackHalfW = 216f, StackHalfX = 228f;

        // ---- LAYOUT.md 3.5: the page. The rectangle of a full page and of the compact SYSTEMS strip (a full page's strip included): the header is a caption shelf in its first 18 units, not a row. The painters keep the
        // safe area of the old header band (Head, HeadCompact: their first row is under it) and are drawn Lift units higher, so their first row is 20 units under the rectangle's top; Strip is the page strip under a full page's body.
        public const float Full = 290f, Compact = 96f;
        public const float Head = 28f, HeadCompact = 22f, Strip = 26f, ShelfRow = 18f;
        public const float WipeSeconds = 0.18f;

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
                if (b.Pwr) g.Pwr = new BoardRect(SatX, BlockTop, SatW, b.PwrCost ? SatH : SatH - CostRow);
                if (b.Exp) g.Exp = new BoardRect(SatX, b.Pwr ? g.Pwr.Bottom + BlockGap : BlockTop, SatW, b.NoShelves ? SatH - ExpShelfRow : SatH);
                if (b.Lamps) g.Lamps = new BoardRect(WingX, BlockTop, WingW, LampH);
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

        /// <summary>
        /// The box as a rectangle of the screen, centre-origin with y up (what <c>ThirdPersonHudLayout</c> works in): <paramref name="fromRight"/> and <paramref name="fromTop"/> are <see cref="Anchor"/>'s, the box
        /// <paramref name="boxW"/> x <paramref name="boxH"/> (all in the same units as the screen).
        /// </summary>
        public static RectF Box(float screenW, float screenH, float fromRight, float fromTop, float boxW, float boxH) =>
            new RectF(screenW * 0.5f - fromRight - boxW * 0.5f, screenH * 0.5f - fromTop - boxH * 0.5f, boxW, boxH);

        /// <summary>How far the painters' body is drawn above its own origin (the old header band less the shelf row): 4 compact, 10 full.</summary>
        public static float Lift(bool compact) => (compact ? HeadCompact : Head) - ShelfRow;

        /// <summary>The page body as the painters see it (their own height, before the <see cref="Lift"/>): the page's height with the lift put back, less the strip under a full page's body.</summary>
        public static float Body(bool compact, bool strip) => (compact ? Compact : Full) + Lift(compact) - (strip && !compact ? Strip : 0f);

        public static float SafeTop(bool compact) => (compact ? HeadCompact : Head) + 2f;
    }
}
