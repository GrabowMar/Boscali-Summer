using System;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The furniture of the minimised map's corner (LAYOUT.md 7), from the native rectangle (DL EVT LOG's is EventLogTests').</summary>
    internal static class CornerLayoutTests
    {
        public static void Run()
        {
            Map();
        }

        private static void At(float actual, float expected, string what) => TestAssert.That(Math.Abs(actual - expected) < 0.01f, what + ": " + actual + ", want " + expected);

        /// <summary>TAC MAP: the plate is 0..320 by 760..1080; brackets (4, 766) to (316, 1068) = 312 x 302, arm 18; the shelf INSIDE the frame at its lower left (its line 14 over the bottom bracket line, from x 10, 104 wide).</summary>
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
            At(f.ShelfX, 10f, "The shelf starts 6 in from the left brackets");
            At(f.ShelfY + 6f, 1054f, "...its line 14 over the bottom brackets' line (y 1054), inside the frame where a screen-edge marker cannot cover it");
            At(f.ShelfW, 104f, "...104 wide: the caption and a short run of line, not the frame's width");
            TestAssert.That(f.ShelfY + 6f > f.Y0 && f.ShelfX + f.ShelfW < f.X1, "...wholly inside the brackets");

            // A plate that runs past the screen's bottom: the bottom pair stays 12 over the screen's bottom.
            CornerFurniture past = CornerLayout.Map(0f, 780f, 320f, 1110f, 1080f, 1f);
            At(past.Y1, 1068f, "A plate running past the screen: the bottom brackets stay on it");

            // 720p: k 0.784, the native plate in canvas units 0..320 by 760..1080 of 1080; offsets and arm carried by k, the shelf's gap (design units) not.
            float k = 1f / 1.275f;
            CornerFurniture small = CornerLayout.Map(0f, 760f, 320f, 1080f, 1080f, k);
            At(small.X0, 4f * k, "At 720p the map brackets are 4 canvas units in (3.1 design units)");
            At(small.Arm, (float)Math.Round(18f * k), "...the arm 18 canvas units (14)");
            At(small.Y1, 1068f * k, "...the bottom pair 12 canvas units over the screen's bottom");
            At(small.ShelfY + 6f, small.Y1 - 14f, "...the shelf 14 design units over their bottom line");
            At(small.ShelfX, small.X0 + 6f, "...6 design units in from the left brackets");
            North();
        }

        /// <summary>The map turns with the aircraft (the game sets its image's z rotation to the heading, counter-clockwise): north is a tick on the frame, clear of the corner arms and of the caption shelf.</summary>
        private static void North()
        {
            CornerFurniture f = CornerLayout.Map(0f, 760f, 320f, 1080f, 1080f, 1f);      // (4, 766) to (316, 1068), centre (160, 917)
            CornerLayout.MapNorth up = CornerLayout.North(f, 0f);
            TestAssert.That(up.Present && up.Side == 0, "Heading north: north is at the top of the frame");
            At(up.X, 160f, "...in the middle of its top edge");
            At(up.Y, 766f, "...on the frame's top line");
            At(up.InY, 774f, "...the tick runs 8 inwards (down)");
            At(up.InX, up.X, "...a vertical tick");
            At(up.LabelY, 766f + 8f + 3f + 6f, "...the N is 3 clear of its end");

            CornerLayout.MapNorth east = CornerLayout.North(f, 90f);      // heading east: north is on the left
            TestAssert.That(east.Present && east.Side == 3, "Heading east: north is on the left of the frame");
            At(east.X, 4f, "...on its left line");
            At(east.Y, 917f, "...in the middle of it");
            At(east.InX, 12f, "...the tick runs 8 inwards (right)");
            At(east.LabelX, 4f + 8f + 3f + 4f, "...and the N beside it");

            CornerLayout.MapNorth west = CornerLayout.North(f, 270f);
            TestAssert.That(west.Side == 1, "Heading west: north is on the right");
            At(west.X, 316f, "...on the right line");
            At(west.InX, 308f, "...its tick inwards (left)");

            CornerLayout.MapNorth south = CornerLayout.North(f, 180f);
            TestAssert.That(south.Side == 2, "Heading south: north is at the bottom");
            At(south.Y, 1068f, "...on the bottom line");
            At(south.InY, 1060f, "...its tick inwards (up)");
            TestAssert.That(south.X >= f.ShelfX + f.ShelfW + 8f, "...clear of the caption shelf: x " + south.X);

            // Near a corner the tick slides along its edge to stay clear of the arms (18) by 8, and off the caption shelf.
            CornerLayout.MapNorth corner = CornerLayout.North(f, 225f);
            TestAssert.That(corner.Side == 2 && corner.X <= f.X1 - f.Arm - CornerLayout.NorthClear + 0.01f, "Heading 225: the tick stays off the lower right corner's arm: x " + corner.X);
            CornerLayout.MapNorth topLeft = CornerLayout.North(f, 40f);      // north is up and a little left: the top edge near its left corner
            TestAssert.That(topLeft.X >= f.X0 + f.Arm + CornerLayout.NorthClear - 0.01f || topLeft.Side == 3, "A tick near the top left corner stays off its arm: " + topLeft.X + " side " + topLeft.Side);
            for (int deg = 0; deg < 360; deg += 5)
            {
                CornerLayout.MapNorth n = CornerLayout.North(f, deg);
                bool onHorizontal = n.Side == 0 || n.Side == 2;
                float along = onHorizontal ? n.X : n.Y, lo = onHorizontal ? f.X0 : f.Y0, hi = onHorizontal ? f.X1 : f.Y1;
                TestAssert.That(n.Present && along >= lo + f.Arm + CornerLayout.NorthClear - 0.01f && along <= hi - f.Arm - CornerLayout.NorthClear + 0.01f, "Turned " + deg + ": the tick is clear of every corner arm (" + along + ")");
                if (n.Side == 2) TestAssert.That(n.X >= f.ShelfX + f.ShelfW + 8f, "Turned " + deg + ": clear of the caption shelf on the bottom edge");
            }
            TestAssert.That(!CornerLayout.North(f, float.NaN).Present, "A turn that cannot be read: no tick");
            TestAssert.That(!CornerLayout.North(default, 0f).Present, "No frame: no tick");
            // 720p (k 0.784): the same rule on the smaller frame.
            float k = 1f / 1.275f;
            CornerFurniture small = CornerLayout.Map(0f, 760f, 320f, 1080f, 1080f, k);
            CornerLayout.MapNorth tiny = CornerLayout.North(small, 0f);
            TestAssert.That(tiny.Present && tiny.Side == 0 && Math.Abs(tiny.X - (small.X0 + small.X1) * 0.5f) < 0.01f, "At 720p north is still the middle of the top edge");
        }
    }
}
