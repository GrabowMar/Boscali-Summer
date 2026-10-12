using System;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>Tag plates stay clear of the icon they name and of each other (annex 05 P4, placer tests).</summary>
    internal static class StrTagPlacerTests
    {
        private const float NativeIconHalf = 17f;   // the game's 34 px airbase hit square

        public static void Run()
        {
            RectanglesOverlapOnlyWhenTheyShareArea();
            CandidatesSitDiagonallyAtTheClearDistance();
            ALoneAnchorTakesTheFirstDiagonal();
            APlateAvoidsAnotherAnchorsIcon();
            ACornerAnchorStaysInsideTheViewport();
            FortyAnchorsOnOneSpotNeverStackWhenARoomIsFree();
            RandomAnchorsNeverMeetAnyIconSquare();
            AnUnplaceablePlateFallsBackToTheFirstDiagonal();
            ANonFiniteAnchorGetsNothingAndBlocksNothing();
        }

        private static void RectanglesOverlapOnlyWhenTheyShareArea()
        {
            var a = new RectF(0f, 0f, 10f, 10f);
            TestAssert.That(a.Overlaps(new RectF(5f, 5f, 10f, 10f)) && new RectF(5f, 5f, 10f, 10f).Overlaps(a), "rectangles that share area overlap, both ways");
            TestAssert.That(!a.Overlaps(new RectF(10f, 0f, 5f, 5f)) && !a.Overlaps(new RectF(0f, 10f, 5f, 5f)), "rectangles that only touch an edge do not overlap");
            TestAssert.That(a.Overlaps(new RectF(2f, 2f, 3f, 3f)), "a rectangle inside another overlaps it");
            TestAssert.That(a.Contains(new RectF(2f, 2f, 3f, 3f)) && a.Contains(a) && !a.Contains(new RectF(8f, 8f, 3f, 3f)), "contains means entirely inside, edges allowed");
        }

        private static void CandidatesSitDiagonallyAtTheClearDistance()
        {
            RectF ne = StrTagPlacer.Candidate(0, 100f, 100f, 60f, 20f), nw = StrTagPlacer.Candidate(1, 100f, 100f, 60f, 20f);
            RectF se = StrTagPlacer.Candidate(2, 100f, 100f, 60f, 20f), sw = StrTagPlacer.Candidate(3, 100f, 100f, 60f, 20f);
            TestAssert.That(ne.X == 100f + StrTagPlacer.IconClear && ne.Y == 100f + StrTagPlacer.IconClear, "candidate 0 is north-east with its near corner IconClear away on both axes");
            TestAssert.That(nw.X + nw.W == 100f - StrTagPlacer.IconClear && nw.Y == 100f + StrTagPlacer.IconClear, "candidate 1 is north-west");
            TestAssert.That(se.X == 100f + StrTagPlacer.IconClear && se.Y + se.H == 100f - StrTagPlacer.IconClear, "candidate 2 is south-east");
            TestAssert.That(sw.X + sw.W == 100f - StrTagPlacer.IconClear && sw.Y + sw.H == 100f - StrTagPlacer.IconClear, "candidate 3 is south-west");
            RectF far = StrTagPlacer.Candidate(4, 100f, 100f, 60f, 20f);
            TestAssert.That(far.X > ne.X && far.Y > ne.Y && far.W == 60f, "candidates 4 to 7 repeat the diagonals one ring further out");
            TestAssert.That(StrTagPlacer.Candidates == 8, "eight candidates");
        }

        private static void ALoneAnchorTakesTheFirstDiagonal()
        {
            var placed = new RectF[1];
            StrTagPlacer.Place(new RectF(0f, 0f, 800f, 600f), new[] { 400f }, new[] { 300f }, new[] { 90f }, new[] { 24f }, 1, placed);
            RectF want = StrTagPlacer.Candidate(0, 400f, 300f, 90f, 24f);
            TestAssert.That(placed[0].X == want.X && placed[0].Y == want.Y && placed[0].W == 90f && placed[0].H == 24f, "with nothing in the way the plate goes north-east");
        }

        private static void APlateAvoidsAnotherAnchorsIcon()
        {
            // Anchor 1 sits inside the first diagonal of anchor 0: that candidate would cover anchor 1's icon, so the plate must go elsewhere.
            var vp = new RectF(0f, 0f, 800f, 600f);
            var ax = new[] { 100f, 150f }; var ay = new[] { 100f, 130f };
            var placed = new RectF[2];
            StrTagPlacer.Place(vp, ax, ay, new[] { 100f, 100f }, new[] { 20f, 20f }, 2, placed);
            RectF first = StrTagPlacer.Candidate(0, 100f, 100f, 100f, 20f);
            TestAssert.That(first.Overlaps(new RectF(150f - NativeIconHalf, 130f - NativeIconHalf, 2f * NativeIconHalf, 2f * NativeIconHalf)), "the setup puts anchor 1 under anchor 0's first diagonal");
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    TestAssert.That(!placed[i].Overlaps(new RectF(ax[j] - NativeIconHalf, ay[j] - NativeIconHalf, 2f * NativeIconHalf, 2f * NativeIconHalf)),
                        "plate " + i + " keeps off the icon of anchor " + j);
            TestAssert.That(placed[0].X != first.X || placed[0].Y != first.Y, "so plate 0 left its first diagonal");
        }

        private static void ACornerAnchorStaysInsideTheViewport()
        {
            var vp = new RectF(0f, 0f, 800f, 600f);
            foreach (var corner in new[] { new[] { 790f, 590f }, new[] { 5f, 5f }, new[] { 790f, 5f }, new[] { 5f, 590f } })
            {
                var placed = new RectF[1];
                StrTagPlacer.Place(vp, new[] { corner[0] }, new[] { corner[1] }, new[] { 120f }, new[] { 30f }, 1, placed);
                TestAssert.That(vp.Contains(placed[0]), "a plate for an anchor in a corner still lies inside the viewport");
                TestAssert.That(!placed[0].Overlaps(new RectF(corner[0] - NativeIconHalf, corner[1] - NativeIconHalf, 2f * NativeIconHalf, 2f * NativeIconHalf)), "and clear of the icon");
            }
        }

        private static bool Free(in RectF vp, RectF r, RectF[] placed, int count, float[] ax, float[] ay, int n)
        {
            if (!vp.Contains(r)) return false;
            for (int j = 0; j < count; j++) if (r.Overlaps(placed[j])) return false;
            for (int j = 0; j < n; j++)
                if (r.Overlaps(new RectF(ax[j] - StrTagPlacer.IconClear, ay[j] - StrTagPlacer.IconClear, 2f * StrTagPlacer.IconClear, 2f * StrTagPlacer.IconClear))) return false;
            return true;
        }

        private static void FortyAnchorsOnOneSpotNeverStackWhenARoomIsFree()
        {
            var vp = new RectF(0f, 0f, 1600f, 900f);
            const int n = 40;
            var ax = new float[n]; var ay = new float[n]; var w = new float[n]; var h = new float[n]; var placed = new RectF[n];
            for (int i = 0; i < n; i++) { ax[i] = 800f; ay[i] = 450f; w[i] = 60f + (i % 5) * 10f; h[i] = 18f + (i % 3) * 4f; }
            StrTagPlacer.Place(vp, ax, ay, w, h, n, placed);

            for (int i = 0; i < n; i++)
            {
                bool anyFree = false; RectF firstFree = default;
                for (int c = 0; c < StrTagPlacer.Candidates && !anyFree; c++)
                {
                    RectF r = StrTagPlacer.Candidate(c, ax[i], ay[i], w[i], h[i]);
                    if (Free(vp, r, placed, i, ax, ay, n)) { anyFree = true; firstFree = r; }
                }
                if (anyFree)
                    TestAssert.That(placed[i].X == firstFree.X && placed[i].Y == firstFree.Y, "plate " + i + " takes the first free candidate");
                else
                    TestAssert.That(vp.Contains(placed[i]), "plate " + i + " has no free room and is still inside the viewport");
            }
            for (int i = 1; i < 4; i++)
                for (int j = 0; j < i; j++)
                    TestAssert.That(!placed[i].Overlaps(placed[j]), "the first four plates fill the four diagonals without touching");
        }

        private static void RandomAnchorsNeverMeetAnyIconSquare()
        {
            var vp = new RectF(0f, 0f, 1600f, 900f);
            var rng = new Random(20261012);

            // 200 lone anchors anywhere in the viewport: a free diagonal always exists, so the guarantee is unconditional.
            for (int t = 0; t < 200; t++)
            {
                float ax = 2f + (float)rng.NextDouble() * 1596f, ay = 2f + (float)rng.NextDouble() * 896f;
                float w = 60f + (float)rng.NextDouble() * 110f, h = 18f + (float)rng.NextDouble() * 16f;
                var placed = new RectF[1];
                StrTagPlacer.Place(vp, new[] { ax }, new[] { ay }, new[] { w }, new[] { h }, 1, placed);
                TestAssert.That(vp.Contains(placed[0]), "lone anchor " + t + " is placed inside the viewport");
                TestAssert.That(!placed[0].Overlaps(new RectF(ax - NativeIconHalf, ay - NativeIconHalf, 2f * NativeIconHalf, 2f * NativeIconHalf)),
                    "lone anchor " + t + " is placed clear of its own 34 px square");
            }

            // Crowds of six anchors at least 220 px apart: every plate meets no anchor square and no other plate.
            for (int b = 0; b < 25; b++)
            {
                const int per = 6;
                var ax = new float[per]; var ay = new float[per]; var w = new float[per]; var h = new float[per]; var placed = new RectF[per];
                for (int i = 0; i < per; i++)
                {
                    bool apart;
                    do
                    {
                        ax[i] = 10f + (float)rng.NextDouble() * 1580f; ay[i] = 10f + (float)rng.NextDouble() * 880f;
                        apart = true;
                        for (int j = 0; j < i && apart; j++) apart = Math.Abs(ax[i] - ax[j]) + Math.Abs(ay[i] - ay[j]) >= 220f;
                    } while (!apart);
                    w[i] = 60f + (float)rng.NextDouble() * 110f; h[i] = 18f + (float)rng.NextDouble() * 16f;
                }
                StrTagPlacer.Place(vp, ax, ay, w, h, per, placed);
                for (int i = 0; i < per; i++)
                {
                    TestAssert.That(vp.Contains(placed[i]), "crowd " + b + " plate " + i + " is inside the viewport");
                    for (int j = 0; j < per; j++)
                        TestAssert.That(!placed[i].Overlaps(new RectF(ax[j] - NativeIconHalf, ay[j] - NativeIconHalf, 2f * NativeIconHalf, 2f * NativeIconHalf)),
                            "crowd " + b + " plate " + i + " meets the 34 px icon square of anchor " + j);
                    for (int j = 0; j < i; j++)
                        TestAssert.That(!placed[i].Overlaps(placed[j]), "crowd " + b + " plate " + i + " covers plate " + j);
                }
            }
        }

        private static void AnUnplaceablePlateFallsBackToTheFirstDiagonal()
        {
            var vp = new RectF(0f, 0f, 400f, 300f);
            // A plate nearly as wide as the viewport fits nowhere clear of the icon: it takes diagonal 1 pulled into view.
            var placed = new RectF[1];
            StrTagPlacer.Place(vp, new[] { 200f }, new[] { 150f }, new[] { 380f }, new[] { 280f }, 1, placed);
            TestAssert.That(vp.Contains(placed[0]) && placed[0].W == 380f && placed[0].H == 280f, "the fallback plate keeps its size and is pulled inside the viewport");

            var wide = new RectF[1];
            StrTagPlacer.Place(vp, new[] { 100f }, new[] { 100f }, new[] { 150f }, new[] { 40f }, 1, wide);
            RectF first = StrTagPlacer.Candidate(0, 100f, 100f, 150f, 40f);
            TestAssert.That(wide[0].X == first.X && wide[0].Y == first.Y, "when diagonal 1 is free it is the one taken");

            var tooBig = new RectF[1];
            StrTagPlacer.Place(vp, new[] { 200f }, new[] { 150f }, new[] { 900f }, new[] { 20f }, 1, tooBig);
            TestAssert.That(tooBig[0].X == vp.X && tooBig[0].W == 900f, "a plate wider than the viewport is pinned to its left edge, never thrown away");
        }

        private static void ANonFiniteAnchorGetsNothingAndBlocksNothing()
        {
            var vp = new RectF(0f, 0f, 800f, 600f);
            var placed = new RectF[3];
            StrTagPlacer.Place(vp, new[] { 400f, float.NaN, 100f }, new[] { 300f, 200f, float.PositiveInfinity }, new[] { 80f, 80f, 80f }, new[] { 20f, 20f, 20f }, 3, placed);
            TestAssert.That(placed[1].W == 0f && placed[1].H == 0f && placed[2].W == 0f, "a plate with no usable anchor is empty");
            RectF want = StrTagPlacer.Candidate(0, 400f, 300f, 80f, 20f);
            TestAssert.That(placed[0].X == want.X && placed[0].Y == want.Y, "and it does not push a good plate off its first diagonal");
            StrTagPlacer.Place(vp, null, null, null, null, 3, placed);
            StrTagPlacer.Place(vp, new float[1], new float[1], new float[1], new float[1], 0, placed);
        }
    }
}
