using BoscaliSummer.Features.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>
    /// Pure assertions for <see cref="WingviewMath"/>: frame-rate independence of the exponential
    /// smoothing, the look-ahead clamp, level-horizon behaviour, and NaN-safety of every entry
    /// point against degenerate input.
    /// </summary>
    internal static class WingviewMathTests
    {
        public static void Run()
        {
            FrameRateIndependence();
            LookAheadClamp();
            LevelHorizon();
            NaNSafety();
            Pose();
        }

        /// <summary>
        /// Stepping the same total time as one 0.1s tick or ten 0.01s ticks must land on (very
        /// nearly) the same smoothed value for a fixed target: exp(-rate*dt) composes exactly
        /// across subdivided steps, so this is an exact analytic property, not an approximation.
        /// </summary>
        private static void FrameRateIndependence()
        {
            const float rate = 6f;
            const float target = 100f;

            float bigStep = 0f;
            bigStep = WingviewMath.ExpSmooth(bigStep, target, rate, 0.1f);

            float smallSteps = 0f;
            for (int i = 0; i < 10; i++) smallSteps = WingviewMath.ExpSmooth(smallSteps, target, rate, 0.01f);

            TestAssert.That(System.Math.Abs(bigStep - smallSteps) < 0.01f,
                "Exponential smoothing must be frame-rate independent for a fixed target: " +
                bigStep + " vs " + smallSteps);

            WVVec3 bigVec = WingviewMath.ExpSmooth(WVVec3.Zero, new WVVec3(10f, 20f, -5f), rate, 0.1f);
            WVVec3 smallVec = WVVec3.Zero;
            for (int i = 0; i < 10; i++) smallVec = WingviewMath.ExpSmooth(smallVec, new WVVec3(10f, 20f, -5f), rate, 0.01f);
            TestAssert.That(
                System.Math.Abs(bigVec.X - smallVec.X) < 0.02f &&
                System.Math.Abs(bigVec.Y - smallVec.Y) < 0.02f &&
                System.Math.Abs(bigVec.Z - smallVec.Z) < 0.02f,
                "Vector smoothing must also be frame-rate independent");
        }

        private static void LookAheadClamp()
        {
            TestAssert.That(WingviewMath.LookAheadYaw(1000f) == WingviewMath.LookAheadClampDeg,
                "A large positive yaw rate clamps to +12 deg");
            TestAssert.That(WingviewMath.LookAheadYaw(-1000f) == -WingviewMath.LookAheadClampDeg,
                "A large negative yaw rate clamps to -12 deg");
            float small = WingviewMath.LookAheadYaw(10f);
            TestAssert.That(small > 0f && small < WingviewMath.LookAheadClampDeg,
                "A small yaw rate scales by the gain without hitting the clamp");
            TestAssert.That(WingviewMath.LookAheadYaw(0f) == 0f, "No yaw rate means no lead");
        }

        /// <summary>Straight and level flight (nose and velocity both horizontal) must produce a
        /// horizontal direction, i.e. the composed up vector stays world up.</summary>
        private static void LevelHorizon()
        {
            var nose = new WVVec3(0f, 0f, 1f);
            var velocity = new WVVec3(0f, 0f, 50f);
            WVVec3 dir = WingviewMath.BlendDirection(nose, velocity, true);
            TestAssert.That(System.Math.Abs(dir.Y) < 1e-4f, "Level flight yields a level direction (Y ~ 0)");
            TestAssert.That(System.Math.Abs(dir.Length - 1f) < 1e-4f, "The blended direction is a unit vector");

            // A steep 80 degree climb must not flip the camera over: pitch is halved, so the
            // resulting direction's own pitch must be well under the input pitch and still finite.
            var climbNose = new WVVec3(0f, (float)System.Math.Sin(80.0 * System.Math.PI / 180.0),
                (float)System.Math.Cos(80.0 * System.Math.PI / 180.0));
            WVVec3 climbDir = WingviewMath.BlendDirection(climbNose, WVVec3.Zero, false);
            float climbPitch = (float)System.Math.Asin(System.Math.Min(1f, System.Math.Max(-1f, climbDir.Y))) * 57.2958f;
            TestAssert.That(climbPitch > 0f && climbPitch < 75f,
                "A steep climb is dampened toward level, never amplified or flipped: " + climbPitch);
        }

        private static void NaNSafety()
        {
            WVVec3 dir = WingviewMath.BlendDirection(WVVec3.Zero, WVVec3.Zero, false);
            TestAssert.That(dir.IsFinite, "Zero nose and zero velocity must not produce NaN/Infinity");

            var nanVec = new WVVec3(float.NaN, float.NaN, float.NaN);
            WVVec3 dirFromNaN = WingviewMath.BlendDirection(nanVec, nanVec, true);
            TestAssert.That(dirFromNaN.IsFinite, "NaN input direction falls back instead of propagating NaN");

            TestAssert.That(!float.IsNaN(WingviewMath.LookAheadYaw(float.NaN)), "NaN yaw rate is absorbed, not propagated");
            TestAssert.That(!float.IsNaN(WingviewMath.SmoothingAlpha(float.NaN, 0.1f)) &&
                !float.IsNaN(WingviewMath.SmoothingAlpha(6f, float.NaN)),
                "NaN rate or dt must not poison the smoothing factor");

            WingviewMath.Pose pose = WingviewMath.ComputePose(new WVVec3(0f, 500f, 0f), WVVec3.Forward, WVVec3.Up, float.NaN);
            TestAssert.That(pose.Eye.IsFinite && pose.LookTarget.IsFinite,
                "A non-finite distance is sanitized rather than poisoning the pose");
        }

        private static void Pose()
        {
            var aircraft = new WVVec3(0f, 100f, 0f);
            var dir = new WVVec3(0f, 0f, 1f);
            float distance = 20f;
            WingviewMath.Pose pose = WingviewMath.ComputePose(aircraft, dir, WVVec3.Up, distance);

            TestAssert.That(pose.Eye.Z < aircraft.Z, "The eye sits behind the aircraft along -dir");
            TestAssert.That(pose.Eye.Y > aircraft.Y, "The eye sits above the aircraft (up fraction)");
            TestAssert.That(pose.LookTarget.Z > aircraft.Z, "The look target sits ahead of the aircraft along +dir");

            float distFromVanilla = WingviewMath.FollowDistance(10f, 0f);
            TestAssert.That(distFromVanilla == 22f, "Follow distance matches vanilla's (1 + maxRadius) * 2 at zero zoom");
        }
    }
}
