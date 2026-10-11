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
            Wipe();
            Lamps();
            Gauges();
        }

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

            // WIDE (LAYOUT.md 3): box 916, cruise 160 (the SAT column's foot), live 374 (a full page with two gauge lines and the ticker under it).
            BoardGeometry cruise = ScopeLayout.Compute(All(BoardLayout.Wide, ScopeLayout.Compact));
            TestAssert.Near(cruise.Width, 916f, "WIDE is 916 wide");
            TestAssert.Near(cruise.Height, 160f, "WIDE in cruise is 160 tall");
            Same(cruise.Hero, 0f, 6f, 272f, 152f, "HERO");
            Same(cruise.Pwr, 284f, 6f, 180f, 72f, "PWR BUS");
            Same(cruise.Exp, 284f, 88f, 180f, 72f, "EXPENDABLES");
            Same(cruise.Lamps, 476f, 0f, 440f, 12f, "WIDE lamp line");
            Same(cruise.Page, 476f, 18f, 440f, 100f, "WIDE compact page");
            Same(cruise.Gauges, 476f, 126f, 440f, 12f, "WIDE gauge line (one line)");
            Same(cruise.Ticker, 476f, 144f, 440f, 12f, "WIDE ticker");
            BoardGeometry live = ScopeLayout.Compute(All(BoardLayout.Wide, ScopeLayout.Full, 2));
            TestAssert.Near(live.Height, 374f, "WIDE with a live page, two gauge lines and a notice is 374 tall");
            Same(live.Page, 476f, 18f, 440f, 300f, "WIDE live page");
            Same(live.Gauges, 476f, 326f, 440f, 30f, "WIDE gauge line (two lines) hangs 8 under the page");
            Same(live.Ticker, 476f, 362f, 440f, 12f, "WIDE ticker hangs 6 under the gauge line");
            Same(live.Hero, 0f, 6f, 272f, 152f, "HERO does not move with the page");

            // STACK (LAYOUT.md 4): box 448, cruise 362, live 580.
            BoardGeometry sc = ScopeLayout.Compute(All(BoardLayout.Stack, ScopeLayout.Compact));
            TestAssert.Near(sc.Width, 448f, "STACK is 448 wide");
            TestAssert.Near(sc.Height, 362f, "STACK in cruise is 362 tall");
            Same(sc.Hero, 4f, 6f, 440f, 108f, "STACK HERO");
            Same(sc.Pwr, 4f, 124f, 216f, 72f, "STACK PWR BUS (left half)");
            Same(sc.Exp, 228f, 124f, 216f, 72f, "STACK EXPENDABLES (right half)");
            Same(sc.Lamps, 4f, 206f, 440f, 12f, "STACK lamp line");
            Same(sc.Page, 4f, 224f, 440f, 100f, "STACK compact page");
            Same(sc.Gauges, 4f, 332f, 440f, 12f, "STACK gauge line");
            Same(sc.Ticker, 4f, 350f, 440f, 12f, "STACK ticker");
            BoardGeometry sl = ScopeLayout.Compute(All(BoardLayout.Stack, ScopeLayout.Full, 2));
            TestAssert.Near(sl.Height, 580f, "STACK with a live page is 580 tall");
            Same(sl.Gauges, 4f, 532f, 440f, 30f, "STACK live gauge line 532..562");
            Same(sl.Ticker, 4f, 568f, 440f, 12f, "STACK live ticker 568..580");

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
            Same(noGauge.Ticker, 476f, 326f, 440f, 12f, "With no gauge line the ticker hangs 8 under the page");

            // A block that is not there gives its room back (LAYOUT.md 3 and 4).
            BoardBlocks b = All(BoardLayout.Wide, ScopeLayout.Compact);
            b.Pwr = false;
            Same(ScopeLayout.Compute(b).Exp, 284f, 6f, 180f, 72f, "WIDE: with no PWR BUS, EXPENDABLES moves up to y 6");
            TestAssert.Near(ScopeLayout.Compute(b).Height, 158f, "...and the box is as tall as HERO then");
            b.Hero = false;
            Same(ScopeLayout.Compute(b).Exp, 284f, 6f, 180f, 72f, "WIDE: no HERO changes nothing else");
            TestAssert.Near(ScopeLayout.Compute(b).Height, 156f, "...and the box is as tall as the WING then (ticker 144..156)");
            b = All(BoardLayout.Wide, ScopeLayout.Compact);
            b.Lamps = false;
            Same(ScopeLayout.Compute(b).Page, 476f, 18f, 440f, 100f, "WIDE: with no lamp line the page keeps y 18");
            b.PageH = 0f;
            Same(ScopeLayout.Compute(b).Gauges, 476f, 18f, 440f, 12f, "WIDE: with no page the first block under the lamp line starts at y 18");
            b.GaugeLines = 0;
            Same(ScopeLayout.Compute(b).Ticker, 476f, 18f, 440f, 12f, "WIDE: with no page and no gauge line the ticker starts at y 18");
            b = All(BoardLayout.Stack, ScopeLayout.Compact);
            b.Hero = false;
            Same(ScopeLayout.Compute(b).Pwr, 4f, 6f, 216f, 72f, "STACK: with no HERO the SAT row starts at y 6");
            b.Pwr = false;
            Same(ScopeLayout.Compute(b).Exp, 4f, 6f, 216f, 72f, "STACK: with no PWR BUS, EXPENDABLES takes its half");
            b.Exp = false;
            Same(ScopeLayout.Compute(b).Lamps, 4f, 6f, 440f, 12f, "STACK: with nothing above it the lamp line starts at y 6");
            b.Lamps = false;
            Same(ScopeLayout.Compute(b).Page, 4f, 6f, 440f, 100f, "STACK: with nothing above it the page starts at y 6");
            b = All(BoardLayout.Stack, ScopeLayout.Compact);
            b.Lamps = false;
            Same(ScopeLayout.Compute(b).Page, 4f, 206f, 440f, 100f, "STACK: with no lamp line the page is 10 under the SAT row");
            TestAssert.Near(ScopeLayout.Compute(new BoardBlocks { Layout = BoardLayout.Wide, Lamps = true }).Height, 12f, "The lamp line alone is a 12 unit box");

            // Every combination: nothing negative, nothing outside the box, the box is as tall as the lowest block, no two blocks touch (the tightest gap is 6, lamp line to page).
            int boards = 0;
            foreach (BoardLayout layout in new[] { BoardLayout.Wide, BoardLayout.Stack })
                for (int mask = 0; mask < 32; mask++)
                    foreach (float pageH in new[] { 0f, ScopeLayout.Compact, 160f, ScopeLayout.Full })
                        foreach (int lines in new[] { 0, 1, 2 })
                        {
                            var blocks = new BoardBlocks { Layout = layout, Hero = (mask & 1) != 0, Pwr = (mask & 2) != 0, Exp = (mask & 4) != 0, Lamps = (mask & 8) != 0, Ticker = (mask & 16) != 0, PageH = pageH, GaugeLines = lines };
                            BoardGeometry g = ScopeLayout.Compute(blocks);
                            string tag = layout + " " + mask + " " + pageH + " " + lines;
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
            TestAssert.That(boards == 2 * 32 * 4 * 3, "Every combination was covered: " + boards);

            TestAssert.Near(ScopeLayout.Body(true, false), 100f, "The compact body is the whole strip");
            TestAssert.Near(ScopeLayout.Body(false, false), 300f, "A full page body is 300");
            TestAssert.Near(ScopeLayout.Body(false, true), 274f, "A page strip takes 26 off a full page");
            TestAssert.Near(ScopeLayout.Body(true, true), 100f, "The compact strip never has a page strip");
            TestAssert.Near(ScopeLayout.WingBodyY, 18f, "The page starts 18 down in WIDE");

            // The gauge line packs its gauges 16 apart into 440 wide lines, and never into a third line.
            TestAssert.Eq(ScopeLayout.GaugeLines(new float[0]), 0, "No gauge, no line");
            TestAssert.Eq(ScopeLayout.GaugeLines(new[] { 90f, 90f, 90f, 90f }), 1, "4 x 90 and 3 gaps of 16 is 408: one line");
            TestAssert.Eq(ScopeLayout.GaugeLines(new[] { 100f, 100f, 100f, 100f }), 2, "4 x 100 and 3 gaps of 16 is 448: two lines");
            TestAssert.Eq(ScopeLayout.GaugeLines(new[] { 300f, 300f, 300f, 300f }), 2, "A third line is never made");

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
            TestAssert.That(!t.Running && t.Height == ScopeLayout.Full && t.Wipe >= 1f, "The wipe ends in 160 ms at the new height");
            t.Update(3, ScopeLayout.Compact, 3f);
            t.Update(3, ScopeLayout.Compact, 3.05f);
            float mid = t.Height;
            t.Update(4, ScopeLayout.Full, 3.06f);
            TestAssert.That(t.Height == mid, "A change mid-wipe eases from where the scope is, never jumps");
            t.Reset();
            TestAssert.That(t.Height == 0f && !t.Running, "Reset forgets the page");
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
            // A Mode gauge's progress rail is a plain 0..1 (a NAV leg 0.1, 0.3, 0.5...): the chevron only ever moves right as the progress grows.
            float prev = float.NegativeInfinity;
            for (float v = 0.1f; v < 0.95f; v += 0.2f)
            {
                float x = GaugeRow.RailX(10f, 109f, v);
                TestAssert.That(x > prev, "The progress chevron is monotonic in the progress: " + v);
                prev = x;
            }
            TestAssert.Near(GaugeRow.RailX(10f, 109f, 0f), 16f, "No progress sits at the rail's start");
            TestAssert.Near(GaugeRow.RailX(10f, 109f, 1f), 97f, "Full progress sits at the diamond");
            TestAssert.Near(GaugeRow.RailX(10f, 109f, 2f), 97f, "Progress beyond 1 is clamped");
        }
    }
}
