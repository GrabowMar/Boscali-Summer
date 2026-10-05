using System;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class ReliefRigTests
    {
        private const float Tolerance = 1e-4f; // viewport units: 0.2 px on a 2000 px view

        public static void Run()
        {
            TestProjectRoundTrip();
            TestGrabKeepsPointUnderCursor();
            TestZoomKeepsAnchor();
            TestOrbitKeepsPivot();
            TestClamps();
            TestDampIsFrameRateIndependent();
            TestNorthUpBasis();
        }

        private static ReliefRig Rig(float yaw = 37f, float pitch = 55f, float zoom = 3f) =>
            new ReliefRig { HalfX = 450f, HalfZ = 450f, Top = 40f, Aspect = 1.6f,
                Yaw = yaw, Pitch = pitch, Zoom = zoom, FocusX = 20f, FocusZ = -35f };

        private static void TestProjectRoundTrip()
        {
            ReliefRig rig = Rig();
            rig.Project(60f, 12f, -10f, out float vx, out float vy);
            rig.Unproject(vx, vy, 12f, out float x, out float z);
            TestAssert.That(Math.Abs(x - 60f) < .01f && Math.Abs(z + 10f) < .01f,
                "Relief rig: unproject must invert project on the point's plane");
        }

        private static void TestGrabKeepsPointUnderCursor()
        {
            ReliefRig rig = Rig();
            rig.Unproject(.4f, .6f, 8f, out float x, out float z);
            rig.Grab(x, 8f, z, .7f, .35f);
            rig.Project(x, 8f, z, out float vx, out float vy);
            TestAssert.That(Math.Abs(vx - .7f) < Tolerance && Math.Abs(vy - .35f) < Tolerance,
                "Relief rig: a grabbed ground point must stay under the cursor");
        }

        private static void TestZoomKeepsAnchor()
        {
            ReliefRig rig = Rig();
            rig.Unproject(.2f, .8f, 5f, out float before, out float beforeZ);
            rig.ZoomAt(7.5f, .2f, .8f, 5f);
            rig.Unproject(.2f, .8f, 5f, out float after, out float afterZ);
            TestAssert.That(Math.Abs(rig.Zoom - 7.5f) < 1e-4f, "Relief rig: zoom must reach the requested level");
            TestAssert.That(Math.Abs(before - after) < .01f && Math.Abs(beforeZ - afterZ) < .01f,
                "Relief rig: zoom must keep the ground under the cursor");
        }

        private static void TestOrbitKeepsPivot()
        {
            ReliefRig rig = Rig();
            rig.Unproject(.65f, .45f, 10f, out float x, out float z);
            rig.OrbitAbout(48f, -17f, x, 10f, z);
            rig.Project(x, 10f, z, out float vx, out float vy);
            TestAssert.That(Math.Abs(vx - .65f) < Tolerance && Math.Abs(vy - .45f) < Tolerance,
                "Relief rig: orbit must keep the pivot on its screen spot");
            TestAssert.That(Math.Abs(rig.Yaw - 85f) < 1e-3f && Math.Abs(rig.Pitch - 38f) < 1e-3f,
                "Relief rig: orbit must apply both angle deltas");
        }

        private static void TestClamps()
        {
            ReliefRig rig = Rig();
            rig.OrbitAbout(0f, 90f, 0f, 0f, 0f);
            TestAssert.That(Math.Abs(rig.Pitch - ReliefRig.MaxPitch) < 1e-4f, "Relief rig: pitch must stop at the top limit");
            rig.OrbitAbout(200f, -200f, 0f, 0f, 0f);
            TestAssert.That(Math.Abs(rig.Pitch - ReliefRig.MinPitch) < 1e-4f, "Relief rig: pitch must stop at the low limit");
            TestAssert.That(rig.Yaw >= -180f && rig.Yaw < 180f, "Relief rig: yaw must stay wrapped");
            rig.ZoomAt(500f, .5f, .5f, 0f);
            TestAssert.That(rig.Zoom <= ReliefRig.MaxZoom && rig.Zoom == rig.ZoomLimit, "Relief rig: zoom must stop at its limit");
            rig.ZoomAt(.1f, .5f, .5f, 0f);
            TestAssert.That(rig.Zoom == ReliefRig.MinZoom, "Relief rig: zoom must not go below fit");
            rig.Grab(0f, 0f, 0f, -40f, 40f);
            TestAssert.That(Math.Abs(rig.FocusX) <= rig.HalfX && Math.Abs(rig.FocusZ) <= rig.HalfZ,
                "Relief rig: focus must stay on the sheet");
        }

        private static void TestDampIsFrameRateIndependent()
        {
            float slow = 1f, fast = 1f;
            for (int i = 0; i < 30; i++) slow = ReliefRig.Damp(slow, 4f, 1f / 30f, .06f);
            for (int i = 0; i < 144; i++) fast = ReliefRig.Damp(fast, 4f, 1f / 144f, .06f);
            TestAssert.That(Math.Abs(slow - fast) < 1e-4f, "Relief rig: damping must not depend on frame rate");
            TestAssert.That(Math.Abs(fast - 4f) < 1e-3f, "Relief rig: damping must converge within a second");
            float angle = ReliefRig.DampAngle(170f, -170f, 10f, .06f);
            TestAssert.That(Math.Abs(ReliefRig.WrapAngle(angle + 170f)) < 1e-3f,
                "Relief rig: angle damping must take the short way round");
        }

        private static void TestNorthUpBasis()
        {
            var rig = new ReliefRig { HalfX = 450f, HalfZ = 450f, Aspect = 1f, Pitch = 85f };
            rig.Project(0f, 0f, 100f, out float vx, out float vy);
            TestAssert.That(Math.Abs(vx - .5f) < 1e-4f && vy > .5f, "Relief rig: yaw 0 must put north up");
            rig.Project(100f, 0f, 0f, out vx, out vy);
            TestAssert.That(vx > .5f && Math.Abs(vy - .5f) < 1e-4f, "Relief rig: yaw 0 must put east right");
        }
    }
}
