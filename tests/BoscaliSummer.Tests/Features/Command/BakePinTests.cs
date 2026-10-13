using BoscaliSummer.Modules.Command.Runtime;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// Pins the pixels of one fixed front (friendly, hostile, a contested pocket, unheld ground).
    /// The bake is hand-tuned for speed; any change to what it draws must show up here.
    /// </summary>
    internal static class BakePinTests
    {
        public static void Run()
        {
            var grid = new TacticalSectorGrid(2000f, 100000f, 60000f);
            for (int pass = 0; pass < 6; pass++)
            {
                grid.Clear();
                for (float z = -25000f; z <= 25000f; z += 3000f)
                {
                    for (float x = -45000f; x <= -5000f; x += 3000f) grid.AddTroopPresence(x, z, 8f, false, 6000f);
                    for (float x = 5000f + (z > 0f ? 4000f : 0f); x <= 30000f; x += 3000f) grid.AddTroopPresence(x, z, 8f, true, 6000f);
                }
                grid.AddTroopPresence(0f, 0f, 8f, false, 6000f);
                grid.AddTroopPresence(0f, 0f, 8f, true, 6000f);
                grid.EvaluateSectors(30f);
            }

            TestAssert.That(grid.ContestedSectorCount > 0 && grid.NeutralSectorCount > 0 && grid.FriendlySectorCount > 0 && grid.HostileSectorCount > 0,
                "the pinned front has every kind of ground");
            ulong wide = Fnv(grid.BakeTexture(320, 200, true, 0.5f));
            // Re-pinned 2026-10-13: an unheld square closed in by held ground is now tinted through (it drew a ring on the front).
            TestAssert.That(wide == 0xD26BC7A0D0F305F5UL, "the 320x200 bake keeps its pixels, got 0x" + wide.ToString("X"));
            ulong tall = Fnv(grid.BakeTexture(200, 320, true, 0.5f));
            TestAssert.That(tall == 0xBF29FFDBC12DEF54UL, "the 200x320 bake keeps its pixels, got 0x" + tall.ToString("X"));

            // The pixel buffer is reused: a bake after a cleared bake must paint the same picture.
            TestAssert.That(Fnv(grid.BakeTexture(320, 200, false, 0.5f)) == Fnv(new Color32[320 * 200]), "a hidden overlay bakes transparent");
            TestAssert.That(Fnv(grid.BakeTexture(320, 200, true, 0.5f)) == wide, "a bake after a cleared bake paints the same pixels");

            // A reused buffer still holds the last picture, a fresh one is blank: baking over the old
            // picture must leave nothing of it. The twin is also what the map overlay bakes on a worker.
            grid.ResetAll();
            grid.AddTroopPresence(-20000f, 10000f, 8f, false, 6000f);
            grid.AddTroopPresence(-14000f, 10000f, 8f, true, 6000f);
            grid.EvaluateSectors(30f);
            TacticalSectorGrid twin = grid.NewBakeTwin();
            TestAssert.That(twin.SharesBakeLayout(grid), "the bake twin has the grid's layout");
            twin.CopyBakeStateFrom(grid);
            TestAssert.That(Fnv(grid.BakeTexture(320, 200, true, 0.5f)) == Fnv(twin.BakeTexture(320, 200, true, 0.5f)),
                "a re-bake over an old picture, and a copy's bake, paint the same pixels");
        }

        private static ulong Fnv(Color32[] pixels)
        {
            ulong hash = 14695981039346656037UL;
            foreach (Color32 pixel in pixels)
            {
                hash = (hash ^ pixel.r) * 1099511628211UL;
                hash = (hash ^ pixel.g) * 1099511628211UL;
                hash = (hash ^ pixel.b) * 1099511628211UL;
                hash = (hash ^ pixel.a) * 1099511628211UL;
            }
            return hash;
        }
    }
}
