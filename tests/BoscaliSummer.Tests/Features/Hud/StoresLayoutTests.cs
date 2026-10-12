using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The numbers inside the stores blocks (LAYOUT.md 3.1 to 3.3, WIDE and STACK) and where the three blocks sit when PWR BUS has no cost row or the shelves are off.</summary>
    internal static class StoresLayoutTests
    {
        public static void Run()
        {
            Hero();
            Numeral();
            Tags();
            Dcl();
            Rail();
            BracketHugsMarker();
            JammerMarker();
            JammerRow();
            Geometry();
            Shelves();
        }

        /// <summary>The jammer marker rides under the cells at +59; under WIDE's 54 high block (no cost row) that is the gap above the next block's shelf stub, so it rides 2 higher there (the stub touched it at a full charge in game).</summary>
        private static void JammerMarker()
        {
            TestAssert.Near(StoresLayout.JamMark(true), 59f, "With room under the cells the marker is the spec's +59");
            float tip = ScopeLayout.SatH - ScopeLayout.CostRow + ScopeLayout.BlockGap - 2f;      // the next shelf's stub tip: its top (54 + 10) less 2 (the shelf at +6, the stub 8 up from it)
            float low = StoresLayout.JamMark(false) + 0.6f * 3f + 1f;                            // the triangle's base (0.6 of its size under its centre) at the blink's low beat
            TestAssert.That(tip - low >= 2f, "Without a cost row the marker's base stays 2 clear of the stub tip (" + low + " over " + tip + ")");
            TestAssert.That(tip - (StoresLayout.JamMarkY + 0.6f * 3f + 1f) < 2f, "(+59 under that block is what touched it: " + (StoresLayout.JamMarkY + 0.6f * 3f + 1f) + " over " + tip + ")");
            TestAssert.That(StoresLayout.JamMark(false) >= StoresLayout.CellsTop + StoresMath.CellH, "...and it is still under the cells");
        }

        /// <summary>The jammer's row sits under the dispenser rows there are: flares only put it where the chaff row would be, no dispenser where the flares' row is (it sat 41 under the flares in game).</summary>
        private static void JammerRow()
        {
            TestAssert.Near(StoresLayout.JamRow(2), 66f, "Two dispensers: the jammer at +66");
            TestAssert.Near(StoresLayout.JamRow(3), 66f, "...the block shows two at most");
            TestAssert.Near(StoresLayout.JamRow(1), 46f, "Flares only: the jammer takes the second row, 20 under the first");
            TestAssert.Near(StoresLayout.JamRow(0), 25f, "No dispenser: the jammer is the first row");
        }

        /// <summary>WIDE's selected marker is bracketed no wider than its marker plus a few units, whatever the number of stations (the bracket is 3 short of the pitch).</summary>
        private static void BracketHugsMarker()
        {
            for (int n = 1; n <= 8; n++)
            {
                float pitch = StoresLayout.RailPitch(n, 270f), glyph = StoresLayout.RailGlyph(pitch, false), content = StoresLayout.RailTagX(glyph) + 0.65f * 12f;
                TestAssert.That(pitch - 3f - content <= 4f, n + " stations: the bracket " + (pitch - 3f) + " wide round a marker " + content + " wide");
            }
        }

        /// <summary>The caption shelves of PWR BUS and EXPENDABLES (LAYOUT.md 3.2, 3.3, 4): 8 short of the block, STACK's PWR BUS 12, so its stub tip stays 12 clear of the EXPENDABLES shelf beside it.</summary>
        private static void Shelves()
        {
            TestAssert.Near(StoresLayout.PwrShelf(false, 180f), 172f, "WIDE PWR BUS: 180 wide, its shelf line 172");
            TestAssert.Near(StoresLayout.ExpShelf(180f), 172f, "WIDE EXPENDABLES: 172");
            TestAssert.Near(StoresLayout.PwrShelf(true, ScopeLayout.StackHalfW), 204f, "STACK PWR BUS: 216 wide, its shelf line 204");
            TestAssert.Near(StoresLayout.ExpShelf(ScopeLayout.StackHalfW), 208f, "STACK EXPENDABLES: 208");
            // The node centres on the line's end and the stub's tip lands 8 units on (ScopeCanvas.Terminal): that tip is 12 short of the block beside.
            float tip = ScopeLayout.StackX + StoresLayout.PwrShelf(true, ScopeLayout.StackHalfW) + 8f;
            TestAssert.Near(ScopeLayout.StackHalfX - tip, 12f, "STACK: PWR BUS's stub tip is 12 clear of the EXPENDABLES block");
            TestAssert.That(ScopeLayout.StackHalfX - (ScopeLayout.StackX + ScopeLayout.StackHalfW) == 8f, "...the two blocks 8 apart");
        }

        private static void Hero()
        {
            StoresLayout.HeroPlan w = StoresLayout.Hero(false, 272f), s = StoresLayout.Hero(true, 440f);
            TestAssert.That(w.NameY == 68f && w.TagW == 116f && w.ArtX == 0f && w.ArtY == 76f && w.ArtW == 272f && w.ArtH == 56f, "WIDE: the name row at +68, the tags 116 wide, the art box 272 x 56 at +76");
            TestAssert.That(w.RailY == 144f && w.RailW == 270f && w.DclX == 266f && w.DclY == StoresLayout.ArmY && w.ShelfW == 264f, "WIDE: the rail at +144 and 270 wide, the DCL flag in the arm row at 266, the shelf 8 short of the block");
            TestAssert.That(s.NameY == 76f && s.TagW == 76f && s.ArtX == 240f && s.ArtY == 18f && s.ArtW == 200f && s.ArtH == 56f, "STACK: the name row at +76, one tag a row, the art box 200 x 56 at (240, +18)");
            TestAssert.That(s.RailY == 100f && s.RailW == 438f && s.DclX == 428f && s.DclY == 6f && s.ShelfW == 432f, "STACK: the rail at +100 and 438 wide, the DCL flag at the top right");
        }

        private static void Numeral()
        {
            TestAssert.Near(StoresLayout.NumeralSize(3, 3), 52f, "500 /500 keeps the full 52");
            TestAssert.Near(StoresLayout.NumeralSize(1, 0), 52f, "A lone figure keeps it too");
            TestAssert.Near(StoresLayout.NumeralSize(4, 4), 37f, "1500 /1500 shrinks so the load still ends 8 short of the arm column");
            TestAssert.Near(StoresLayout.NumeralSize(7, 7), 24f, "...but never under 24");
            // The numeral and "/max" really end short of the arm column at the size chosen.
            for (int digits = 1; digits <= 5; digits++)
            {
                float size = StoresLayout.NumeralSize(digits, digits), end = StoresLayout.NumeralX + digits * 0.65f * size + StoresLayout.MaxGap + (1 + digits) * 0.65f * StoresLayout.MaxSize;
                TestAssert.That(size <= 24f ? true : end <= StoresLayout.ArmMarkerX - StoresLayout.NumeralGap + 0.01f, digits + " digits end at " + end + ", before the arm column");
            }
        }

        private static void Tags()
        {
            float x = StoresLayout.TagAt(0f, 31.2f, 116f, out bool wrap);
            TestAssert.That(x == 0f && !wrap, "The first tag starts the row");
            x = StoresLayout.TagAt(31.2f, 62.4f, 116f, out wrap);
            TestAssert.Near(x, 41.2f, "LINK and TRT AUTO pack 10 apart on one WIDE row");
            TestAssert.That(!wrap, "...they fit the 116");
            x = StoresLayout.TagAt(31.2f, 62.4f, 76f, out wrap);
            TestAssert.That(x == 0f && wrap, "STACK's 76 wide column takes one tag a row: the second wraps");
            x = StoresLayout.TagAt(0f, 200f, 76f, out wrap);
            TestAssert.That(x == 0f && !wrap, "A tag wider than an empty row stays on it (it is never wrapped away)");
        }

        private static void Dcl()
        {
            float left = 266f - 39f;      // "DCL 1" is 5 characters of 7.8
            TestAssert.That(StoresLayout.DclRow(new[] { 168f + 5f * 9.1f, 0f }, left) == 0, "ARMED leaves the arm row 13 clear of the flag: it stays there");
            TestAssert.That(StoresLayout.DclRow(new[] { 168f + 7f * 9.1f, 0f }, left) == 1, "RELEASE comes within 10 of it: the flag goes to the first tag row");
            TestAssert.That(StoresLayout.DclRow(new[] { 168f + 6f * 9.1f, 0f }, left) == 1, "LAUNCH too");
            TestAssert.That(StoresLayout.DclRow(new[] { 168f + 7f * 9.1f, 156f + 103.6f }, left) == 2, "...and below the tags when they are in the way as well");
            TestAssert.That(StoresLayout.DclRow(new float[0], left) == 0, "No rows, no conflict");
        }

        private static void Rail()
        {
            TestAssert.Near(StoresLayout.RailPitch(8, 270f), 33f, "Eight stations on WIDE's 270: the prototype's 33");
            TestAssert.Near(StoresLayout.RailPitch(8, 438f), 54f, "...and on STACK's 438: 54");
            TestAssert.Near(StoresLayout.RailPitch(12, 270f), 22f, "Twelve stations: 22");
            TestAssert.Near(StoresLayout.RailPitch(0, 270f), 33f, "No stations: no division by zero");
            TestAssert.Near(StoresLayout.RailPitch(1, 270f), 33f, "A lone gun packs at the left at the eight station pitch, so its brackets stay marker sized");
            TestAssert.Near(StoresLayout.RailPitch(4, 270f), 33f, "...as do four stations (they spread to 54 in game, a bracket 51 wide round a marker 31 wide)");
            TestAssert.Near(StoresLayout.RailPitch(4, 438f), 54f, "...and STACK's rail keeps its own 54");
            TestAssert.Near(StoresLayout.RailGlyph(33f, false), 14f, "At 33 the glyph is the full 14");
            TestAssert.Near(StoresLayout.RailGlyph(54f, true), 14f, "...never more");
            TestAssert.Near(StoresLayout.RailGlyph(30f, false), 13f, "A crowded rail shortens the glyph to keep the tag clear");
            TestAssert.Near(StoresLayout.RailGlyph(27f, false), 10f, "...more as it crowds");
            TestAssert.Near(StoresLayout.RailGlyph(24f, true), 0f, "...until only the tag is left (two digit numbers need more room)");
            TestAssert.Near(StoresLayout.RailTagX(14f), 20f, "The tag follows the full glyph at +20");
            TestAssert.Near(StoresLayout.RailTagX(0f), 2f, "...and starts at +2 with no glyph");
            // At every pitch the tag, wherever the glyph ends, stays inside the bracket the selected marker wears (3 short of the pitch).
            for (int n = 1; n <= 12; n++)
            {
                float pitch = StoresLayout.RailPitch(n, 270f), glyph = StoresLayout.RailGlyph(pitch, n >= 10), tagW = (n >= 10 ? 2f : 1f) * 0.65f * 12f, end = StoresLayout.RailTagX(glyph) + tagW;
                TestAssert.That(end <= pitch - 3f + 0.01f, n + " stations: the tag ends at " + end + " inside a pitch of " + pitch);
            }
        }

        private static BoardBlocks Wide(bool cost, bool noShelves) => new BoardBlocks { Layout = BoardLayout.Wide, Hero = true, Pwr = true, PwrCost = cost, Exp = true, NoShelves = noShelves };

        private static void Same(BoardRect r, float x, float y, float w, float h, string what)
        {
            TestAssert.That(r.Present && Math.Abs(r.X - x) < 0.001f && Math.Abs(r.Y - y) < 0.001f && Math.Abs(r.W - w) < 0.001f && Math.Abs(r.H - h) < 0.001f,
                what + ": (" + r.X + ", " + r.Y + ", " + r.W + ", " + r.H + ") should be (" + x + ", " + y + ", " + w + ", " + h + ")");
        }

        private static void Geometry()
        {
            BoardGeometry cost = ScopeLayout.Compute(Wide(true, false));
            Same(cost.Pwr, 284f, 6f, 180f, 72f, "WIDE with an energy weapon: PWR BUS 72 high (the cost row at +66)");
            Same(cost.Exp, 284f, 88f, 180f, 72f, "...and EXPENDABLES at y 88");
            BoardGeometry plain = ScopeLayout.Compute(Wide(false, false));
            Same(plain.Pwr, 284f, 6f, 180f, 54f, "WIDE without one: PWR BUS is 54 high");
            Same(plain.Exp, 284f, 70f, 180f, 72f, "...and EXPENDABLES is pulled up to y 70");
            BoardGeometry lean = ScopeLayout.Compute(Wide(true, true));
            Same(lean.Exp, 284f, 88f, 180f, 53f, "WIDE with no shelves: EXPENDABLES has no shelf row, 19 shorter (its words move up by that, so its first row is 10 under the cost row)");
            BoardGeometry leanPlain = ScopeLayout.Compute(Wide(false, true));
            Same(leanPlain.Exp, 284f, 70f, 180f, 53f, "...and with no cost row either");
            BoardBlocks b = Wide(false, false);
            b.Pwr = false;
            Same(ScopeLayout.Compute(b).Exp, 284f, 6f, 180f, 72f, "With no PWR BUS EXPENDABLES is at y 6 whatever the cost row");
            b.NoShelves = true;
            Same(ScopeLayout.Compute(b).Exp, 284f, 6f, 180f, 53f, "...and shelf-less it is as short as its words");
            BoardGeometry stack = ScopeLayout.Compute(new BoardBlocks { Layout = BoardLayout.Stack, Hero = true, Pwr = true, Exp = true, NoShelves = true });
            Same(stack.Pwr, 4f, 124f, 216f, 72f, "STACK keeps both halves 72 high with or without the cost row");
            Same(stack.Exp, 228f, 124f, 216f, 72f, "...and the shelves");
            for (int mask = 0; mask < 16; mask++)
            {
                var blocks = new BoardBlocks { Layout = BoardLayout.Wide, Hero = true, Pwr = (mask & 1) != 0, PwrCost = (mask & 2) != 0, Exp = true, NoShelves = (mask & 4) != 0, Lamps = (mask & 8) != 0 };
                BoardGeometry g = ScopeLayout.Compute(blocks);
                if (g.Pwr.Present) TestAssert.That(g.Exp.Y - g.Pwr.Bottom >= 10f - 0.001f, "PWR BUS and EXPENDABLES keep their 10 apart: " + mask);
                TestAssert.That(g.Exp.Bottom <= g.Height + 0.001f && g.Hero.Bottom <= g.Height + 0.001f, "Both stay inside the box: " + mask);
            }
        }
    }
}
