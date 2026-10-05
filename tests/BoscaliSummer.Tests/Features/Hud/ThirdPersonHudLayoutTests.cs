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
            KeepClearIsIdempotent();
            CollapsedCardHasNoHeight();
        }

        private static void ClusterInsideAndClear(float width, float height, float aspect)
        {
            RectF screen = ThirdPersonHudLayout.Screen(width, height);
            RectF frame = ThirdPersonHudLayout.AircraftFrame(width, height);
            TestAssert.That(ThirdPersonHudLayout.Inside(frame, screen), "Aircraft frame stays on screen at aspect " + aspect);

            RectF spd = ThirdPersonHudLayout.SpeedBox(width, height);
            RectF alt = ThirdPersonHudLayout.AltitudeBox(width, height);
            RectF hdg = ThirdPersonHudLayout.HeadingBox(width, height);

            foreach ((RectF rect, string name) in new[] { (spd, "SPD"), (alt, "ALT"), (hdg, "HDG") })
            {
                TestAssert.That(ThirdPersonHudLayout.Inside(rect, screen),
                    name + " box stays on screen at aspect " + aspect);
                TestAssert.That(!ThirdPersonHudLayout.Overlaps(rect, frame),
                    name + " box never overlaps the aircraft frame at aspect " + aspect);
            }

            TestAssert.That(spd.CenterX < 0f && alt.CenterX > 0f, "SPD sits left of centre, ALT sits right");
            TestAssert.That(hdg.CenterX == 0f, "HDG is horizontally centred");

            RectF spdSub = ThirdPersonHudLayout.SubLine(spd);
            RectF spdBar = ThirdPersonHudLayout.SideBar(spd, outwardIsLeft: true);
            TestAssert.That(ThirdPersonHudLayout.Inside(spdSub, screen), "SPD sub-line stays on screen at aspect " + aspect);
            TestAssert.That(!ThirdPersonHudLayout.Overlaps(spdBar, spd), "The side bar does not sit on top of its box");
        }

        private static void TargetCardInsideAndClear(float width, float height, float aspect)
        {
            RectF screen = ThirdPersonHudLayout.Screen(width, height);
            RectF frame = ThirdPersonHudLayout.AircraftFrame(width, height);
            RectF card = ThirdPersonHudLayout.TargetCard(width, height, visible: true);

            TestAssert.That(ThirdPersonHudLayout.Inside(card, screen), "Target card stays on screen at aspect " + aspect);
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
