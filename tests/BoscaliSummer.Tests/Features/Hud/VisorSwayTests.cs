using System;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The visor's lag (LAYOUT.md 10): opposite to the head, at most 4 units, eased, the same at any frame rate, nothing on a cut or a hitch.</summary>
    internal static class VisorSwayTests
    {
        public static void Run()
        {
            StepResponse();
            Clamp();
            Cuts();
            FrameRate();
            Pixels();
        }

        /// <summary>Turn the camera at <paramref name="yawRate"/> / <paramref name="pitchRate"/> degrees a second for <paramref name="seconds"/> at <paramref name="fps"/> frames a second.</summary>
        private static VisorSway Turn(VisorSway s, float yawRate, float pitchRate, float seconds, float fps)
        {
            float dt = 1f / fps;
            int frames = (int)Math.Round(seconds * fps);
            for (int i = 0; i < frames; i++) s.Step(dt, yawRate * dt, pitchRate * dt);
            return s;
        }

        /// <summary>A steady turn swings the symbology the other way by rate / FullRate x 4 units, along an exponential at 6 a second; when the head stops it comes back.</summary>
        private static void StepResponse()
        {
            var s = new VisorSway();
            TestAssert.That(s.X == 0f && s.Y == 0f, "At rest the symbology is where it is drawn");
            // 40 degrees a second to the right and 20 up: half and a quarter of the full swing, opposite to the motion (left, down).
            s = Turn(s, 40f, 20f, 1f / 6f, 120f);
            float expectX = -2f * (1f - (float)Math.Exp(-1f)), expectY = -1f * (1f - (float)Math.Exp(-1f));
            TestAssert.That(Math.Abs(s.X - expectX) < 0.01f && Math.Abs(s.Y - expectY) < 0.01f, "After one time constant (a sixth of a second) it is 63 % of the way: (" + s.X + ", " + s.Y + ") for (" + expectX + ", " + expectY + ")");
            s = Turn(s, 40f, 20f, 1.5f, 120f);
            TestAssert.That(Math.Abs(s.X + 2f) < 0.01f && Math.Abs(s.Y + 1f) < 0.01f, "Held, it settles at -2 and -1 units: (" + s.X + ", " + s.Y + ")");
            s = Turn(s, -40f, 0f, 0.5f, 120f);
            TestAssert.That(s.X > 0f, "A turn the other way swings it the other way");
            s = Turn(s, 0f, 0f, 0.5f, 120f);
            float held = Math.Abs(s.X) + Math.Abs(s.Y);
            s = Turn(s, 0f, 0f, 1.5f, 120f);
            TestAssert.That(Math.Abs(s.X) + Math.Abs(s.Y) < held && Math.Abs(s.X) < 0.01f && Math.Abs(s.Y) < 0.01f, "Still, it eases back to nothing: " + s.X + ", " + s.Y);
        }

        /// <summary>However fast the head turns, the symbology goes no further than 4 units each way.</summary>
        private static void Clamp()
        {
            var s = Turn(new VisorSway(), 300f, 300f, 2f, 240f);      // 1.25 degrees a frame: not a cut
            TestAssert.Near(s.X, -VisorSway.MaxUnits, "Fast to the right: at most 4 units left");
            TestAssert.Near(s.Y, -VisorSway.MaxUnits, "Fast up: at most 4 units down");
            s = Turn(new VisorSway(), -300f, -300f, 2f, 240f);
            TestAssert.Near(s.X, VisorSway.MaxUnits, "Fast to the left: at most 4 units right");
            TestAssert.Near(s.Y, VisorSway.MaxUnits, "Fast down: at most 4 units up");
            TestAssert.Near(VisorSway.MaxUnits, 4f, "LAYOUT.md 10: 4 units");
        }

        /// <summary>A camera cut (a jump over a few degrees in one frame), a hitch and a bad number are no head movement: the filter starts over.</summary>
        private static void Cuts()
        {
            var s = Turn(new VisorSway(), 40f, 20f, 1f, 60f);
            TestAssert.That(s.X != 0f, "Swinging");
            s.Step(1f / 60f, 20f, 0f);
            TestAssert.That(s.X == 0f && s.Y == 0f, "A 20 degree jump in a frame is a cut: reset, not a swing");
            s = Turn(new VisorSway(), 0f, 0f, 0.5f, 60f);
            s.Step(1f / 60f, 0f, -9f);
            TestAssert.That(s.X == 0f && s.Y == 0f, "...pitch too");
            s.Step(1f / 60f, 5f, 0f);
            TestAssert.That(s.X < 0f && s.X > -VisorSway.MaxUnits - 0.001f, "Just under the limit it still swings: " + s.X);
            s = Turn(new VisorSway(), 40f, 0f, 1f, 60f);
            s.Step(0.5f, 0.1f, 0f);
            TestAssert.That(s.X == 0f, "A frame half a second long is a hitch: reset");
            s = Turn(new VisorSway(), 40f, 0f, 1f, 60f);
            float before = s.X;
            s.Step(0f, 5f, 0f);
            s.Step(float.NaN, 5f, 0f);
            TestAssert.That(s.X == before, "No time, or no number, changes nothing");
            s.Step(1f / 60f, float.NaN, 0f);
            TestAssert.That(s.X == 0f && s.Y == 0f, "A bad angle resets");
        }

        /// <summary>The same head movement gives the same offset at any frame rate (the turn per frame is the rate over the frame, the ease is exact).</summary>
        private static void FrameRate()
        {
            float[] x = new float[3], y = new float[3];
            float[] fps = { 30f, 60f, 144f };
            for (int i = 0; i < fps.Length; i++)
            {
                VisorSway s = Turn(new VisorSway(), 35f, -15f, 0.5f, fps[i]);      // half a second is a whole number of frames at all three
                s = Turn(s, 0f, 0f, 0.5f, fps[i]);
                x[i] = s.X;
                y[i] = s.Y;
            }
            for (int i = 1; i < fps.Length; i++)
                TestAssert.That(Math.Abs(x[i] - x[0]) < 0.001f && Math.Abs(y[i] - y[0]) < 0.001f, "The same movement at " + fps[i] + " and 30 frames a second: (" + x[i] + ", " + y[i] + ") against (" + x[0] + ", " + y[0] + ")");
        }

        /// <summary>The offset reaches the screen as whole device pixels, symmetric about nothing.</summary>
        private static void Pixels()
        {
            TestAssert.Eq(VisorSway.Pixels(0f, 1f), 0, "Nothing is no pixels");
            TestAssert.Eq(VisorSway.Pixels(2.4f, 1f), 2, "2.4 units at a pixel a unit: 2 pixels");
            TestAssert.Eq(VisorSway.Pixels(-2.6f, 1f), -3, "-2.6 units: -3 pixels");
            TestAssert.Eq(VisorSway.Pixels(2.5f, 1f), 3, "Halves go away from nothing");
            TestAssert.Eq(VisorSway.Pixels(-2.5f, 1f), -3, "...both ways");
            TestAssert.Eq(VisorSway.Pixels(4f, 0.85f), 3, "At 720p a unit is 0.85 of a pixel: 4 units are 3 pixels");
            TestAssert.Eq(VisorSway.Pixels(4f, 2f), 8, "On a 4K screen they are 8");
        }
    }
}
