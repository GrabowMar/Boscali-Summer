using System;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The furniture of the two screen corners (LAYOUT.md 7), from the native rectangle.</summary>
    internal static class CornerLayoutTests
    {
        public static void Run()
        {
            Feed();
            Map();
        }

        private static void At(float actual, float expected, string what) => TestAssert.That(Math.Abs(actual - expected) < 0.01f, what + ": " + actual + ", want " + expected);

        /// <summary>DL EVT LOG: the game's box is 0..475 by 0..90 for four lines (the mock's lines at 21, 39, 58, 76). Brackets at x 2, corners at y 3 and 86, arms 10; the shelf's line at y 98 from x 2, 150 wide.</summary>
        private static void Feed()
        {
            CornerFurniture f = CornerLayout.Feed(0f, 0f, 475f, 90f, 1f);
            At(f.X0, 2f, "Feed brackets stand 2 in from the box");
            At(f.Y0, 3f, "...the top corner 3 under its top (the game's first line starts higher than the mock drew it: its capitals reach up to y 9)");
            At(f.Y1, 86f, "...the bottom corner 4 over its bottom");
            At(f.Arm, 10f, "...arms of 10");
            TestAssert.That(!f.Both && f.Present, "Two brackets on the left only");
            At(f.ShelfX, 2f, "The shelf starts at x 2");
            At(f.ShelfY + 6f, 98f, "...its line at y 98 (6 under the block top the painter is given)");
            At(f.ShelfW, 150f, "...150 wide");

            // The box grows and moves with its lines and the screen: one line, and a box that is not at the corner.
            CornerFurniture one = CornerLayout.Feed(0f, 0f, 300f, 34f, 1f);
            TestAssert.That(one.Present && one.Arm * 2f < one.Y1 - one.Y0, "One line (a 34 high box, as in game): the brackets do not meet: " + one.Arm);
            // The corner clears the first letter of a one line box: its capitals reach y 9 (seen in game), the top arm is 6 over them.
            TestAssert.That(one.Y0 <= 9f - 6f + 0.01f, "...and the top arm at y " + one.Y0 + " stays over the capitals (y 9 down), not through them");
            TestAssert.That(one.Y1 >= 21f + 5f, "...the bottom arm at y " + one.Y1 + " under the line (its baseline at y 21, descenders to 25)");
            CornerFurniture tiny = CornerLayout.Feed(0f, 0f, 300f, 22f, 1f);
            TestAssert.That(tiny.Present && tiny.Arm < 10f && tiny.Arm * 2f < tiny.Y1 - tiny.Y0, "A box too short for arms of 10: they shrink so the two brackets do not meet: " + tiny.Arm);
            CornerFurniture low = CornerLayout.Feed(40f, 200f, 515f, 290f, 1f);
            At(low.X0, 42f, "A box 40 over: the brackets follow");
            At(low.Y0, 203f, "...and 200 down");
            At(low.ShelfY + 6f, 298f, "...and so does the shelf");

            // At 720p a canvas unit is 0.667 of a pixel and a design unit 0.85: the box is carried into design units (k 0.784), the mock's own offsets (design units) apply to it.
            float k = 1f / 1.275f;
            CornerFurniture small = CornerLayout.Feed(0f, 0f, 475f, 90f, k);
            At(small.X0, 2f, "At 720p the feed brackets are still 2 from the box's edge");
            At(small.Y1, 90f * k - 4f, "...the bottom corner 4 over its bottom (in design units)");
            At(small.ShelfY + 6f, 90f * k + 8f, "...and the shelf 8 under it");
        }

        /// <summary>TAC MAP: the plate is 0..320 by 760..1080; brackets (4, 766) to (316, 1068) = 312 x 302, arm 18; the shelf's line 16 over them, from x 4, 312 wide.</summary>
        private static void Map()
        {
            CornerFurniture f = CornerLayout.Map(0f, 760f, 320f, 1080f, 1080f, 1f);
            At(f.X0, 4f, "Map brackets: left 4 in");
            At(f.Y0, 766f, "...top 6 under the plate's top");
            At(f.X1, 316f, "...right 4 in");
            At(f.Y1, 1068f, "...bottom 12 over the screen's bottom");
            At(f.X1 - f.X0, 312f, "...312 wide");
            At(f.Y1 - f.Y0, 302f, "...302 tall");
            At(f.Arm, 18f, "...arms of 18");
            TestAssert.That(f.Both && f.Present, "Four brackets");
            At(f.ShelfX, 4f, "The shelf starts at the brackets' left");
            At(f.ShelfY + 6f, 750f, "...its line 16 over their top (y 750)");
            At(f.ShelfW, 312f, "...as wide as they are");

            // A plate that runs past the screen's bottom: the bottom pair stays 12 over the screen's bottom.
            CornerFurniture past = CornerLayout.Map(0f, 780f, 320f, 1110f, 1080f, 1f);
            At(past.Y1, 1068f, "A plate running past the screen: the bottom brackets stay on it");

            // 720p: k 0.784, the native plate in canvas units 0..320 by 760..1080 of 1080; offsets and arm carried by k, the shelf's gap (design units) not.
            float k = 1f / 1.275f;
            CornerFurniture small = CornerLayout.Map(0f, 760f, 320f, 1080f, 1080f, k);
            At(small.X0, 4f * k, "At 720p the map brackets are 4 canvas units in (3.1 design units)");
            At(small.Arm, (float)Math.Round(18f * k), "...the arm 18 canvas units (14)");
            At(small.Y1, 1068f * k, "...the bottom pair 12 canvas units over the screen's bottom");
            At(small.ShelfY + 6f, small.Y0 - 16f, "...the shelf 16 design units over");
        }
    }
}
