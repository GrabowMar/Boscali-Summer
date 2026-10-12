using System;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

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
            ClipOut();
            RadarTags();
            CallLine();
            FreeSpot();
            RadarCluster();
        }

        /// <summary>The call box's second line (the nearest radar's name and range in ten characters): the whole words that fit, the range whole, never an ellipsis (in game "HYPE... 37KM" and "MSV ... 19KM" read as two different cuts and the game's ellipsis glyph is wide).</summary>
        private static void CallLine()
        {
            string Line(string name, float metres) => ScopeStr.Distance(name, metres, AvUnits.Metric, 10, true).ToString();
            TestAssert.That(Line("HYPERION", 37000f) == "HYPER 37KM", "A first word too long for its room is cut, with no ellipsis: " + Line("HYPERION", 37000f));
            TestAssert.That(Line("MSV RADAR", 19000f) == "MSV 19KM", "A cut that falls on a space keeps the whole words and no blank before the range: " + Line("MSV RADAR", 19000f));
            TestAssert.That(Line("T9K41", 18000f) == "T9K41 18KM" && Line("SA-19", 11000f) == "SA-19 11KM" && Line("t9k41", 18000f) == "T9K41 18KM", "A name that fits stays whole, upper case: " + Line("T9K41", 18000f));
            TestAssert.That(Line("SHARD CLASS", 45000f) == "SHARD 45KM" && Line("ZSU", 4100f) == "ZSU 4.1KM", "SHARD CLASS gives its second word, ZSU keeps its whole: " + Line("SHARD CLASS", 45000f) + " / " + Line("ZSU", 4100f));
            TestAssert.That(Line(null, 37000f) == "37KM" && Line("  ", 37000f) == "37KM" && ScopeStr.Distance("SA-19", 123000f, AvUnits.Metric, 8, true).ToString() == "123KM", "No name, or no room for three characters of one: the range alone");
            foreach (string name in new[] { "HYPERION CLASS CARRIER", "SHARD CLASS CORVETTE", "T9K41 BOLTSTRIKE", "MSV RADAR", "HLT RADAR TRUCK", "FS-20 VORTEX", "A" })
                foreach (float km in new[] { 0.5f, 4.1f, 19f, 37f, 99f, 120f })
                {
                    string line = Line(name, km * 1000f);
                    TestAssert.That(line.Length <= 10 && !line.Contains("…") && !line.Contains(" ... ") && line.EndsWith("M") && !line.StartsWith(" "), "[" + name + ", " + km + " km] reads [" + line + "]: at most ten characters, no ellipsis, the range whole");
                }
        }

        /// <summary>
        /// THREAT's radar tags with the radars the game's mission put round the player (read off the in-game captures: three of a carrier group at about 185 degrees, a SAM site at about 212 and one more at 229, the longest names
        /// 22 characters): every tag stays inside the scope and clear of the page's own boxes, of the other tags by <see cref="KeepOut.Gap"/> (two tags touched in game) and of every spoke (a spoke ran through the first
        /// letter of HYPERION and through "HLT RADAR"), where the page's tags are the placement's only business. The first scene is the cruise capture, two radars on one bearing.
        /// </summary>
        private static void RadarCluster()
        {
            const float CX = 220f, CY = 164f, R = 130f, H = 31f;
            var scenes = new[]
            {
                (names: new[] { "SHARD CLASS CORVETTE", "HYPERION CLASS CARRIER" }, bearing: new[] { 186f, 187f }, km: new[] { 37f, 37f }),
                (names: new[] { "SHARD CLASS CORVETTE", "HYPERION CLASS CARRIER", "HLT RADAR TRUCK", "MSV RADAR", "T9K41 BOLTSTRIKE", "T9K41 BOLTSTRIKE", "T9K41 BOLTSTRIKE" },
                    bearing: new[] { 185f, 186f, 187f, 211f, 212.5f, 214f, 229f }, km: new[] { 45f, 45f, 38f, 18.5f, 18f, 18f, 20f }),
            };
            foreach (var scene in scenes)
            {
                int n = scene.names.Length;
                var fixedBoxes = new[] { new[] { CX - 13f, CY - 13f, 26f, 26f }, new[] { 328f, 30f, 98f, 54f }, new[] { 328f, 230f, 98f, 66f }, new[] { 3f, 34f, 76f, 30f } };
                var keep = new KeepOut();
                foreach (float[] b in fixedBoxes) keep.Add(b[0], b[1], b[2], b[3]);
                var rings = new[] { (5, 2), (10, 3), (20, 3) };      // the ring labels "5S" "10S" "20S" at 152 degrees, as ThreatPage.Rings keeps them out
                foreach (var ring in rings)
                {
                    float r = R * (float)Math.Sqrt(ring.Item1 / 20f), a = 152f * (float)Math.PI / 180f;
                    keep.Add(CX + (float)Math.Sin(a) * r - 4f, CY - (float)Math.Cos(a) * r - 8f, (float)Math.Round(ring.Item2 * 7.8f) + 16f, 18f);
                }
                var spokes = new float[4 * n];
                for (int i = 0; i < n; i++)
                {
                    float a = scene.bearing[i] * (float)Math.PI / 180f;
                    spokes[4 * i] = CX + (float)Math.Sin(a) * R; spokes[4 * i + 1] = CY - (float)Math.Cos(a) * R;
                    spokes[4 * i + 2] = CX + (float)Math.Sin(a) * R * 0.56f; spokes[4 * i + 3] = CY - (float)Math.Cos(a) * R * 0.56f;
                }
                var x = new float[n]; var y = new float[n]; var w = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int chars = Math.Max(Tag(scene.names[i]).Length, ScopeStr.Distance("", scene.km[i] * 1000f, AvUnits.Metric, 8).Length);
                    w[i] = chars * 7.8f + 8f;
                    keep.PlaceRadar(CX, CY, R, scene.bearing[i], w[i], H, spokes, 2f, 436f, 30f, 296f, out x[i], out y[i]);
                }
                for (int i = 0; i < n; i++)
                {
                    string tag = n + " radars, [" + scene.names[i] + "] at " + x[i] + ", " + y[i];
                    TestAssert.That(x[i] >= 2f && x[i] + w[i] <= 436f && y[i] >= 30f && y[i] + H <= 296f, "Inside the scope: " + tag);
                    foreach (float[] b in fixedBoxes)
                        TestAssert.That(KeepOut.Overlap(x[i], y[i], w[i], H, b[0], b[1], b[2], b[3]) == 0f, "Clear of the page's own boxes: " + tag);
                    for (int j = 0; j < n; j++)
                        if (j != i) TestAssert.That(KeepOut.Overlap(x[i] - KeepOut.Gap, y[i] - KeepOut.Gap, w[i] + 2f * KeepOut.Gap, H + 2f * KeepOut.Gap, x[j], y[j], w[j], H) == 0f, "Clear of tag " + j + " by the gap: " + tag + " and " + x[j] + ", " + y[j]);
                    for (int s = 0; s < n; s++)
                        TestAssert.That(KeepOut.SegHits(x[i], y[i], w[i], H, spokes[4 * s], spokes[4 * s + 1], spokes[4 * s + 2], spokes[4 * s + 3], 24) == 0, "No spoke (" + scene.bearing[s] + " degrees) through the tag: " + tag);
                }
            }
        }

        private static string Tag(string name)
        {
            var into = new char[ThreatMath.TagChars];
            return new string(into, 0, ThreatMath.TagName(name, into));
        }

        /// <summary>A radar's tag on the THREAT scope: the whole name up to 12 characters, else the whole words that fit (the game's names run to 22), never an ellipsis.</summary>
        private static void RadarTags()
        {
            TestAssert.That(Tag("SA-19") == "SA-19" && Tag("ZSU") == "ZSU" && Tag("FS-20 VORTEX") == "FS-20 VORTEX" && Tag("MSV RADAR") == "MSV RADAR", "A name of 12 characters or fewer is the tag");
            TestAssert.That(Tag("HYPERION CLASS CARRIER") == "HYPERION", "HYPERION CLASS CARRIER (seen in game) reads HYPERION, whole words only: " + Tag("HYPERION CLASS CARRIER"));
            TestAssert.That(Tag("SHARD CLASS CORVETTE") == "SHARD CLASS" && Tag("T9K41 BOLTSTRIKE") == "T9K41" && Tag("HLT RADAR TRUCK") == "HLT RADAR", "...as do the others the game's names gave: " + Tag("SHARD CLASS CORVETTE") + ", " + Tag("T9K41 BOLTSTRIKE") + ", " + Tag("HLT RADAR TRUCK"));
            TestAssert.That(Tag("ABCDEFGHIJKL MNOP") == "ABCDEFGHIJKL", "A word that ends exactly at the limit is whole");
            TestAssert.That(Tag("SUPERLONGNAME123456") == "SUPERLONGNAM", "A first word that is itself too long is cut at the limit, with no ellipsis: " + Tag("SUPERLONGNAME123456"));
            TestAssert.That(Tag("  PADDED  ") == "PADDED" && Tag(null) == "" && Tag("") == "" && Tag("   ") == "", "Blanks are trimmed; no name, no tag word");
        }

        /// <summary>The THREAT scope's radar tags when their few spots all sit on others (the real names put three at one bearing, seen in game as text printed over text): each finds a spot of its own.</summary>
        private static void FreeSpot()
        {
            const float W = 117f, H = 31f;
            var keep = new KeepOut();
            keep.Add(328f, 30f, 98f, 54f);      // the call box
            keep.Add(328f, 230f, 98f, 66f);     // the time bars
            keep.Add(3f, 34f, 76f, 30f);        // TTI RINGS and HDG UP
            float x = 150f, y = 255f;
            keep.Free(W, H, default, 2f, 436f, 30f, 296f, ref x, ref y);
            TestAssert.That(x == 150f && y == 255f, "A spot that sits on nothing stays where it is: " + x + ", " + y);
            keep.Add(x, y, W, H);

            // Four more, each asked for at the very spot the first one took: every one ends up clear of all the others and of the page's own boxes, inside the scope.
            var at = new float[10];
            at[0] = x; at[1] = y;
            for (int i = 1; i < 5; i++)
            {
                x = 150f; y = 255f;
                keep.Free(W, H, default, 2f, 436f, 30f, 296f, ref x, ref y);
                TestAssert.That(KeepOut.Overlap(x, y, W, H, 328f, 30f, 98f, 54f) == 0f && KeepOut.Overlap(x, y, W, H, 328f, 230f, 98f, 66f) == 0f && KeepOut.Overlap(x, y, W, H, 3f, 34f, 76f, 30f) == 0f, "Tag " + i + " clear of the page's boxes: " + x + ", " + y);
                for (int j = 0; j < i; j++) TestAssert.That(KeepOut.Overlap(x, y, W, H, at[2 * j], at[2 * j + 1], W, H) == 0f, "Tag " + i + " clear of tag " + j + ": " + x + ", " + y + " on " + at[2 * j] + ", " + at[2 * j + 1]);
                TestAssert.That(x >= 2f && x + W <= 436f && y >= 30f && y + H <= 296f, "...and inside the scope: " + x + ", " + y);
                keep.Add(x, y, W, H);
                at[2 * i] = x; at[2 * i + 1] = y;
            }

            // The nearest free spot: a tag on another moves to a near free one, not across the scope.
            var one = new KeepOut();
            one.Add(100f, 100f, W, H);
            x = 110f; y = 112f;
            one.Free(W, H, default, 2f, 436f, 30f, 296f, ref x, ref y);
            TestAssert.That(KeepOut.Overlap(x, y, W, H, 100f, 100f, W, H) == 0f && Math.Abs(x - 110f) + Math.Abs(y - 112f) < 100f, "A tag on another moves to a near free spot: " + x + ", " + y);

            // Lines count too: a tag sitting on the spokes moves to a spot they miss, when one is free of labels (in the first render of the long names the spokes of six radars ran through the tags).
            var lines = new KeepOut();
            var spokes = new[] { 200f, 294f, 200f, 237f, 215f, 294f, 215f, 237f, 230f, 294f, 230f, 237f };
            x = 150f; y = 255f;
            lines.Free(W, H, spokes, 2f, 436f, 30f, 296f, ref x, ref y);
            TestAssert.That(KeepOut.SegHits(x, y, W, H, 200f, 294f, 200f, 237f, 6) == 0 && KeepOut.SegHits(x, y, W, H, 230f, 294f, 230f, 237f, 6) == 0, "A tag that sits on the spokes moves off them: " + x + ", " + y);
            x = 2f; y = 255f;
            lines.Free(W, H, spokes, 2f, 436f, 30f, 296f, ref x, ref y);
            TestAssert.That(x == 2f && y == 255f, "...one clear of them stays: " + x + ", " + y);

            // No room anywhere: it does not throw and it leaves the tag no worse than it was.
            var full = new KeepOut();
            full.Add(0f, 0f, 440f, 300f);
            x = 200f; y = 100f;
            full.Free(W, H, default, 2f, 436f, 30f, 296f, ref x, ref y);
            TestAssert.That(full.OverlapCost(x, y, W, H) <= full.OverlapCost(200f, 100f, W, H), "A scope with no free spot leaves the tag no worse than it was");
        }

        /// <summary>MISSILE CAM cuts its ground picture round the words (a hole in a line): none of a line inside, all of one that misses, the piece before and the piece after for one that crosses.</summary>
        private static void ClipOut()
        {
            var p = new float[8];
            bool Is(int at, float want) => Math.Abs(p[at] - want) < 0.01f;
            TestAssert.Eq(MissileCamMath.ClipOut(0f, 0f, 100f, 0f, 40f, 10f, 60f, 30f, p), -1, "A line that misses the hole is left alone");
            int n = MissileCamMath.ClipOut(0f, 20f, 100f, 20f, 40f, 10f, 60f, 30f, p);
            TestAssert.That(n == 2 && Is(0, 0f) && Is(2, 40f) && Is(4, 60f) && Is(6, 100f) && Is(1, 20f) && Is(7, 20f), "A line through the hole is two pieces, before it and after it, in order");
            n = MissileCamMath.ClipOut(100f, 20f, 0f, 20f, 40f, 10f, 60f, 30f, p);
            TestAssert.That(n == 2 && Is(0, 100f) && Is(2, 60f) && Is(4, 40f) && Is(6, 0f), "...in the line's own direction");
            n = MissileCamMath.ClipOut(0f, 20f, 50f, 20f, 40f, 10f, 60f, 30f, p);
            TestAssert.That(n == 1 && Is(0, 0f) && Is(2, 40f), "A line that ends in the hole keeps the part before it");
            n = MissileCamMath.ClipOut(50f, 20f, 100f, 20f, 40f, 10f, 60f, 30f, p);
            TestAssert.That(n == 1 && Is(0, 60f) && Is(2, 100f), "...one that starts in it, the part after");
            TestAssert.Eq(MissileCamMath.ClipOut(45f, 15f, 55f, 25f, 40f, 10f, 60f, 30f, p), 0, "A line wholly inside has nothing left");
            n = MissileCamMath.ClipOut(0f, 0f, 100f, 100f, 40f, 40f, 60f, 60f, p);
            TestAssert.That(n == 2 && Is(2, 40f) && Is(3, 40f) && Is(4, 60f) && Is(5, 60f), "A diagonal is cut where it enters and leaves");
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

            // A leader asked for once every label is down (THREAT draws the radars' leaders last): the label it belongs to is named, not "the last one", and a label placed after it counts.
            var late = new KeepOut();
            late.Add(200f, 100f, 60f, 17f);      // the tag the leader belongs to
            TestAssert.That(!late.LeaderCrosses(230f, 200f, 230f, 117f, 230f, 200f, 0), "A leader into its own tag crosses nothing");
            late.Add(210f, 140f, 40f, 17f);      // a tag placed after it, on its way
            TestAssert.That(late.LeaderCrosses(230f, 200f, 230f, 117f, 230f, 200f, 0), "...until a tag placed after it stands on its way (drawn as its tag was placed, the leader ran through it: HLT R|ADAR in game)");
            TestAssert.That(!late.LeaderCrosses(230f, 200f, 230f, 160f, 230f, 200f, 0), "...and one that stops short of it is clear");
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
