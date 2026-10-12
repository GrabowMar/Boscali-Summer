using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>
    /// Pure assertions for <see cref="ThirdPersonHudLayout"/>: every cluster/card rect stays
    /// inside the screen and clear of the aircraft-frame guard, across the aspect ratios the
    /// spec calls out (16:9, 21:9, 32:9, 4:3).
    /// </summary>
    internal static class ThirdPersonHudLayoutTests
    {
        private static readonly float[] AspectRatios = { 16f / 9f, 21f / 9f, 32f / 9f, 4f / 3f };
        private const float Height = 1080f;

        public static void Run()
        {
            foreach (float aspect in AspectRatios)
            {
                float width = Height * aspect;
                ClusterInsideAndClear(width, Height, aspect);
                TargetCardInsideAndClear(width, Height, aspect);
            }
            TextScale();
            BoardKeepClear();
            KeepClearIsIdempotent();
            CollapsedCardHasNoHeight();
            Cues();
        }

        private static RectF Screen(float width, float height) => new RectF(0f, 0f, width, height);

        /// <summary>The TAC BOARD's box on a canvas (centre-origin, y up) as the presenter hands it to the cluster: held by its cruise height, <paramref name="pageH"/> tall when a page is live.</summary>
        private static RectF BoardBox(float canvasW, float canvasH, float baseUnit, BoardLayout layout, float pageH)
        {
            BoardBlocks live = new BoardBlocks { Layout = layout, Hero = true, Pwr = true, PwrCost = true, Exp = true, Lamps = true, PageH = pageH, GaugeLines = pageH > ScopeLayout.Compact ? 2 : 0, Ticker = pageH > ScopeLayout.Compact };
            BoardBlocks cruise = live;
            cruise.PageH = ScopeLayout.Compact;
            cruise.GaugeLines = 0;
            cruise.Ticker = false;
            BoardGeometry geo = ScopeLayout.Compute(live);
            ScopeLayout.Anchor(canvasW, canvasH, baseUnit, baseUnit, 0f, 0f, geo.Width, ScopeLayout.Compute(cruise).Height, out float fromRight, out float fromTop);
            return ScopeLayout.Box(canvasW, canvasH, fromRight, fromTop, geo.Width * baseUnit, geo.Height * baseUnit);
        }

        /// <summary>
        /// The cluster keeps clear of the TAC BOARD. At 1280 x 720 the canvas is 1920 x 1080 units and the board's unit 1.275 of them (0.85 of a pixel): the heading box (100 x 32, 0.30 H above the centre) is under the
        /// HERO block by about 100 x 11; at 4:3 (1440 x 1080) a live STACK page reaches the altitude box (120 x 56 at 0.17 W): the shortest move out is taken, and the box's bar stays clear too.
        /// </summary>
        private static void BoardKeepClear()
        {
            RectF none = default;
            RectF wide = BoardBox(1920f, 1080f, 1.275f, BoardLayout.Wide, ScopeLayout.Compact);
            RectF hdg = ThirdPersonHudLayout.HeadingBox(1920f, 1080f, none);
            TestAssert.That(ThirdPersonHudLayout.Overlaps(hdg, wide), "At 720p in WIDE the unmoved heading box is under the board");
            float across = Math.Min(hdg.Right, wide.Right) - Math.Max(hdg.Left, wide.Left), down = Math.Min(hdg.Top, wide.Top) - Math.Max(hdg.Bottom, wide.Bottom);
            TestAssert.That(across >= 99f && down > 8f && down < 14f, "...by about 100 x 11: " + across + " x " + down);
            RectF moved = ThirdPersonHudLayout.HeadingBox(1920f, 1080f, wide);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(moved, wide), "With the board's box fed in the heading box is clear of it");
            TestAssert.Near(moved.CenterX, hdg.CenterX, "...it moved straight down");
            TestAssert.That(moved.CenterY < hdg.CenterY && hdg.CenterY - moved.CenterY < 30f, "...by less than 30 units: " + (hdg.CenterY - moved.CenterY));
            RectF frame = ThirdPersonHudLayout.AircraftFrame(1920f, 1080f);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(moved, frame), "...and still clear of the aircraft frame");
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(ThirdPersonHudLayout.SpeedBox(1920f, 1080f, wide), wide) && !ThirdPersonHudLayout.Overlaps(ThirdPersonHudLayout.AltitudeBox(1920f, 1080f, wide), wide), "The speed and altitude boxes are clear of it");

            // 1080p 16:9: nothing is in the way, nothing moves.
            RectF wide1080 = BoardBox(1920f, 1080f, 1f, BoardLayout.Wide, ScopeLayout.Compact);
            TestAssert.That(ThirdPersonHudLayout.HeadingBox(1920f, 1080f, wide1080).CenterY == ThirdPersonHudLayout.HeadingBox(1920f, 1080f).CenterY, "At 1080p the heading box is not in its way");
            RectF live1080 = BoardBox(1920f, 1080f, 1f, BoardLayout.Wide, ScopeLayout.Full);
            TestAssert.That(ThirdPersonHudLayout.AltitudeBox(1920f, 1080f, live1080).CenterX == ThirdPersonHudLayout.AltitudeBox(1920f, 1080f).CenterX, "...nor is the altitude box, with a live page");

            // 4:3 STACK with a live page.
            RectF stack = BoardBox(1440f, 1080f, 1f, BoardLayout.Stack, ScopeLayout.Full);
            RectF alt = ThirdPersonHudLayout.AltitudeBox(1440f, 1080f);
            TestAssert.That(ThirdPersonHudLayout.Overlaps(alt, stack), "At 4:3 a live STACK page reaches the unmoved altitude box");
            RectF altMoved = ThirdPersonHudLayout.AltitudeBox(1440f, 1080f, stack);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(altMoved, stack), "With the board's box fed in the altitude box is clear of it");
            RectF bar = ThirdPersonHudLayout.SideBar(altMoved, outwardIsLeft: false);
            TestAssert.That(bar.Right <= stack.Left - ThirdPersonHudLayout.GuardMargin + 0.01f, "...and so is the bar beside it");
            TestAssert.Near(altMoved.CenterY, alt.CenterY, "...it moved sideways (the shorter way out), not down");
            TestAssert.That(altMoved.CenterX < alt.CenterX, "...toward the middle of the screen");
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(altMoved, ThirdPersonHudLayout.AircraftFrame(1440f, 1080f)), "...and clear of the aircraft frame");
            RectF stackCruise = BoardBox(1440f, 1080f, 1f, BoardLayout.Stack, ScopeLayout.Compact);
            TestAssert.That(ThirdPersonHudLayout.AltitudeBox(1440f, 1080f, stackCruise).CenterX == alt.CenterX, "STACK in cruise is not in its way");

            // The rule on its own: the four moves, the shortest wins; no board, or a board already clear, changes nothing.
            var board = new RectF(0f, 0f, 200f, 200f);
            TestAssert.That(ThirdPersonHudLayout.KeepClearOf(new RectF(0f, 0f, 20f, 20f), none, 6f).CenterX == 0f, "No board: unchanged");
            TestAssert.That(ThirdPersonHudLayout.KeepClearOf(new RectF(300f, 0f, 20f, 20f), board, 6f).CenterX == 300f, "Already clear: unchanged");
            RectF right = ThirdPersonHudLayout.KeepClearOf(new RectF(95f, 0f, 20f, 20f), board, 6f);
            TestAssert.That(Math.Abs(right.Left - 106f) < 0.01f && right.CenterY == 0f, "Nearest the right edge: out to the right, 6 clear: " + right.Left);
            RectF top = ThirdPersonHudLayout.KeepClearOf(new RectF(0f, 95f, 20f, 20f), board, 6f);
            TestAssert.That(Math.Abs(top.Bottom - 106f) < 0.01f && top.CenterX == 0f, "Nearest the top: up, 6 clear: " + top.Bottom);
            RectF idempotent = ThirdPersonHudLayout.KeepClearOf(top, board, 6f);
            TestAssert.That(idempotent.CenterX == top.CenterX && idempotent.CenterY == top.CenterY, "A rect that was moved is clear: moving it again changes nothing");
        }

        private static bool Inside(RectF r, RectF screen) =>
            r.Left >= screen.Left - 0.01f && r.Right <= screen.Right + 0.01f &&
            r.Bottom >= screen.Bottom - 0.01f && r.Top <= screen.Top + 0.01f;

        private static void TextScale()
        {
            TestAssert.That(ThirdPersonHudLayout.TextScale(40f) == 1f && ThirdPersonHudLayout.TextScale(60f) == 1.5f, "The player's text size scales the layout");
            TestAssert.That(ThirdPersonHudLayout.TextScale(100f) == ThirdPersonHudLayout.MaxScale && ThirdPersonHudLayout.TextScale(10f) == ThirdPersonHudLayout.MinScale, "...within limits");
            TestAssert.That(ThirdPersonHudLayout.TextScale(float.NaN) == 1f && ThirdPersonHudLayout.TextScale(0f) == 1f, "An unknown size is the default");
        }

        private static void Cues()
        {
            TestAssert.That(HudCues.Terrain(1000f, -50f) == TerrainCue.None, "High above the ground: no cue");
            TestAssert.That(HudCues.Terrain(100f, 2f) == TerrainCue.None, "Climbing low: no cue");
            TestAssert.That(HudCues.Terrain(100f, -10f) == TerrainCue.Low, "Low and sinking: LOW");
            TestAssert.That(HudCues.Terrain(100f, -30f) == TerrainCue.PullUp, "Impact in 3.3 s: PULL UP");
            TestAssert.That(HudCues.Terrain(float.NaN, -30f) == TerrainCue.None, "Unknown radar altitude: no cue");
            TestAssert.That(HudCues.Cardinal(0f) == "N" && HudCues.Cardinal(359f) == "N" && HudCues.Cardinal(87f) == "E" &&
                HudCues.Cardinal(-90f) == "W" && HudCues.Cardinal(225f) == "SW", "Compass points round to the nearest of eight");
        }

        private static void ClusterInsideAndClear(float width, float height, float aspect)
        {
            RectF screen = Screen(width, height);
            RectF frame = ThirdPersonHudLayout.AircraftFrame(width, height);
            TestAssert.That(Inside(frame, screen), "Aircraft frame stays on screen at aspect " + aspect);

            RectF spd = ThirdPersonHudLayout.SpeedBox(width, height);
            RectF alt = ThirdPersonHudLayout.AltitudeBox(width, height);
            RectF hdg = ThirdPersonHudLayout.HeadingBox(width, height);

            foreach ((RectF rect, string name) in new[] { (spd, "SPD"), (alt, "ALT"), (hdg, "HDG") })
            {
                TestAssert.That(Inside(rect, screen),
                    name + " box stays on screen at aspect " + aspect);
                TestAssert.That(!ThirdPersonHudLayout.Overlaps(rect, frame),
                    name + " box never overlaps the aircraft frame at aspect " + aspect);
            }

            TestAssert.That(spd.CenterX < 0f && alt.CenterX > 0f, "SPD sits left of centre, ALT sits right");
            TestAssert.That(hdg.CenterX == 0f, "HDG is horizontally centred");

            RectF spdBar = ThirdPersonHudLayout.SideBar(spd, outwardIsLeft: true);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(spdBar, spd), "The side bar does not sit on top of its box");
        }

        private static void TargetCardInsideAndClear(float width, float height, float aspect)
        {
            RectF screen = Screen(width, height);
            RectF frame = ThirdPersonHudLayout.AircraftFrame(width, height);
            RectF card = ThirdPersonHudLayout.TargetCard(width, height, visible: true);

            TestAssert.That(Inside(card, screen), "Target card stays on screen at aspect " + aspect);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(card, frame), "Target card never overlaps the aircraft frame at aspect " + aspect);
            TestAssert.That(card.Right <= screen.Right - ThirdPersonHudLayout.SafeMargin + 0.5f &&
                card.Bottom >= screen.Bottom + ThirdPersonHudLayout.SafeMargin - 0.5f,
                "Target card respects the safe margin at aspect " + aspect);
            TestAssert.That(card.Width == ThirdPersonHudLayout.TargetCardWidth, "Target card width is capped, never the full panel width");
        }

        private static void KeepClearIsIdempotent()
        {
            RectF frame = ThirdPersonHudLayout.AircraftFrame(1920f, 1080f);
            RectF clear = new RectF(500f, 500f, 50f, 20f);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(clear, frame), "Sanity: this rect starts clear of the frame");
            RectF unchanged = ThirdPersonHudLayout.KeepClear(clear, frame);
            TestAssert.That(unchanged.CenterX == clear.CenterX && unchanged.CenterY == clear.CenterY,
                "KeepClear must not move a rect that is already clear");

            RectF overlapping = new RectF(frame.CenterX, frame.CenterY, 20f, 20f);
            RectF pushed = ThirdPersonHudLayout.KeepClear(overlapping, frame);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(pushed, frame), "KeepClear must resolve a real overlap");
            RectF pushedAgain = ThirdPersonHudLayout.KeepClear(pushed, frame);
            TestAssert.That(pushedAgain.CenterY == pushed.CenterY, "KeepClear is idempotent once clear");
        }

        private static void CollapsedCardHasNoHeight()
        {
            RectF collapsed = ThirdPersonHudLayout.TargetCard(1920f, 1080f, visible: false);
            TestAssert.That(collapsed.Height == 0f, "A collapsed target card reserves no vertical space");
        }
    }
}
