using System;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Pure layout maths for the OPS MFD and map overlays: names, geometry, boards.</summary>
    internal static class OpsLayoutTests
    {
        public static void Run()
        {
            CheckTownNames();
            CheckBoardFit();
            CheckLabels();
            CheckShorten();
            CheckTimeline();
            CheckContrast();
        }

        private static void CheckTownNames()
        {
            TestAssert.That(
                PlaceNames.Town("City_1_Buildings", "NORTH RIDGE", "12/-3") == "NORTH RIDGE OUTSKIRTS",
                "a city-set name with no real word takes the nearest airbase");
            TestAssert.That(
                PlaceNames.Town("city_kersey (2)", "NORTH RIDGE", "12/-3") == "KERSEY",
                "a real word inside a city-set name is kept");
            TestAssert.That(
                PlaceNames.Town("City", null, "12/-3") == "TOWN 12/-3",
                "a bare city token with no airbase falls back to the grid");
            TestAssert.That(
                PlaceNames.Town("Set_4_Buildings", "", "3/8") == "TOWN 3/8",
                "digits plus CITY/BUILDING(S)/SET and an empty airbase fall back to the grid");
            TestAssert.That(PlaceNames.Clean("city_kersey (2)") == "CITY KERSEY", "clean strips the parenthetical and underscores");
            TestAssert.That(PlaceNames.Clean(null) == "", "clean of null is empty");
            TestAssert.That(PlaceNames.Clean(PlaceNames.Clean("A--B")) == PlaceNames.Clean("A--B"), "clean is idempotent");
        }

        private static void CheckBoardFit()
        {
            var xs = new float[12];
            var zs = new float[12];
            for (int i = 0; i < 10; i++)
            {
                xs[i] = 1000f + (i - 5) * 80f;
                zs[i] = 2000f + ((i % 3) - 1) * 60f;
            }
            xs[10] = -40000f;
            zs[10] = -40000f;
            xs[11] = 40000f;
            zs[11] = 40000f;
            BoardFrame frame = BoardFit.Fit(xs, zs, 12, 400f, 300f, 20f, 1000f, 10f);
            BoardFit.Project(frame, 1000f, 2000f, 400f, 300f, out float sx, out float sy);
            TestAssert.That(sx > 40f && sx < 360f && sy > 30f && sy < 270f, "the cluster is framed, not the outliers");
            BoardFit.Project(frame, xs[10], zs[10], 400f, 300f, out float ox, out float oy);
            TestAssert.That(ox < 0f || ox > 400f || oy < 0f || oy > 300f, "a far outlier may fall outside");

            BoardFit.Unproject(frame, sx, sy, 400f, 300f, out float wx, out float wz);
            Near(wx, 1000f, "project round-trip x");
            Near(wz, 2000f, "project round-trip z");

            BoardFrame one = BoardFit.Fit(new[] { 0f }, new[] { 0f }, 1, 200f, 100f, 0f, 2000f, 0f);
            TestAssert.That(one.MetresPerPixel * 100f >= 2000f - 0.1f, "minSpan is respected");
            Near(one.MetresPerPixel * 200f / (one.MetresPerPixel * 100f), 2f, "aspect follows the view");

            BoardFit.Project(frame, 1000f, 2000f, 400f, 300f, out float pivotX, out float pivotY);
            BoardFrame zoomed = BoardFit.Zoom(frame, 2f, pivotX, pivotY, 400f, 300f, 0.5f, 500f);
            Near(zoomed.MetresPerPixel, frame.MetresPerPixel * 0.5f, "zoom factor 2 halves metres per pixel");
            BoardFit.Project(zoomed, 1000f, 2000f, 400f, 300f, out float zx, out float zy);
            Near(zx, pivotX, "zoom keeps the pivot's world point");
            Near(zy, pivotY, "zoom keeps the pivot's world point");
            BoardFrame clamped = BoardFit.Zoom(frame, 1000f, pivotX, pivotY, 400f, 300f, 2f, 8f);
            TestAssert.That(clamped.MetresPerPixel >= 2f - 0.001f && clamped.MetresPerPixel <= 8f + 0.001f, "zoom clamps");

            BoardFrame shoved = BoardFit.Pan(frame, 100000f, -100000f, 400f, 300f, 40960f);
            float halfW = 200f * shoved.MetresPerPixel;
            float halfH = 150f * shoved.MetresPerPixel;
            TestAssert.That(shoved.CentreX - halfW < 40960f && shoved.CentreX + halfW > -40960f, "pan keeps the map in the view");
            TestAssert.That(shoved.CentreZ - halfH < 40960f && shoved.CentreZ + halfH > -40960f, "pan keeps the map in the view");
        }

        private static void CheckLabels()
        {
            var requests = new LabelRequest[20];
            for (int i = 0; i < 20; i++)
            {
                requests[i] = new LabelRequest(40f + (i % 5) * 52f, 30f + (i / 5) * 42f, 90f, 24f, 20 - i, 4f);
            }
            var placed = new PlacedLabel[32];
            int count = LabelPlacer.Place(requests, 20, new Box(0f, 0f, 300f, 200f), placed);
            TestAssert.That(count > 0 && count <= 20, "some labels place");
            for (int i = 0; i < count; i++)
            {
                TestAssert.That(placed[i].Visible, "a returned label is visible");
                Box rect = new Box(placed[i].X, placed[i].Y, placed[i].Width, placed[i].Height);
                TestAssert.That(rect.X >= -0.01f && rect.Y >= -0.01f && rect.Right <= 300.01f && rect.Bottom <= 200.01f,
                    "labels stay inside the bounds");
                for (int j = 0; j < i; j++)
                {
                    Box other = new Box(placed[j].X, placed[j].Y, placed[j].Width, placed[j].Height);
                    TestAssert.That(!rect.Intersects(other), "placed labels do not intersect");
                }
            }

            var contest = new[]
            {
                new LabelRequest(150f, 80f, 90f, 24f, 1, 6f),
                new LabelRequest(150f, 80f, 90f, 24f, 9, 6f)
            };
            int contested = LabelPlacer.Place(contest, 2, new Box(0f, 0f, 300f, 200f), placed);
            TestAssert.That(contested == 2, "both contested labels place");
            TestAssert.That(!placed[0].Leader && placed[1].Leader, "the higher priority keeps the preferred slot");
            TestAssert.That(placed[0].Index == 1 && placed[1].Index == 0, "each placed label names the request it belongs to");

            // A panel over the map: no label may land under it, and the anchor still gets a label.
            var obstacle = new[] { new Box(120f, 88f, 120f, 60f) };
            var lone = new[] { new LabelRequest(150f, 80f, 90f, 24f, 5, 6f) };
            int avoided = LabelPlacer.Place(lone, 1, new Box(0f, 0f, 300f, 200f), placed, obstacle, 1);
            TestAssert.That(avoided == 1 && placed[0].Index == 0, "a label finds a slot beside the obstacle");
            TestAssert.That(!new Box(placed[0].X, placed[0].Y, placed[0].Width, placed[0].Height).Intersects(obstacle[0]),
                "no label is placed under an obstacle");
            TestAssert.That(placed[0].Leader, "moving off the preferred slot draws a leader");

            var crowded = new LabelRequest[40];
            for (int i = 0; i < 40; i++) crowded[i] = new LabelRequest(10f, 10f, 80f, 20f, i, 2f);
            int capped = LabelPlacer.Place(crowded, 40, new Box(0f, 0f, 300f, 200f), placed);
            TestAssert.That(capped <= 32, "placement is bounded at 32 anchors");
            TestAssert.That(placed[0].Visible, "the highest priority is not the one dropped");
        }

        private static void CheckShorten()
        {
            TestAssert.That(PlaceNames.Shorten("VIGIL CAY NAVAL AIRBASE", 16) == "VIGIL CAY NAV AB", "naval airbase");
            TestAssert.That(PlaceNames.Shorten("DUSTBOWL HIGHWAY STRIP", 16) == "DUSTBOWL HWY STR", "highway strip");
            TestAssert.That(PlaceNames.Shorten("SOUTH COAST ENRICHMENT", 16) == "SOUTH COAST ENR", "stops once it fits");
            TestAssert.That(PlaceNames.Shorten("ALPHA BRAVO CHARLIE DELTA", 11) == "ALPHA BRAVO", "drops trailing words");
            TestAssert.That(PlaceNames.Shorten("SUPERCALIFRAGILISTIC", 8) == "SUPERCA…", "a single long word takes an ellipsis");
            TestAssert.That(PlaceNames.Shorten(PlaceNames.Shorten("VIGIL CAY NAVAL AIRBASE", 16), 16) == "VIGIL CAY NAV AB", "shorten is idempotent");
            TestAssert.That(PlaceNames.Shorten(null, 16) == "" && PlaceNames.Shorten("", 16) == "", "null and empty shorten to empty");
            TestAssert.That(PlaceNames.Shorten("AIR DEFENCE SITE", 8) == "AD SITE", "the two-word defence phrase abbreviates");
        }

        private static void CheckTimeline()
        {
            var lanes = new LaneSegment[8];
            int count = TimelineMath.Team(TeamState.EnRoute, 40f, FieldMission.Seize, 0, 600f, lanes);
            TestAssert.That(count == 5, "en route, arrival decision, task, hold, recover");
            Near(lanes[0].Start, 0f, "en route start");
            Near(lanes[0].End, 40f / 600f, "en route end");
            TestAssert.That(lanes[0].Kind == LaneKind.EnRoute && !lanes[0].Projected, "en route is current");
            Near(lanes[1].Start, 40f / 600f, "decision start");
            Near(lanes[1].End, 70f / 600f, "decision end");
            TestAssert.That(lanes[1].Kind == LaneKind.Deciding && lanes[1].Projected, "arrival decision is projected");
            Near(lanes[2].End, 130f / 600f, "task end");
            TestAssert.That(lanes[2].Kind == LaneKind.OnTask && lanes[2].Projected, "task awaits the execute order");
            Near(lanes[3].End, 250f / 600f, "temporary post end");
            TestAssert.That(lanes[3].Kind == LaneKind.Holding && lanes[3].Projected, "holding awaits success");
            Near(lanes[4].End, 310f / 600f, "recover end");
            TestAssert.That(lanes[4].Kind == LaneKind.Recovering && lanes[4].Projected, "recovery is projected");

            int arrived = TimelineMath.Team(TeamState.Deciding, 20f, FieldMission.Seize, 0, 600f, lanes);
            TestAssert.That(arrived == 4 && lanes[0].Kind == LaneKind.Deciding && !lanes[0].Projected,
                "arrival decision is current and task still needs a command");
            Near(lanes[0].End, 20f / 600f, "current decision end");
            TestAssert.That(lanes[1].Kind == LaneKind.OnTask && lanes[1].Projected, "uncommitted task is projected");

            int executing = TimelineMath.Team(TeamState.OnTask, 20f, FieldMission.Seize, 0, 600f, lanes);
            TestAssert.That(executing == 3 && lanes[0].Kind == LaneKind.OnTask && !lanes[0].Projected,
                "executing task is current");
            int post = TimelineMath.Team(TeamState.Holding, 20f, FieldMission.Seize, 0, 600f, lanes);
            TestAssert.That(post == 2 && lanes[0].Kind == LaneKind.Holding && !lanes[0].Projected,
                "earned post is current");
            int resting = TimelineMath.Team(TeamState.Recovering, 20f, FieldMission.Seize, 0, 600f, lanes);
            TestAssert.That(resting == 1 && lanes[0].Kind == LaneKind.Recovering && !lanes[0].Projected,
                "recovery is current");

            int ready = TimelineMath.Team(TeamState.Ready, 0f, FieldMission.Recon, 0, 600f, lanes);
            TestAssert.That(ready == 1 && lanes[0].Kind == LaneKind.Empty, "ready is one empty lane");

            int clipped = TimelineMath.Team(TeamState.EnRoute, 500f, FieldMission.Seize, 0, 100f, lanes);
            TestAssert.That(clipped == 1 && lanes[0].Kind == LaneKind.EnRoute, "a phase past the window is clipped");
            Near(lanes[0].End, 1f, "the clip ends at the window");
        }

        private static void CheckContrast()
        {
            Near(Contrast.Ratio(1f, 1f, 1f, 0f, 0f, 0f), 21f, "white on black");
            float primary = Contrast.Ratio(AvTokens.TextPrimary.R, AvTokens.TextPrimary.G, AvTokens.TextPrimary.B,
                AvTokens.Surface.R, AvTokens.Surface.G, AvTokens.Surface.B);
            float muted = Contrast.Ratio(AvTokens.TextMuted.R, AvTokens.TextMuted.G, AvTokens.TextMuted.B,
                AvTokens.Surface.R, AvTokens.Surface.G, AvTokens.Surface.B);
            float inert = Contrast.Ratio(AvTokens.RailInert.R, AvTokens.RailInert.G, AvTokens.RailInert.B,
                AvTokens.Surface.R, AvTokens.Surface.G, AvTokens.Surface.B);
            TestAssert.That(primary >= 4.5f, "primary text clears 4.5:1");
            TestAssert.That(muted >= 4.5f && muted < 7f, "muted text passes, around 5.6");
            TestAssert.That(inert < 3f, "an inert rail fails as body text");
        }

        private static void Near(float actual, float expected, string message)
        {
            TestAssert.That(Math.Abs(actual - expected) < 0.02f, message + " got " + actual + " wanted " + expected);
        }
    }
}
