using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The TAC BOARD's blocks (WIDE and STACK), anchor, wipe, lamp rule and gauge row.</summary>
    internal static class ScopeLayoutTests
    {
        public static void Run()
        {
            Layout();
            ManualColumns();
            Wipe();
            Lamps();
            Gauges();
        }

        /// <summary>The page grid: at most two across and two down, slot order left to right then down, a row that is not full against the right edge, SYSTEMS above it all.</summary>
        private static void ManualColumns()
        {
            TestAssert.That(ScopeLayout.Columns(BoardLayout.Wide, 1) == 1 && ScopeLayout.Columns(BoardLayout.Wide, 2) == 2 && ScopeLayout.Columns(BoardLayout.Wide, 3) == 2 && ScopeLayout.Columns(BoardLayout.Wide, 4) == 2,
                "WIDE fills a row of two first");
            TestAssert.That(ScopeLayout.Columns(BoardLayout.Stack, 1) == 1 && ScopeLayout.Columns(BoardLayout.Stack, 2) == 1 && ScopeLayout.Columns(BoardLayout.Stack, 3) == 2 && ScopeLayout.Columns(BoardLayout.Stack, 4) == 2,
                "STACK fills a column of two first");

            // WIDE with the stores: the box keeps its 916; one page hangs in the spine under SYSTEMS, a row of two clears the stores (SAT foot 160 + 10).
            BoardGeometry w1 = Grid(BoardLayout.Wide, 1);
            TestAssert.Near(w1.Width, ScopeLayout.WideW, "WIDE one page: 916 wide");
            Same(w1.Status, 476f, 24f, 440f, 96f, "SYSTEMS under the lamp line");
            Same(w1.Slot0, 476f, 128f, 440f, 290f, "One page hangs 8 under SYSTEMS");
            BoardGeometry w4 = Grid(BoardLayout.Wide, 4);
            TestAssert.Near(w4.Width, ScopeLayout.WideW, "WIDE four pages: still 916 wide");
            Same(w4.Status, 476f, 24f, 440f, 96f, "SYSTEMS does not move with the pages");
            Same(w4.Slot0, 24f, 170f, 440f, 290f, "Page 1 top left, under the stores");
            Same(w4.Slot1, 476f, 170f, 440f, 290f, "Page 2 top right, in the spine");
            Same(w4.Slot2, 24f, 468f, 440f, 290f, "Page 3 bottom left");
            Same(w4.Slot3, 476f, 468f, 440f, 290f, "Page 4 bottom right");
            Same(w4.Page, 24f, 170f, 892f, 588f, "The page box holds the grid");
            Same(w4.Gauges, 476f, 766f, 440f, 30f, "The gauge line hangs 8 under the last row, in the spine");
            BoardGeometry w3 = Grid(BoardLayout.Wide, 3);
            Same(w3.Slot2, 476f, 468f, 440f, 290f, "A third page sits under the spine, not under the empty corner");
            TestAssert.That(!w3.Slot3.Present, "No fourth slot for three pages");

            // STACK: one column of two, then a second column to its left; the stack itself stays the right-hand column.
            BoardGeometry s2 = Grid(BoardLayout.Stack, 2);
            TestAssert.Near(s2.Width, ScopeLayout.StackW, "STACK two pages: still one column");
            Same(s2.Status, 4f, 224f, 440f, 96f, "STACK: SYSTEMS under the lamp line");
            Same(s2.Slot0, 4f, 328f, 440f, 290f, "STACK page 1 under SYSTEMS");
            Same(s2.Slot1, 4f, 626f, 440f, 290f, "STACK page 2 under page 1");
            BoardGeometry s4 = Grid(BoardLayout.Stack, 4);
            TestAssert.Near(s4.Width, 900f, "STACK four pages: two columns and the stack's insets");
            Same(s4.Hero, 456f, 6f, 440f, 108f, "STACK: the stack is the right-hand column");
            Same(s4.Slot0, 4f, 328f, 440f, 290f, "STACK page 1 top left");
            Same(s4.Slot1, 456f, 328f, 440f, 290f, "STACK page 2 top right");
            Same(s4.Slot3, 456f, 626f, 440f, 290f, "STACK page 4 bottom right");

            foreach (BoardLayout layout in new[] { BoardLayout.Wide, BoardLayout.Stack })
                for (int count = 1; count <= ScopeLayout.MaxSlots; count++)
                {
                    BoardGeometry g = Grid(layout, count);
                    string tag = layout + " / " + count;
                    int cols = ScopeLayout.Columns(layout, count);
                    float spine = g.Width - (layout == BoardLayout.Stack ? ScopeLayout.StackX : 0f) - ScopeLayout.WingW;
                    float minY = float.MaxValue;
                    for (int i = 0; i < ScopeLayout.MaxSlots; i++)
                    {
                        BoardRect r = g.Slot(i);
                        TestAssert.That(r.Present == (i < count), "Exactly the chosen slots are laid out: " + tag + " / " + i);
                        if (!r.Present) continue;
                        minY = Math.Min(minY, r.Y);
                        TestAssert.That(r.W == ScopeLayout.WingW && r.H == ScopeLayout.Full && r.X >= 0f && r.Right <= g.Width && r.Bottom <= g.Height, "Every page is a full page inside the box: " + tag + " / " + i);
                        TestAssert.That(r.X == spine || r.X == spine - ScopeLayout.WingW - ScopeLayout.PageGapX, "A page is in the spine or the column left of it: " + tag + " / " + i);
                        if (i > 0 && i % cols != 0) TestAssert.That(r.X > g.Slot(i - 1).X && r.Y == g.Slot(i - 1).Y, "Slots run left to right along a row: " + tag + " / " + i);
                    }
                    TestAssert.That(g.Status.Bottom + ScopeLayout.PageGap <= minY, "SYSTEMS sits above every page: " + tag);
                    TestAssert.Near(g.Slot(count - 1).X, spine, "The last page is in the spine, over the gauge line: " + tag);
                    BoardBlocks hidden = GridBlocks(layout, count);
                    hidden.StoresHidden = true;
                    BoardGeometry withoutStores = ScopeLayout.Compute(hidden);
                    TestAssert.That(!withoutStores.Hero.Present && !withoutStores.Pwr.Present && !withoutStores.Exp.Present, "The major stores switch hides every stores block");
                    if (layout == BoardLayout.Wide)
                        TestAssert.Near(withoutStores.Width, cols * ScopeLayout.WingW + (cols - 1) * ScopeLayout.PageGapX, "The stores group returns its reserved width: " + tag);
                    foreach (var viewport in new[] { (w: 1280f, h: 720f), (w: 1440f, h: 1080f), (w: 1920f, h: 1080f), (w: 3440f, h: 1440f) })
                        foreach (float requested in new[] { 0.6f, 0.85f, 1.4f })
                        {
                            float fitted = ScopeLayout.FitUnit(requested, 1f, viewport.w, viewport.h, g.Width, g.Height);
                            TestAssert.That(fitted <= requested && g.Width * fitted <= viewport.w - 1f && g.Height * fitted <= viewport.h - 1f,
                                "All requested pages fit without leaving the viewport: " + tag + " / " + viewport.w + " / " + requested);
                        }
                }
            BoardGeometry wide4 = Grid(BoardLayout.Wide, 4);
            TestAssert.Near(ScopeLayout.FitUnit(0.85f, 1f, 1920f, 1080f, wide4.Width, wide4.Height), 0.85f, "Four pages fit 1080p at the default size: the size is the pilot's");
        }

        private static BoardBlocks GridBlocks(BoardLayout layout, int count)
        {
            BoardBlocks b = All(layout, ScopeLayout.Full, 2);
            b.PageCount = count;
            b.StatusH = ScopeLayout.Compact;
            return b;
        }

        private static BoardGeometry Grid(BoardLayout layout, int count) => ScopeLayout.Compute(GridBlocks(layout, count));

        private static BoardBlocks All(BoardLayout layout, float pageH, int gaugeLines = 1, bool ticker = true) => new BoardBlocks
        {
            Layout = layout, Hero = true, Pwr = true, PwrCost = true, Exp = true, Lamps = true, PageH = pageH, GaugeLines = gaugeLines, Ticker = ticker,
        };

        private static void Same(BoardRect r, float x, float y, float w, float h, string what)
        {
            TestAssert.That(r.Present && Math.Abs(r.X - x) < 0.001f && Math.Abs(r.Y - y) < 0.001f && Math.Abs(r.W - w) < 0.001f && Math.Abs(r.H - h) < 0.001f,
                what + ": (" + r.X + ", " + r.Y + ", " + r.W + ", " + r.H + ") should be (" + x + ", " + y + ", " + w + ", " + h + ")");
        }

        private static void Layout()
        {
            BoardGeometry empty = ScopeLayout.Compute(new BoardBlocks());
            TestAssert.Near(empty.Height, 0f, "A board with no block takes no room");
            for (int i = 0; i < BoardGeometry.BlockCount; i++) TestAssert.That(!empty.Block(i).Present, "No block, no rectangle: " + i);

            // WIDE (LAYOUT.md 3): box 916, cruise 160 (the SAT column's foot), live 370 (a full page with two gauge lines and the ticker under it).
            BoardGeometry cruise = ScopeLayout.Compute(All(BoardLayout.Wide, ScopeLayout.Compact));
            TestAssert.Near(cruise.Width, 916f, "WIDE is 916 wide");
            TestAssert.Near(cruise.Height, 160f, "WIDE in cruise is 160 tall");
            Same(cruise.Hero, 0f, 6f, 272f, 152f, "HERO");
            Same(cruise.Pwr, 284f, 6f, 180f, 72f, "PWR BUS");
            Same(cruise.Exp, 284f, 88f, 180f, 72f, "EXPENDABLES");
            Same(cruise.Lamps, 476f, 6f, 440f, 12f, "WIDE lamp line (its centre, y 12, is the centre of the shelf captions beside it)");
            Same(cruise.Page, 476f, 24f, 440f, 96f, "WIDE compact page");
            Same(cruise.Gauges, 476f, 128f, 440f, 12f, "WIDE gauge line (one line)");
            Same(cruise.Ticker, 476f, 146f, 440f, 12f, "WIDE ticker");
            BoardGeometry live = ScopeLayout.Compute(All(BoardLayout.Wide, ScopeLayout.Full, 2));
            TestAssert.Near(live.Height, 370f, "WIDE with a live page, two gauge lines and a notice is 370 tall");
            Same(live.Page, 476f, 24f, 440f, 290f, "WIDE live page");
            Same(live.Gauges, 476f, 322f, 440f, 30f, "WIDE gauge line (two lines) hangs 8 under the page");
            Same(live.Ticker, 476f, 358f, 440f, 12f, "WIDE ticker hangs 6 under the gauge line");
            Same(live.Hero, 0f, 6f, 272f, 152f, "HERO does not move with the page");
            BoardBlocks rwr = All(BoardLayout.Wide, ScopeLayout.Full, 2);
            rwr.StatusH = ScopeLayout.Compact;
            BoardGeometry warned = ScopeLayout.Compute(rwr);
            Same(warned.Status, 476f, 24f, 440f, 96f, "SYSTEMS takes the place under the lamp line");
            Same(warned.Page, 476f, 128f, 440f, 290f, "The page hangs 8 under SYSTEMS");
            Same(warned.Gauges, 476f, 426f, 440f, 30f, "the gauge line hangs 8 under the page");

            // STACK (LAYOUT.md 4): box 448, cruise 358, live 570.
            BoardGeometry sc = ScopeLayout.Compute(All(BoardLayout.Stack, ScopeLayout.Compact));
            TestAssert.Near(sc.Width, 448f, "STACK is 448 wide");
            TestAssert.Near(sc.Height, 358f, "STACK in cruise is 358 tall");
            Same(sc.Hero, 4f, 6f, 440f, 108f, "STACK HERO");
            Same(sc.Pwr, 4f, 124f, 216f, 72f, "STACK PWR BUS (left half)");
            Same(sc.Exp, 228f, 124f, 216f, 72f, "STACK EXPENDABLES (right half)");
            Same(sc.Lamps, 4f, 206f, 440f, 12f, "STACK lamp line");
            Same(sc.Page, 4f, 224f, 440f, 96f, "STACK compact page");
            Same(sc.Gauges, 4f, 328f, 440f, 12f, "STACK gauge line");
            Same(sc.Ticker, 4f, 346f, 440f, 12f, "STACK ticker");
            BoardGeometry sl = ScopeLayout.Compute(All(BoardLayout.Stack, ScopeLayout.Full, 2));
            TestAssert.Near(sl.Height, 570f, "STACK with a live page is 570 tall");
            Same(sl.Gauges, 4f, 522f, 440f, 30f, "STACK live gauge line 522..552");
            Same(sl.Ticker, 4f, 558f, 440f, 12f, "STACK live ticker 558..570");

            // The page never moves when the gauge line or the ticker come and go.
            foreach (BoardLayout layout in new[] { BoardLayout.Wide, BoardLayout.Stack })
                foreach (float pageH in new[] { ScopeLayout.Compact, ScopeLayout.Full, 187f })
                {
                    BoardRect page = ScopeLayout.Compute(All(layout, pageH, 0, false)).Page;
                    foreach (int lines in new[] { 0, 1, 2 })
                        foreach (bool ticker in new[] { false, true })
                        {
                            BoardRect p = ScopeLayout.Compute(All(layout, pageH, lines, ticker)).Page;
                            TestAssert.That(p.X == page.X && p.Y == page.Y && p.W == page.W && p.H == page.H, layout + " " + pageH + ": the page is where it is whatever hangs under it (" + lines + " gauge lines, ticker " + ticker + ")");
                        }
                }
            BoardGeometry noGauge = ScopeLayout.Compute(All(BoardLayout.Wide, ScopeLayout.Full, 0));
            Same(noGauge.Ticker, 476f, 322f, 440f, 12f, "With no gauge line the ticker hangs 8 under the page");

            // A block that is not there gives its room back (LAYOUT.md 3 and 4).
            BoardBlocks b = All(BoardLayout.Wide, ScopeLayout.Compact);
            b.Pwr = false;
            Same(ScopeLayout.Compute(b).Exp, 284f, 6f, 180f, 72f, "WIDE: with no PWR BUS, EXPENDABLES moves up to y 6");
            TestAssert.Near(ScopeLayout.Compute(b).Height, 158f, "...and the box is as tall as HERO then");
            b.Hero = false;
            Same(ScopeLayout.Compute(b).Exp, 284f, 6f, 180f, 72f, "WIDE: no HERO changes nothing else");
            TestAssert.Near(ScopeLayout.Compute(b).Height, 158f, "...and the box is as tall as the WING then (ticker 146..158)");
            b = All(BoardLayout.Wide, ScopeLayout.Compact);
            b.Lamps = false;
            Same(ScopeLayout.Compute(b).Page, 476f, 24f, 440f, 96f, "WIDE: with no lamp line the page keeps y 24");
            b.PageH = 0f;
            Same(ScopeLayout.Compute(b).Gauges, 476f, 24f, 440f, 12f, "WIDE: with no page the first block under the lamp line starts at y 24");
            b.GaugeLines = 0;
            Same(ScopeLayout.Compute(b).Ticker, 476f, 24f, 440f, 12f, "WIDE: with no page and no gauge line the ticker starts at y 24");
            b = All(BoardLayout.Stack, ScopeLayout.Compact);
            b.Hero = false;
            Same(ScopeLayout.Compute(b).Pwr, 4f, 6f, 216f, 72f, "STACK: with no HERO the SAT row starts at y 6");
            b.Pwr = false;
            Same(ScopeLayout.Compute(b).Exp, 4f, 6f, 216f, 72f, "STACK: with no PWR BUS, EXPENDABLES takes its half");
            b.Exp = false;
            Same(ScopeLayout.Compute(b).Lamps, 4f, 6f, 440f, 12f, "STACK: with nothing above it the lamp line starts at y 6");
            b.Lamps = false;
            Same(ScopeLayout.Compute(b).Page, 4f, 6f, 440f, 96f, "STACK: with nothing above it the page starts at y 6");
            b = All(BoardLayout.Stack, ScopeLayout.Compact);
            b.Lamps = false;
            Same(ScopeLayout.Compute(b).Page, 4f, 206f, 440f, 96f, "STACK: with no lamp line the page is 10 under the SAT row");
            TestAssert.Near(ScopeLayout.Compute(new BoardBlocks { Layout = BoardLayout.Wide, Lamps = true }).Height, 18f, "The lamp line alone is a box 18 tall: its 12, under the 6 of the caption row of the blocks beside it");

            // Every combination: nothing negative, nothing outside the box, the box is as tall as the lowest block, no two blocks touch (the tightest gap is 6, lamp line to page).
            int boards = 0;
            foreach (BoardLayout layout in new[] { BoardLayout.Wide, BoardLayout.Stack })
                for (int mask = 0; mask < 64; mask++)
                    foreach (float pageH in new[] { 0f, ScopeLayout.Compact, 160f, ScopeLayout.Full })
                        foreach (int lines in new[] { 0, 1, 2 })
                            for (int pages = 1; pages <= ScopeLayout.MaxSlots; pages++)
                        {
                            var blocks = new BoardBlocks
                            {
                                Layout = layout, Hero = (mask & 1) != 0, Pwr = (mask & 2) != 0, Exp = (mask & 4) != 0, Lamps = (mask & 8) != 0, Ticker = (mask & 16) != 0, StatusH = (mask & 32) != 0 ? ScopeLayout.Compact : 0f,
                                PageH = pageH, GaugeLines = lines, PageCount = pages,
                            };
                            BoardGeometry g = ScopeLayout.Compute(blocks);
                            string tag = layout + " " + mask + " " + pageH + " " + lines + " " + pages;
                            float lowest = 0f;
                            for (int i = 0; i < BoardGeometry.BlockCount; i++)
                            {
                                BoardRect r = g.Block(i);
                                if (!r.Present) { TestAssert.That(r.W == 0f && r.H == 0f, "An absent block has no size: " + tag + " " + i); continue; }
                                TestAssert.That(r.X >= 0f && r.Y >= 0f && r.W > 0f && r.H > 0f, "No block is negative: " + tag + " " + i);
                                TestAssert.That(r.Right <= g.Width + 0.001f && r.Bottom <= g.Height + 0.001f, "No block leaves the box: " + tag + " " + i);
                                lowest = Math.Max(lowest, r.Bottom);
                                for (int k = 0; k < i; k++)
                                {
                                    BoardRect o = g.Block(k);
                                    if (!o.Present) continue;
                                    float gap = Math.Max(Math.Max(o.X - r.Right, r.X - o.Right), Math.Max(o.Y - r.Bottom, r.Y - o.Bottom));
                                    TestAssert.That(gap >= 6f, "No two blocks touch (gap " + gap + "): " + tag + " " + k + " " + i);
                                }
                            }
                            TestAssert.Near(g.Height, lowest, "The box ends at its lowest block: " + tag);
                            boards++;
                        }
            TestAssert.That(boards == 2 * 64 * 4 * 3 * ScopeLayout.MaxSlots, "Every combination was covered: " + boards);

            TestAssert.Near(ScopeLayout.Body(true, false), 100f, "The compact body is the whole strip");
            TestAssert.Near(ScopeLayout.Body(false, false), 300f, "A full page body is 300");
            TestAssert.Near(ScopeLayout.Body(false, true), 274f, "A page strip takes 26 off a full page");
            TestAssert.Near(ScopeLayout.Body(true, true), 100f, "The compact strip never has a page strip");
            TestAssert.Near(ScopeLayout.Lift(true), 4f, "The compact body is drawn 4 above its origin (the old header allowance 22 less the shelf row 18)");
            TestAssert.Near(ScopeLayout.Lift(false), 10f, "A full page's body is drawn 10 above its origin");
            TestAssert.Near(ScopeLayout.Body(true, false) - ScopeLayout.Lift(true), ScopeLayout.Compact, "The compact body less its lift is the compact page");
            TestAssert.Near(ScopeLayout.Body(false, false) - ScopeLayout.Lift(false), ScopeLayout.Full, "A full body less its lift is the full page");
            TestAssert.Near(ScopeLayout.Body(false, true) - ScopeLayout.Lift(false) + ScopeLayout.Strip, ScopeLayout.Full, "...and a page strip is under the body, inside the page's height");
            TestAssert.Near(ScopeLayout.WingBodyY, 24f, "The page starts 24 down in WIDE");


            // The box's corner: 8 in from the right and 6 down at offset 0; the offsets move it left and down; the whole box stays on the screen.
            ScopeLayout.Anchor(1920f, 1080f, 1f, 1f, 0f, 0f, 916f, 160f, out float right, out float top);
            TestAssert.That(right == 8f && top == 6f, "The anchor is 8 from the right edge and 6 from the top: " + right + ", " + top);
            ScopeLayout.Anchor(1920f, 1080f, 1f, 0.7f, 300f, 120f, 916f, 160f, out right, out top);
            TestAssert.That(right == 308f && top == 126f, "The offsets move it left and down in base units, whatever the Scale: " + right + ", " + top);
            ScopeLayout.Anchor(1920f, 1080f, 2f, 2f, 0f, 0f, 916f, 160f, out right, out top);
            TestAssert.That(right == 16f && top == 12f, "Base units scale the margins: " + right + ", " + top);
            ScopeLayout.Anchor(1920f, 1080f, 1f, 1.4f, 1200f, 600f, 916f, 374f, out right, out top);
            TestAssert.Near(right, 1920f - 916f * 1.4f, "Moved too far left, the box stops at the left edge");
            TestAssert.Near(top, 1080f - 374f * 1.4f, "Moved too far down, the box stops at the bottom edge");
            ScopeLayout.Anchor(800f, 600f, 1f, 1f, 0f, 0f, 916f, 700f, out right, out top);
            TestAssert.That(right == 0f && top == 0f, "A box bigger than the screen sits in its corner: " + right + ", " + top);
        }

        private static void Wipe()
        {
            var t = new ScopeTransition();
            t.Update(1, ScopeLayout.Compact, 0f);
            TestAssert.That(t.Height == ScopeLayout.Compact && !t.Running && t.Wipe >= 1f, "The first page snaps: no wipe at boot");
            t.Update(1, ScopeLayout.Compact, 1f);
            TestAssert.That(!t.Running, "A steady page stays still");
            t.Update(2, ScopeLayout.Full, 2f);
            TestAssert.That(t.Running && t.Wipe < 0.01f && t.Height == ScopeLayout.Compact, "A page change starts the wipe where the old height was");
            t.Update(2, ScopeLayout.Full, 2.08f);
            TestAssert.That(t.Wipe > 0.4f && t.Wipe < 0.6f && t.Height > ScopeLayout.Compact && t.Height < ScopeLayout.Full, "Half way through the wipe the height is between");
            t.Update(2, ScopeLayout.Full, 2.2f);
            TestAssert.That(!t.Running && t.Height == ScopeLayout.Full && t.Wipe >= 1f, "The wipe ends in 180 ms at the new height");
            t.Update(3, ScopeLayout.Compact, 3f);
            t.Update(3, ScopeLayout.Compact, 3.05f);
            float mid = t.Height;
            t.Update(4, ScopeLayout.Full, 3.06f);
            TestAssert.That(t.Height == mid, "A change mid-wipe eases from where the scope is, never jumps");
            t.Reset();
            TestAssert.That(t.Height == 0f && !t.Running, "Reset forgets the page");
            TestAssert.Near(ScopeLayout.WipeSeconds, 0.18f, "A block wipes in over 180 ms (LAYOUT.md 10)");
            t.Update(1, ScopeLayout.Compact, 0f);
            t.Update(2, ScopeLayout.Full, 5f);
            t.Update(2, ScopeLayout.Full, 5.17f);
            TestAssert.That(t.Running && t.Wipe < 1f, "Still wiping 170 ms in");
            t.Update(2, ScopeLayout.Full, 5.19f);
            TestAssert.That(!t.Running && t.Wipe >= 1f, "Done 190 ms in");
        }

        private static void Lamps()
        {
            var c = new LampChange();
            TestAssert.That(!c.Recent(0, LampState.On, 10f), "The first reading is a baseline, not a change");
            TestAssert.That(!c.Recent(0, LampState.On, 11f), "A steady lamp is not recent");
            TestAssert.That(c.Recent(0, LampState.Dark, 12f), "A lamp that just went dark is recent: the change is seen once");
            TestAssert.That(c.Recent(0, LampState.Dark, 14f), "...for 2.5 seconds");
            TestAssert.That(!c.Recent(0, LampState.Dark, 14.6f), "...then it is just dark");
            TestAssert.That(c.Recent(0, LampState.Caution, 15f) && c.Recent(0, LampState.Warning, 15.1f), "Every change restarts the window");
            c.Forget(0);
            TestAssert.That(!c.Recent(0, LampState.On, 16f), "A lamp that left the airframe is a baseline again when it returns");
            TestAssert.That(!c.Recent(99, LampState.On, 0f), "An out-of-range lamp is never recent");
            c.Reset();
            TestAssert.That(!c.Recent(0, LampState.Dark, 20f), "Reset re-baselines every lamp");
            LampLines();
        }

        /// <summary>The lamp line's packing (LAYOUT.md 3.4): lamp widths, the Full level's slots, the packed lit lamps of Lean and Minimal, a line of threat lamps only.</summary>
        private static void LampLines()
        {
            TestAssert.Near(LampLine.Width(4), 39f, "GEAR: the 7 of the marker and 31.2 of four bold letters rounded up to 32");
            TestAssert.Near(LampLine.Width(2), 23f, "FA: 7 + 15.6 rounded up");
            TestAssert.Near(LampLine.Width(5), 46f, "MSL 1: 7 + 39");
            TestAssert.Near(LampLine.Width(0), 7f, "No word: the marker alone");

            // Full: GEAR FA HOVR RDR NVG SAFE ENG | LOCK SPKE MSL, every lamp in its slot, 10 apart; the right group ends at the line's end.
            float[] system = { 39f, 23f, 39f, 31f, 31f, 39f, 31f }, threat = { 39f, 39f, 31f };
            var sx = new float[7];
            var tx = new float[3];
            LampLine.Place(system, threat, true, 440f, sx, tx);
            TestAssert.That(sx[0] == 0f && sx[1] == 49f && sx[6] == 262f, "The system lamps start at 0 and are 10 apart: " + sx[1] + " " + sx[6]);
            TestAssert.That(tx[0] == 311f && tx[1] == 360f && tx[2] + threat[2] == 440f, "The threat lamps end at 440, 10 apart: " + tx[0] + " " + tx[2]);
            TestAssert.That(tx[0] - (sx[6] + system[6]) >= LampLine.GroupGap, "The groups are at least 16 apart: " + (tx[0] - (sx[6] + system[6])));

            // A long word (MSL 1) squeezes the gaps, never under 4, until the groups are 16 apart.
            float[] msl1 = { 39f, 39f, 46f };
            LampLine.Place(system, msl1, true, 440f, sx, tx);
            float gap = sx[1] - system[0];
            TestAssert.That(gap < 10f && gap >= LampLine.MinGap, "MSL 1 narrows the gaps: " + gap);
            TestAssert.Near(tx[0] - (sx[6] + system[6]), LampLine.GroupGap, "...so that the groups are exactly 16 apart");
            TestAssert.Near(tx[2] + msl1[2], 440f, "...and the right group still ends at 440");
            float[] wide = { 80f, 80f, 80f, 80f, 80f, 80f, 80f };
            LampLine.Place(wide, threat, true, 440f, sx, tx);
            TestAssert.Near(sx[1] - wide[0], LampLine.MinGap, "The gaps never go under 4");

            // Lean and Minimal: only the lit lamps, packed 14 apart; the right group ends at 440 however many there are.
            float[] lit = { 39f, 23f, 31f, 39f };
            LampLine.Place(lit, new float[] { 39f, 46f }, false, 440f, sx, tx);
            TestAssert.That(sx[0] == 0f && sx[1] == 53f && sx[2] == 90f && sx[3] == 135f, "Lit lamps pack from 0, 14 apart: " + sx[1] + " " + sx[2] + " " + sx[3]);
            TestAssert.That(tx[0] == 341f && tx[1] == 394f, "...the threat lamps against the right end, 14 apart: " + tx[0] + " " + tx[1]);

            // Only threat lamps (Minimal), and only system lamps: the other group is absent, nothing moves.
            LampLine.Place(default, new float[] { 39f, 46f }, false, 440f, sx, tx);
            TestAssert.That(tx[0] == 341f && tx[1] == 394f, "Threat lamps alone end at 440: " + tx[0]);
            LampLine.Place(new float[] { 39f }, default, false, 440f, sx, tx);
            TestAssert.That(sx[0] == 0f, "A single system lamp sits at the left end");
            LampLine.Place(default, new float[] { 31f }, true, 440f, sx, tx);
            TestAssert.Near(tx[0], 409f, "A single threat lamp ends at 440");
            LampLine.Place(default, default, true, 440f, sx, tx);
        }

        private static void Gauges()
        {
            var page = new[] { new GaugeSpec { Id = "g", Label = "G" } };
            var reg = new[]
            {
                new GaugeSpec { Id = "lance", Rank = 0 }, new GaugeSpec { Id = "perk", Rank = 0 }, new GaugeSpec { Id = "radio", Rank = 0 },
                new GaugeSpec { Id = "ap", Rank = 0 }, new GaugeSpec { Id = "ace", Rank = 5 },
            };
            var into = new GaugeSpec[GaugeRow.Slots];
            int n = GaugeRow.Merge(page, 1, reg, 5, into);
            TestAssert.That(n == 4 && into[0].Id == "g" && into[1].Id == "ace" && into[2].Id == "lance" && into[3].Id == "perk", "Page gauge first, then registered by rank (registration order among equals): " + into[1].Id + into[2].Id + into[3].Id);
            var dup = new[] { new GaugeSpec { Id = "lance" } };
            n = GaugeRow.Merge(dup, 1, reg, 5, into);
            int lances = 0;
            for (int i = 0; i < n; i++) if (into[i].Id == "lance") lances++;
            TestAssert.That(n == 4 && lances == 1 && into[0].Id == "lance", "A page gauge displaces the registered one of the same id");
            TestAssert.Eq(GaugeRow.Merge(page, 0, reg, 0, into), 0, "No gauges, no row");
            TestAssert.Eq(GaugeRow.Merge(page, 1, reg, 0, into), 1, "A lone page gauge fills one slot");

            // Gauges exist only while live and sit from the left: whatever mix of page and registered gauges, the filled slots are the first n, none blank, none twice.
            for (int pageCount = 0; pageCount <= 2; pageCount++)
                for (int regCount = 0; regCount <= 5; regCount++)
                {
                    var own = new[] { new GaugeSpec { Id = "g" }, new GaugeSpec { Id = "lance" } };
                    var live = new GaugeSpec[GaugeRow.Slots];
                    int count = GaugeRow.Merge(own, pageCount, reg, regCount, live);
                    string tag = pageCount + " page + " + regCount + " registered";
                    TestAssert.Eq(count, System.Math.Min(GaugeRow.Slots, pageCount + regCount - (pageCount > 1 && regCount > 0 ? 1 : 0)), "Every live gauge has a slot, up to four: " + tag);
                    for (int i = 0; i < count; i++)
                    {
                        TestAssert.That(!string.IsNullOrEmpty(live[i].Id), "No blank cell between live gauges: " + tag + " slot " + i);
                        for (int k = 0; k < i; k++) TestAssert.That(live[k].Id != live[i].Id, "No gauge twice: " + tag);
                    }
                }
            GaugeLine();
        }

        /// <summary>The gauge line (LAYOUT.md 3.6): a gauge's width, packing from the left, a second line, and the sub-words giving way when two lines do not hold them.</summary>
        private static void GaugeLine()
        {
            // glyph 21 + label + 6 + value (+ 8 + sub), each word rounded up.
            TestAssert.Near(GaugeRow.Width(31.2f, 23.4f, 39f), 21f + 32f + 6f + 24f + 8f + 39f, "A gauge with its sub-word");
            TestAssert.Near(GaugeRow.Width(31.2f, 23.4f, 0f), 21f + 32f + 6f + 24f, "...and without it");

            var x = new float[4];
            var row = new int[4];
            TestAssert.Eq(GaugeRow.Pack(new float[] { 100f, 100f, 100f }, x, row), 1, "Three of 100 and two gaps of 16 are 332: one line");
            TestAssert.That(x[0] == 0f && x[1] == 116f && x[2] == 232f && row[0] == 0 && row[2] == 0, "They pack from the left, 16 apart: " + x[1] + " " + x[2]);
            TestAssert.Eq(GaugeRow.Pack(new float[] { 100f, 100f, 100f, 100f }, x, row), 2, "A fourth of 100 does not fit: a second line");
            TestAssert.That(row[3] == 1 && x[3] == 0f, "...it starts at the left of the second line");
            TestAssert.Eq(GaugeRow.Pack(new float[] { 300f, 300f, 300f }, x, row), 3, "Pack reports a third line when the widths need one");
            TestAssert.Eq(GaugeRow.Pack(new float[0], x, row), 0, "No gauge, no line");
            TestAssert.Eq(GaugeRow.Lines(new float[] { 440f }), 1, "A gauge exactly 440 wide fills one line");
            TestAssert.Eq(GaugeRow.Lines(new float[] { 441f }), 1, "...one wider still has its own line");

            // Short sub-words: the four gauges fit two lines with them.
            var label = new float[] { 39f, 39f, 39f, 39f };
            var value = new float[] { 23.4f, 23.4f, 23.4f, 23.4f };
            var sub = new float[] { 39f, 39f, 39f, 39f };
            var width = new float[4];
            TestAssert.Eq(GaugeRow.Lay(label, value, sub, 4, width, out bool subs), 2, "Four gauges of 137: two lines");
            TestAssert.That(subs && width[0] == 137f, "...with their sub-words: " + width[0]);
            TestAssert.Eq(GaugeRow.Lay(label, value, sub, 2, width, out subs), 1, "Two of 137 are one line");
            TestAssert.That(subs, "...with their sub-words");
            // Long sub-words: one gauge a line with them, four lines, so none shows its sub-word and two lines hold them.
            label = new float[] { 62.4f, 62.4f, 62.4f, 62.4f };
            value = new float[] { 46.8f, 46.8f, 46.8f, 46.8f };
            sub = new float[] { 70.2f, 70.2f, 70.2f, 70.2f };
            TestAssert.Eq(GaugeRow.Lay(label, value, sub, 4, width, out subs), 2, "Long sub-words: two lines in the end");
            TestAssert.That(!subs && width[0] == 21f + 63f + 6f + 47f, "...with no sub-word on any gauge: " + width[0]);
            TestAssert.Eq(GaugeRow.Lay(label, value, sub, 0, width, out subs), 0, "No gauge, no line");
            // However the words come out, the line count is never above two.
            label = new float[] { 300f, 300f, 300f, 300f };
            TestAssert.Eq(GaugeRow.Lay(label, value, sub, 4, width, out subs), 2, "A third line is never reported");
        }
    }
}
