using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The scope's label placement (<see cref="KeepOut"/>): THREAT's notch wedge keep-out and its leader test (LAYOUT.md 12.2), and the SHOT timeline's chip names (12.3).</summary>
    internal static class KeepOutTests
    {
        public static void Run()
        {
            Wedge();
            Leader();
            ChipNames();
        }

        private static void Wedge()
        {
            const float W = 60f, H = 17f;
            var keep = new KeepOut();
            keep.AddWedge(251f, 136f, 113f, 56f);      // the notch wedge's box on the 3 o'clock side (its bounding box grown by 8)
            keep.Add(310f, 154f, 20f, 20f);            // the missile's own box: its marker sits in the wedge
            keep.Place(LabelSpots.Threat, 320f, 164f, W, H, default, 2f, 436f, 32f, 292f, out float x, out float y);
            TestAssert.That(KeepOut.Overlap(x, y, W, H, 251f, 136f, 113f, 56f) == 0f, "A tag whose marker is in the notch wedge sits off the wedge's box, not on its hatch: " + x + ", " + y);
            TestAssert.That(y + H <= 136f || y >= 192f, "...above or below it: " + y);
            TestAssert.That(keep.Count == 3, "...and it is registered as placed");

            var free = new KeepOut();
            free.AddWedge(251f, 136f, 113f, 56f);
            free.Place(LabelSpots.Threat, 120f, 220f, W, H, default, 2f, 436f, 32f, 292f, out x, out y);
            TestAssert.That(x == 90f && y == 190f, "A marker far from the wedge takes the spot nearest it, centred above (0.3 a unit of distance): " + x + ", " + y);
        }

        private static void Leader()
        {
            var keep = new KeepOut();
            keep.AddWedge(251f, 136f, 113f, 56f);
            keep.Add(100f, 100f, 40f, 17f);            // another label
            keep.Add(300f, 160f, 20f, 20f);            // the marker's own box, centred on it
            keep.Add(200f, 100f, 60f, 17f);            // the label just placed
            TestAssert.That(keep.LeaderCrosses(110f, 90f, 130f, 120f, 310f, 170f), "A leader through another label is refused");
            TestAssert.That(!keep.LeaderCrosses(150f, 90f, 160f, 120f, 310f, 170f), "...one that runs clear of every label is drawn");
            TestAssert.That(!keep.LeaderCrosses(260f, 150f, 330f, 170f, 310f, 170f), "...the wedge's box and the marker's own box are not in the way");
            TestAssert.That(!keep.LeaderCrosses(230f, 90f, 230f, 130f, 310f, 170f), "...nor is the label the leader belongs to");
        }

        private static void ChipNames()
        {
            TestAssert.That(Chip("SKYWY") == "SKYWY" && Chip("DART") == "DART", "A name of five characters or fewer is the chip's name");
            TestAssert.That(Chip("GBU-12") == "GBU12" && Chip("AGM 48") == "AGM48", "Hyphens and spaces are left out, so GBU-12 fits");
            TestAssert.That(Chip("SIDEWINDER") == "SIDEW", "A longer name is cut to five characters, with no ellipsis");
            TestAssert.That(Chip(null) == "" && Chip("") == "", "No name, no chip word");
        }

        private static string Chip(string name)
        {
            var into = new char[ShotMath.ChipChars];
            return new string(into, 0, ShotMath.ChipName(name, into));
        }
    }
}
