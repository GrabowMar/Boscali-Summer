using System;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The game's weapon pictures as an alpha mask (LAYOUT.md 1 "line art"): light lines on opaque black become white with the brightness as alpha, the ink box is measured on the way, and the cache keeps 32.</summary>
    internal static class StoreMaskTests
    {
        public static void Run()
        {
            AlphaRule();
            Ground();
            Convert();
            NoInk();
            Cache();
        }

        /// <summary>The game's pictures are DXT1: their "black" ground is 1 to 4 (up to 24 beside a stroke), which alpha showed as a faint tinted box over the whole picture. The ground goes to nothing, a solid line stays solid.</summary>
        private static void Ground()
        {
            TestAssert.Eq(StoreMask.Clean(0), (byte)0, "Nothing stays nothing");
            foreach (int ground in new[] { 1, 2, 4, 8, StoreMask.Floor }) TestAssert.Eq(StoreMask.Clean((byte)ground), (byte)0, "A ground of " + ground + " is clear");
            TestAssert.That(StoreMask.Clean(StoreMask.Floor + 1) >= 1, "Just over the floor is a trace of ink");
            TestAssert.Eq(StoreMask.Clean(255), (byte)255, "A solid line stays solid");
            byte last = 0;
            for (int a = 0; a <= 255; a++) { byte c = StoreMask.Clean((byte)a); TestAssert.That(c >= last, "The floor never lowers a brighter pixel below a dimmer one: " + a); last = c; }

            // A picture the way the game ships it: DXT-ish ground (1, 2 and 4 in a pattern, opaque), one white line. Every ground pixel comes out with alpha 0 (the box has no tint), the line out solid.
            const int w = 16, h = 8;
            var px = new byte[w * h * 4];
            byte[] ground3 = { 1, 2, 4 };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    byte v = (y == 4 && x >= 2 && x <= 13) ? (byte)255 : ground3[(x * 7 + y * 13) % 3];
                    px[i] = px[i + 1] = px[i + 2] = v;
                    px[i + 3] = 255;
                }
            TestAssert.That(StoreMask.Convert(px, w, h, out ArtBounds ink), "The line is ink");
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte a = px[(y * w + x) * 4 + 3];
                    bool line = y == 4 && x >= 2 && x <= 13;
                    TestAssert.That(line ? a == 255 : a == 0, "Alpha at " + x + ", " + y + ": " + a + (line ? " (the line)" : " (the ground, which must be clear)"));
                }
            TestAssert.Near(ink.X0, 2f / w, "The ink box is the line's");
        }

        private static void AlphaRule()
        {
            TestAssert.Eq(StoreMask.Alpha(0, 0, 0, 255), (byte)0, "The opaque black ground is clear");
            TestAssert.Eq(StoreMask.Alpha(255, 255, 255, 255), (byte)255, "A white line is solid");
            TestAssert.Eq(StoreMask.Alpha(0, 200, 0, 255), (byte)200, "The brightest channel is the alpha");
            TestAssert.Eq(StoreMask.Alpha(60, 30, 90, 255), (byte)90, "...whichever it is");
            TestAssert.Eq(StoreMask.Alpha(255, 255, 255, 0), (byte)0, "A transparent pixel is clear whatever its colour");
            TestAssert.Eq(StoreMask.Alpha(255, 255, 255, 128), (byte)128, "A half transparent white is half solid (source alpha times brightness)");
            TestAssert.Eq(StoreMask.Alpha(128, 0, 0, 128), (byte)64, "...and the two multiply");
            TestAssert.That(StoreMask.InkShare == 0.1f && StoreMask.MaxSide == 1024, "Ink is above 10 % alpha; the biggest picture baked is 1024 a side");
        }

        /// <summary>A 6 x 4 picture, black, with a white 2 x 1 line (x 1..2, the second row from the TOP) and a grey pixel too faint to be ink.</summary>
        private static void Convert()
        {
            const int w = 6, h = 4;
            var px = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i += 4) px[i + 3] = 255;
            void Put(int x, int topRow, byte v)
            {
                int i = ((h - 1 - topRow) * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = v;
            }
            Put(1, 1, 255);
            Put(2, 1, 255);
            Put(5, 3, 20);      // 20 is over the ground floor (12) and under the ink share (25): a faint trace
            TestAssert.That(StoreMask.Convert(px, w, h, out ArtBounds ink), "A picture with a line has ink");
            TestAssert.Near(ink.X0, 1f / 6f, "The ink starts at the line's left");
            TestAssert.Near(ink.X1, 3f / 6f, "...ends after its right pixel (the faint one is not ink)");
            TestAssert.Near(ink.Y0, 1f / 4f, "...y down: the line is the second row from the top");
            TestAssert.Near(ink.Y1, 2f / 4f, "...and its bottom is the row's foot");
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = ((h - 1 - y) * w + x) * 4;
                    TestAssert.That(px[i] == 255 && px[i + 1] == 255 && px[i + 2] == 255, "Every pixel is white (the tint is the colour's): " + x + ", " + y);
                    byte want = y == 1 && (x == 1 || x == 2) ? (byte)255 : x == 5 && y == 3 ? StoreMask.Clean(20) : (byte)0;
                    TestAssert.Eq(px[i + 3], want, "Alpha at " + x + ", " + y + ": near 1 on the line, 0 on the ground");
                }
        }

        private static void NoInk()
        {
            var px = new byte[4 * 4 * 4];
            for (int i = 0; i < px.Length; i += 4) px[i + 3] = 255;
            TestAssert.That(!StoreMask.Convert(px, 4, 4, out ArtBounds ink), "A picture with nothing in it has no ink");
            TestAssert.That(ink.X0 == 0f && ink.Y0 == 0f && ink.X1 == 1f && ink.Y1 == 1f, "...and its box is the whole picture");
        }

        private sealed class Thing
        {
            public readonly string Name;
            public Thing(string name) { Name = name; }
        }

        /// <summary>At most 32; a lookup counts as a use; the one used longest ago goes first and comes back to its owner; Clear hands over everything.</summary>
        private static void Cache()
        {
            var cache = new StoreMaskCache<Thing>();
            TestAssert.Eq(StoreMaskCache<Thing>.Capacity, 32, "Thirty-two pictures");
            for (int i = 0; i < 32; i++) TestAssert.That(cache.Add(1000 + i, new Thing("t" + i)) == null, "Room for " + i);
            TestAssert.Eq(cache.Count, 32, "Full");
            TestAssert.That(cache.TryGet(1000, out Thing first) && first.Name == "t0", "A lookup finds it");
            Thing pushed = cache.Add(2000, new Thing("new"));
            TestAssert.That(pushed != null && pushed.Name == "t1", "The 33rd pushes out the one used longest ago (t0 was just looked up, so t1)");
            TestAssert.Eq(cache.Count, 32, "...and the count stays 32");
            TestAssert.That(!cache.TryGet(1001, out _) && cache.TryGet(1000, out _) && cache.TryGet(2000, out _), "The pushed one is gone, the used and the new stay");
            Thing replaced = cache.Add(2000, new Thing("again"));
            TestAssert.That(replaced != null && replaced.Name == "new" && cache.Count == 32, "The same key replaces its entry and hands the old one back");
            for (int i = 0; i < 100; i++) cache.Add(5000 + i, new Thing("x" + i));
            TestAssert.Eq(cache.Count, 32, "A hundred more and still 32");
            int released = 0;
            cache.Clear(t => released++);
            TestAssert.That(released == 32 && cache.Count == 0 && !cache.TryGet(5099, out _), "Clear hands every entry over and empties it");
            TestAssert.That(cache.Add(1, new Thing("after")) == null && cache.Count == 1, "It is usable again");
        }
    }
}
