using System;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>Pure layout maths for the OPS window: names, geometry, motion, boards.</summary>
    internal static class OpsLayoutTests
    {
        public static void Run()
        {
            CheckTownNames();
            CheckWindow();
            CheckMotion();
            CheckBoardFit();
            CheckLabels();
            CheckShorten();
            CheckTimeline();
            CheckStack();
            CheckContrast();
            CheckRoomInk();
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

        private static void CheckWindow()
        {
            Box hd = WindowGeometry.Window(1920f, 1080f);
            Same(hd, new Box(48f, 72f, 1824f, 968f), "1920x1080 window clears theater wire");
            Box wide = WindowGeometry.Window(2560f, 1080f);
            Same(wide, new Box(360f, 72f, 1840f, 968f), "21:9 clamps width and clears theater wire");
            Box floor = WindowGeometry.Window(1280f, 720f);
            Same(floor, new Box(0f, 0f, 1280f, 720f), "the floor wins on a small canvas");
            Same(WindowGeometry.Window(float.NaN, 1080f), hd, "non-finite canvas uses the centred default");

            Box mark = new Box(10f, 20f, 30f, 40f);
            TestAssert.That(mark.Contains(10f, 20f) && !mark.Contains(40f, 20f), "contains is half-open");
            TestAssert.That(mark.Intersects(new Box(39f, 59f, 5f, 5f)) && !mark.Intersects(new Box(40f, 20f, 5f, 5f)),
                "touching edges do not intersect");
            Same(mark.Inflate(2f, 3f), new Box(8f, 17f, 34f, 46f), "inflate");
        }

        private static void CheckMotion()
        {
            TestAssert.That(Motion.EaseOutCubic(0f) == 0f && Motion.EaseOutCubic(1f) == 1f, "ease endpoints");
            float previous = -1f;
            for (int i = 0; i <= 10; i++)
            {
                float value = Motion.EaseOutCubic(i / 10f);
                TestAssert.That(value >= previous, "ease is monotonic");
                previous = value;
            }
            TestAssert.That(Motion.Progress(-4f, 0.16f, true) == 1f, "reduced motion is immediate");
            TestAssert.That(Motion.Progress(0.01f, 0f, false) == 1f, "a zero duration is immediate");
            TestAssert.That(Motion.Progress(-1f, 0.16f, false) == 0f, "negative elapsed has not started");
            TestAssert.That(Motion.BackdropIn == 0.12f && Motion.WindowIn == 0.16f &&
                            Motion.WindowOut == 0.09f && Motion.RoomSwitch == 0.12f, "root motion tokens");

            var tween = new TweenState();
            tween.Retarget(1f, 1f, false);
            tween.Tick(0.5f);
            float mid = tween.Value;
            TestAssert.That(mid > 0.5f && mid < 1f, "ease-out is ahead of linear at the midpoint");
            tween.Retarget(0f, 1f, false);
            TestAssert.That(Math.Abs(tween.Value - mid) < 0.0001f, "a retarget starts from the current value");
            tween.Tick(0.01f);
            TestAssert.That(Math.Abs(tween.Value - mid) < 0.05f, "the next sample does not jump");
            tween.Retarget(1f, 1f, true);
            TestAssert.That(tween.Value == 1f, "reduced motion snaps");
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
            TestAssert.That(count == 4, "en route, task, hold, recover");
            Near(lanes[0].Start, 0f, "en route start");
            Near(lanes[0].End, 40f / 600f, "en route end");
            TestAssert.That(lanes[0].Kind == LaneKind.EnRoute && !lanes[0].Projected, "en route is current");
            Near(lanes[1].Start, 40f / 600f, "task start");
            Near(lanes[1].End, 100f / 600f, "task end");
            TestAssert.That(lanes[1].Kind == LaneKind.OnTask && !lanes[1].Projected, "the task is the committed phase");
            Near(lanes[2].End, 400f / 600f, "hold end");
            TestAssert.That(lanes[2].Kind == LaneKind.Holding && lanes[2].Projected, "holding is projected");
            Near(lanes[3].End, 460f / 600f, "recover end");
            TestAssert.That(lanes[3].Kind == LaneKind.Recovering && lanes[3].Projected, "recovery is projected");

            int ready = TimelineMath.Team(TeamState.Ready, 0f, FieldMission.Recon, 0, 600f, lanes);
            TestAssert.That(ready == 1 && lanes[0].Kind == LaneKind.Empty, "ready is one empty lane");

            int clipped = TimelineMath.Team(TeamState.EnRoute, 500f, FieldMission.Seize, 0, 100f, lanes);
            TestAssert.That(clipped == 1 && lanes[0].Kind == LaneKind.EnRoute, "a phase past the window is clipped");
            Near(lanes[0].End, 1f, "the clip ends at the window");
        }

        private static void CheckStack()
        {
            var pieces = new[]
            {
                new StackPiece(1, 200f, 200f, false),
                new StackPiece(2, 180f, 180f, false),
                new StackPiece(3, 160f, 160f, false),
                new StackPiece(4, 48f, 48f, true)
            };
            var heights = new float[4];
            float used = AdaptiveStack.Fit(pieces, 4, 420f, heights);
            TestAssert.That(heights[0] == 200f && heights[1] == 180f && heights[2] == 0f && heights[3] == 0f, "420 keeps P1 and P2");
            TestAssert.That(used <= 420f, "420 does not overflow");
            used = AdaptiveStack.Fit(pieces, 4, 596f, heights);
            TestAssert.That(heights[2] == 160f && heights[3] >= 48f, "596 fits P3 and gives P4 at least three lines");
            Near(used, 596f, "596 fills with the log");
            used = AdaptiveStack.Fit(pieces, 4, 896f, heights);
            Near(heights[3], 896f - 540f, "896 gives the fill section every leftover pixel");
            TestAssert.That(used <= 896f + 0.01f, "896 does not overflow");
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

        /// <summary>
        /// Every room reads its own surface: body ink at least 4.5:1 on the surface it sits on,
        /// strokes and accents at least 3:1. Parsed from the shipped sheet, so a retune that breaks
        /// a pair fails here rather than on screen.
        /// </summary>
        private static void CheckRoomInk()
        {
            string text;
            using (System.IO.Stream stream = typeof(OpsLayoutTests).Assembly
                .GetManifestResourceStream("BoscaliSummer.Tests.avionics.avss"))
            {
                TestAssert.That(stream != null, "the shipped avionics sheet must be embedded for the room ink check");
                using (var reader = new System.IO.StreamReader(stream)) text = reader.ReadToEnd();
            }
            AvStyleSheet sheet = AvStyleSheet.Parse(text);
            Ink(sheet, "room-space-ink", "room-space-surface", 4.5f);
            Ink(sheet, "room-space-dim", "room-space-surface", 4.5f);
            Ink(sheet, "room-space-ink", "room-space-console", 4.5f);
            Ink(sheet, "room-space-dim", "room-space-console", 4.5f);
            Ink(sheet, "room-space-line", "room-space-surface", 3f);
            Ink(sheet, "room-cyber-ink", "room-cyber-surface", 4.5f);
            Ink(sheet, "room-cyber-ink", "room-cyber-pane", 4.5f);
            Ink(sheet, "room-cyber-dim", "room-cyber-pane", 4.5f);
            Ink(sheet, "room-cyber-title", "room-cyber-bar", 4.5f);
            Ink(sheet, "room-cyber-accent", "room-cyber-pane", 3f);
            Ink(sheet, "room-cyber-lattice", "room-cyber-surface", 1.5f);
            Ink(sheet, "room-specops-ink", "room-specops-paper", 4.5f);
            Ink(sheet, "room-specops-khaki", "room-specops-paper", 4.5f);
            Ink(sheet, "room-specops-stamp", "room-specops-paper", 4.5f);
            Ink(sheet, "room-specops-map", "room-specops-map", 4.5f);
            Ink(sheet, "room-specops-banner", "room-specops-banner", 4.5f);
            Ink(sheet, "room-imager-ink", "room-imager-pod", 4.5f);
            Ink(sheet, "room-imager-dim", "room-imager-pod", 4.5f);
        }

        private static void Ink(AvStyleSheet sheet, string inkClass, string surfaceClass, float minimum)
        {
            AvStyle ink = sheet.Resolve(inkClass);
            AvStyle surface = sheet.Resolve(surfaceClass);
            TestAssert.That(ink.Color.Kind == AvColorRef.Fixed, "." + inkClass + " declares a literal colour");
            TestAssert.That(surface.Background.Kind == AvColorRef.Fixed, "." + surfaceClass + " declares a literal background");
            if (ink.Color.Kind != AvColorRef.Fixed || surface.Background.Kind != AvColorRef.Fixed) return;
            Rgba a = ink.Color.Value, b = surface.Background.Value;
            float ratio = Contrast.Ratio(a.R, a.G, a.B, b.R, b.G, b.B);
            TestAssert.That(ratio >= minimum, "." + inkClass + " on ." + surfaceClass + " is " + ratio.ToString("0.0") +
                                             ":1, needs " + minimum + ":1");
        }

        private static void Same(Box actual, Box expected, string message)
        {
            Near(actual.X, expected.X, message + " x");
            Near(actual.Y, expected.Y, message + " y");
            Near(actual.Width, expected.Width, message + " w");
            Near(actual.Height, expected.Height, message + " h");
        }

        private static void Near(float actual, float expected, string message)
        {
            TestAssert.That(Math.Abs(actual - expected) < 0.02f, message + " got " + actual + " wanted " + expected);
        }
    }
}
