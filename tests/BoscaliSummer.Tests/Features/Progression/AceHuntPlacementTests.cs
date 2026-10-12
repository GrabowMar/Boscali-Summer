using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Progression.Domain;

namespace BoscaliSummer.Tests
{
    /// <summary>The ace-hunt banner steps aside for the TAC BOARD's box (<c>IHudBoard.TryGetScreenBox</c>): the panels are centred along the top, the board's stores side reaches under their right end.</summary>
    internal static class AceHuntPlacementTests
    {
        internal static void Run()
        {
            foreach (var screen in new[] { (w: 1920f, h: 1080f), (w: 1280f, h: 720f), (w: 2560f, h: 1440f), (w: 1920f, h: 1200f) })
                Clears(screen.w, screen.h);
            Narrow();
            Rule();
        }

        /// <summary>The board's WIDE box in cruise on a screen of <paramref name="w"/> x <paramref name="h"/> pixels, as the board reports it: left, top, right, bottom in pixels from the top-left corner.</summary>
        private static void Box(float w, float h, out float left, out float top, out float right, out float bottom)
        {
            float unit = Math.Max(h / 1080f, 0.85f);      // pixels to a design unit: the board's scale never goes under 0.85
            BoardGeometry geo = ScopeLayout.Compute(new BoardBlocks { Layout = BoardLayout.Wide, Hero = true, Pwr = true, PwrCost = true, Exp = true, Lamps = true, PageH = ScopeLayout.Compact });
            ScopeLayout.Anchor(w, h, unit, unit, 0f, 0f, geo.Width, geo.Height, out float fromRight, out float fromTop);
            right = w - fromRight;
            left = right - geo.Width * unit;
            top = fromTop;
            bottom = top + geo.Height * unit;
        }

        private static bool Meets(float x, float width, float top, float height, float left, float boxTop, float right, float bottom) =>
            x < right && x + width > left && top < bottom && top + height > boxTop;

        /// <summary>At 16:9 and wider the banner (both panels) is moved to the left of the board, 12 clear, and stays on the screen; without the move they meet.</summary>
        private static void Clears(float w, float h)
        {
            Box(w, h, out float left, out float top, out float right, out float bottom);
            float scale = Math.Min(w / 1920f, h / 1080f), k = 1f / scale, screenW = w * k;      // the banner's canvas is scaled to fit (Expand): its units are pixels over that
            foreach (var panel in new[] { (width: 704f, top: 48f, height: 184f), (width: 420f, top: 8f, height: 36f) })
            {
                float x0 = (screenW - panel.width) / 2f;
                bool met = Meets(x0, panel.width, panel.top, panel.height, left * k, top * k, right * k, bottom * k);
                float shift = AceHuntPlacement.Shift(screenW, panel.width, panel.top, panel.height, left * k, top * k, right * k, bottom * k);
                string what = panel.width + " wide at " + w + "x" + h;
                TestAssert.That(met, "Unmoved the panel meets the board: " + what);
                TestAssert.That(shift < 0f && x0 + shift >= 0f, "It moves left, and stays on the screen: " + what + " by " + shift);
                TestAssert.That(!Meets(x0 + shift, panel.width, panel.top, panel.height, left * k, top * k, right * k, bottom * k), "...and clear of the board: " + what);
                TestAssert.Near(x0 + shift + panel.width, left * k - AceHuntPlacement.Gap, "...12 units from it: " + what);
            }
        }

        /// <summary>At 4:3 the board is wider than the screen leaves room for: the panel goes as far left as the screen allows and the overlap is smaller than it was.</summary>
        private static void Narrow()
        {
            float w = 1280f, h = 960f;
            Box(w, h, out float left, out float top, out float right, out float bottom);
            float scale = Math.Min(w / 1920f, h / 1080f), k = 1f / scale, screenW = w * k, x0 = (screenW - 704f) / 2f;
            float shift = AceHuntPlacement.Shift(screenW, 704f, 48f, 184f, left * k, top * k, right * k, bottom * k);
            TestAssert.That(shift < 0f && Math.Abs(x0 + shift) < 0.01f, "At 4:3 the first panel goes flush with the screen's left edge: " + shift);
            float before = x0 + 704f - left * k, after = 704f - left * k;
            TestAssert.That(after < before, "...which leaves less of it over the board: " + after + " against " + before);
        }

        /// <summary>The rule itself: no board, a board elsewhere, below or beside the panel changes nothing; the shorter way out that fits is taken.</summary>
        private static void Rule()
        {
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 0f, 0f, 0f, 0f), 0f, "A board with no size is no board");
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 996f, 300f, 1912f, 460f), 0f, "A board that starts below the panel is not in its way");
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 1400f, 6f, 1912f, 166f), 0f, "...nor one that starts to the right of it (with the 12 clear)");
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 1324f, 6f, 1912f, 166f), 0f, "...12 clear is enough");
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 1316f, 6f, 1912f, 166f), -8f, "...4 clear needs 8 more");
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 1300f, 6f, 1912f, 166f), -24f, "A board 12 over the panel's right edge: 24 left");
            // A board in the middle of the screen. Panel 608..1312; board 700..800: out to the left takes 624 and would leave the screen, out to the right 204.
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 700f, 6f, 800f, 166f), 204f, "A board in the middle: the way out that stays on the screen");
            // Both fit: the shorter. Panel 608..1312; board 1000..1100: left 1000 - 12 - 1312 = -324 (to 284), right 1112 - 608 = 504 (to 1816): left wins.
            TestAssert.Near(AceHuntPlacement.Shift(1920f, 704f, 48f, 184f, 1000f, 6f, 1100f, 166f), -324f, "Both fit: the shorter, to the left");
        }
    }
}
