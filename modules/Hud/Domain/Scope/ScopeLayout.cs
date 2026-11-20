using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;

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

    /// <summary>Which blocks of the board exist this tick, and the sizes that move: the panels' heights, the mission rows and the number of gauge lines.</summary>
    internal struct BoardBlocks
    {
        public BoardLayout Layout;
        /// <summary>WPN (the weapon) and CM (the dispensers).</summary>
        public bool Hero, Exp;
        /// <summary>The major stores group is explicitly disabled: WPN and CM are not drawn and WIDE collapses to the spine's width.</summary>
        public bool StoresHidden;
        /// <summary>SYS (the capacitor and the lamp grid) and INFO (clock, wind, sky) are drawn.</summary>
        public bool Sys, Info;
        /// <summary>The mission rows' height at the top of the spine (<see cref="ScopeLayout.MissionRowsH"/> while a mission runs), 0: none.</summary>
        public float MissionH;
        /// <summary>The three panels' current heights in board units, header included (animated by each slot's wipe), in the spine's fixed order RWR, FIRES, WING; 0: that panel is not drawn.</summary>
        public float RwrH, FiresH, WingH;

        public readonly float SlotH(int i) => i == 0 ? RwrH : i == 1 ? FiresH : i == 2 ? WingH : 0f;
        /// <summary>0: no gauge is live; else the lines the live gauges pack into, 1 or 2 (<see cref="GaugeRow.Lay"/>).</summary>
        public int GaugeLines;
    }

    /// <summary>The box (its origin is its top-left corner, <see cref="Width"/> x <see cref="Height"/>) and the rectangle of every block in it; 0 high: no block.</summary>
    internal struct BoardGeometry
    {
        public float Width, Height;
        /// <summary>The bar's four cells (ONE BAR), the mission rows and the gauge line.</summary>
        public BoardRect Wpn, Cm, Sys, Info, Mission, Gauges;
        /// <summary>The bar lines (WIDE: Bar0 alone; STACK: Bar0 = WPN | CM over Bar1 = SYS | INFO); no size: no present cell on that line.</summary>
        public BoardRect Bar0, Bar1;
        /// <summary>The x of the first present cell of Bar0 / Bar1: where that line's frame and brackets start.</summary>
        public float FrameLeft0, FrameLeft1;
        /// <summary>The page slots in slot order (no size: no such slot), and <see cref="Page"/>, the box round all of them (no size: no pages).</summary>
        public BoardRect Slot0, Slot1, Slot2, Page;

        /// <summary>The blocks, in the order of <see cref="Block"/>: the shade's cloud field (<see cref="ShadeField"/>) is the strongest of their fall-offs.</summary>
        public const int BlockCount = 8;

        public readonly BoardRect Slot(int i) => i == 0 ? Slot0 : i == 1 ? Slot1 : i == 2 ? Slot2 : default;

        public void SetSlot(int i, BoardRect r)
        {
            if (i == 0) Slot0 = r;
            else if (i == 1) Slot1 = r;
            else if (i == 2) Slot2 = r;
        }

        public readonly BoardRect Block(int i)
        {
            switch (i)
            {
                case 0: return Wpn;
                case 1: return Cm;
                case 2: return Sys;
                case 3: return Slot0;
                case 4: return Gauges;
                case 5: return Slot2;
                case 6: return Info;
                case 7: return Slot1;
                default: return default;
            }
        }
    }

    /// <summary>
    /// Pure layout of the TAC BOARD: in board units, 1 unit = 1 px at 1080p, scaled by the board unit (the screen's height over 1080, never under <see cref="MinScale"/>, times the player's Scale). The top band is ONE BAR of four
    /// fixed cells (WPN | CM | SYS | INFO) sharing one row grid (<see cref="ShelfY"/>, <see cref="RuleY"/>, <see cref="R1"/>..<see cref="R3"/>); the spine (mission rows, RWR, FIRES, WING, gauges) hangs under it in the
    /// right-hand 440 column, and SYS + INFO are exactly that column (SysX = WideW - WingW). Two arrangements: WIDE (one bar line) and STACK (two lines). Every number is in this one table. No UnityEngine.
    /// </summary>
    internal static class ScopeLayout
    {
        // ---- the anchor (the box's top-right corner, the HUD's one outer margin on both edges) and the smallest unit: below 1080p a 12 unit word is 10 px, never smaller.
        public const float Margin = HudZones.Margin, MinScale = 0.85f;

        // ---- the shade. ONE cloud field behind all the blocks (ShadeField): each block's fall-off is a softened smoothstep over CloudFeather units outside it (measured from a point warped by up to CloudWarp units,
        // so no edge is ruled), the fall-offs combine with MAX, the core is CloudAlpha at the top of the box and falls by CloudFall over CloudFallY units of box y; the fade distance varies by +-CloudWobble and the core is up to
        // CloudMottle lighter in places; baked one texel per CloudCell units. The band hangs from the top of the screen and ramps in over BandRamp units at its left.
        public const float CloudAlpha = 0.60f, CloudFeather = 200f, CloudFall = 0.35f, CloudFallY = 380f, CloudWobble = 0.12f, CloudMottle = 0.12f, CloudWarp = 18f, CloudCell = 4f;
        public const float BandAlpha = 0.55f, BandHeight = 130f, BandRamp = 280f, BandFadeTop = 60f;

        // ---- ONE BAR: the shared row grid, measured from the top of a bar line. Shelf words' middle (lamps, rail, captions), the glareshield hairline, then the three value rows.
        public const float BarH = 96f, ShelfY = 9f, RuleY = 19f, R1 = 35f, R2 = 59f, R3 = 82f;
        public const float CellGap = 14f, StackLineGap = 10f;

        // ---- WIDE: box 944 = WPN 280 + 14 + CM 196 + 14 + [SYS 226 + 14 + INFO 200], the bracket being the spine's 440 column. The cells sit at fixed slots (nothing slides when a neighbour is missing).
        public const float WideW = 944f, WingW = 440f;
        public const float WpnX = 0f, WpnW = 280f, CmX = 294f, CmW = 196f, SysX = 504f, SysW = 226f, InfoX = 744f, InfoW = 200f;
        /// <summary>Where the spine column starts in WIDE (SYS's x, WideW - WingW): STACK's line B is the WIDE x less this, from the inset.</summary>
        public const float Fold = SysX;

        // ---- STACK: box 448, the lines 440 wide at the inset StackX; line A is WPN | CM at their narrower STACK widths (WIDE's would overrun 440), line B is SYS | INFO folded (x - Fold).
        public const float StackX = 4f, StackInnerW = 440f, StackWpnW = 250f, StackCmX = 264f, StackCmW = 176f;

        /// <summary>The CM cell's width in either arrangement.</summary>
        public static float CmWOf(bool stack) => stack ? StackCmW : CmW;

        /// <summary>What hangs under the bar and between the panels; the gauge line hangs under the last panel.</summary>
        public const float PageGap = HudZones.Gutter;
        /// <summary>The mission rows (name and phase, then the timer or the score) at the top of the spine while a mission runs.</summary>
        public const float MissionRowsH = 50f;
        /// <summary>The gauge line: one line of 12, or two with a row pitch of 18; the gauges are <see cref="GaugeSpace"/> apart.</summary>
        public const float GaugeLine1 = 12f, GaugeLine2 = 30f, GaugePitch = 18f, GaugeSpace = 16f;

        // ---- the panels. The rectangle of a full page: the header is a caption shelf in its first 18 units, not a row. The painters keep the safe area of the old header band (Head) and are drawn Lift units higher, so their
        // first row is 20 units under the rectangle's top; Strip is the page strip under a full page's body.
        public const float Full = 290f;
        public const float Head = 28f, HeadCompact = 22f, Strip = 26f, ShelfRow = 18f;
        public const float WipeSeconds = 0.18f;

        /// <summary>The panel slots, one above the other in this order: RWR, FIRES, WING.</summary>
        public const int MaxSlots = 3, SlotRwr = 0, SlotFires = 1, SlotWing = 2;

        // ---- The panels' heights (2026-11-16, four panels): each takes what it needs. RWR its full scope, or its header line alone while nothing paints us; FIRES the SHOT plan, or a full page for STRIKE (its
        // tracks and SAT / CYBER blocks do not fold yet); WING a small plot and two full rows.
        public const float RwrClear = ShelfRow, ShotH = 150f, WingH = 168f;
        /// <summary>The board's room in board units from the box's top: the BOARD TOP zone's top (12) to the BOARD SPINE zone's bottom (666 reference px) at the default Scale 0.85.</summary>
        public const float SpineH = (666f - 12f) / 0.85f;

        /// <summary>The bar's height: one line WIDE; two lines and their gap STACK.</summary>
        public static float BarLinesH(bool stack) => stack ? BarH * 2f + StackLineGap : BarH;

        public static float RwrHeight(bool open) => open ? Full : RwrClear;

        public static float FiresHeight(ScopePage host) => host == ScopePage.Strike ? Full : ShotH;

        /// <summary>WING is the first to go when the bar (<paramref name="barH"/>, <see cref="BarLinesH"/>), the mission rows and the three panels would pass <see cref="SpineH"/> (a full RWR over a STRIKE): the Wing module's strip stands in for it (<c>IHudBoard.ShowsWing</c>).</summary>
        public static bool WingFits(float barH, float rwrH, float firesH, float missionH = 0f) =>
            barH + PageGap + (missionH > 0f ? missionH + PageGap : 0f) + rwrH + PageGap + (firesH > 0f ? firesH + PageGap : 0f) + WingH <= SpineH;

        /// <summary>
        /// The blocks of a board. The bar's cells at their fixed slots (WIDE one line; STACK WPN | CM over SYS | INFO), each line's frame starting at its first present cell. The spine hangs <see cref="PageGap"/> under the
        /// last bar line in the right-hand 440 column: the mission rows, then the panels in slot order (each as tall as its own height says and only while that is not 0), then the gauge line.
        /// </summary>
        public static BoardGeometry Compute(in BoardBlocks b)
        {
            bool stack = b.Layout == BoardLayout.Stack;
            bool hero = b.Hero && !b.StoresHidden, exp = b.Exp && !b.StoresHidden, sys = b.Sys, info = b.Info;
            float inset = stack ? StackX : 0f;
            float headW = stack ? StackInnerW : b.StoresHidden ? WingW : WideW;
            var g = new BoardGeometry { Width = inset * 2f + headW };
            float spineX = g.Width - inset - WingW;
            if (stack)
            {
                float lineB = BarH + StackLineGap;
                if (hero) g.Wpn = new BoardRect(StackX + WpnX, 0f, StackWpnW, BarH);
                if (exp) g.Cm = new BoardRect(StackX + StackCmX, 0f, StackCmW, BarH);
                if (sys) g.Sys = new BoardRect(StackX + SysX - Fold, lineB, SysW, BarH);
                if (info) g.Info = new BoardRect(StackX + InfoX - Fold, lineB, InfoW, BarH);
                if (hero || exp) { g.Bar0 = new BoardRect(StackX, 0f, StackInnerW, BarH); g.FrameLeft0 = hero ? g.Wpn.X : g.Cm.X; }
                if (sys || info) { g.Bar1 = new BoardRect(StackX, lineB, StackInnerW, BarH); g.FrameLeft1 = sys ? g.Sys.X : g.Info.X; }
            }
            else
            {
                float shift = g.Width - WideW;      // SYS and INFO are right-anchored: under StoresHidden the 440 box holds them alone
                if (hero) g.Wpn = new BoardRect(WpnX, 0f, WpnW, BarH);
                if (exp) g.Cm = new BoardRect(CmX, 0f, CmW, BarH);
                if (sys) g.Sys = new BoardRect(SysX + shift, 0f, SysW, BarH);
                if (info) g.Info = new BoardRect(InfoX + shift, 0f, InfoW, BarH);
                if (hero || exp || sys || info)
                {
                    g.Bar0 = new BoardRect(0f, 0f, g.Width, BarH);
                    g.FrameLeft0 = hero ? g.Wpn.X : exp ? g.Cm.X : sys ? g.Sys.X : g.Info.X;
                }
            }
            float y = g.Bar1.Present ? g.Bar1.Bottom + PageGap : g.Bar0.Present ? g.Bar0.Bottom + PageGap : 0f;
            if (b.MissionH > 0f) { g.Mission = new BoardRect(spineX, y, WingW, b.MissionH); y = g.Mission.Bottom + PageGap; }
            float pagesTop = y;
            for (int i = 0; i < MaxSlots; i++)
            {
                float h = b.SlotH(i);
                if (h <= 0f) continue;
                g.SetSlot(i, new BoardRect(spineX, y, WingW, h));
                y += h + PageGap;
            }
            if (y > pagesTop) g.Page = new BoardRect(spineX, pagesTop, WingW, y - PageGap - pagesTop);
            if (b.GaugeLines > 0) g.Gauges = new BoardRect(spineX, y, WingW, b.GaugeLines > 1 ? GaugeLine2 : GaugeLine1);

            if (g.Mission.Present) g.Height = g.Mission.Bottom;
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
            fromTop = Math.Min((Margin + offsetY) * baseUnit, Math.Max(0f, screenH - boxH * unit));
        }

        /// <summary>Keep all selected columns inside the viewport, preserving the requested size whenever it fits.</summary>
        public static float FitUnit(float requested, float pixel, float screenW, float screenH, float boxW, float boxH) =>
            Math.Max(pixel * 0.1f, Math.Min(requested, Math.Min((screenW - pixel * 2f) / boxW, (screenH - pixel * 2f) / boxH)));

        /// <summary>
        /// The box as a rectangle of the screen, centre-origin with y up (what <c>ThirdPersonHudLayout</c> works in): <paramref name="fromRight"/> and <paramref name="fromTop"/> are <see cref="Anchor"/>'s, the box
        /// <paramref name="boxW"/> x <paramref name="boxH"/> (all in the same units as the screen).
        /// </summary>
        public static RectF Box(float screenW, float screenH, float fromRight, float fromTop, float boxW, float boxH) =>
            new RectF(screenW * 0.5f - fromRight - boxW * 0.5f, screenH * 0.5f - fromTop - boxH * 0.5f, boxW, boxH);

        /// <summary>How far the painters' body is drawn above its own origin (the old header band less the shelf row): 4 compact, 10 full.</summary>
        public static float Lift(bool compact) => (compact ? HeadCompact : Head) - ShelfRow;

        /// <summary>The body of a panel <paramref name="pageH"/> high.</summary>
        public static float Body(float pageH, bool strip) => pageH + Lift(false) - (strip ? Strip : 0f);

        public static float SafeTop(bool compact) => (compact ? HeadCompact : Head) + 2f;
    }
}
