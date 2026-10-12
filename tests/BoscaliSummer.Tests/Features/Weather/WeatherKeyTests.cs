using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The v8 weather key (spec 9.1): one time function, rebasing, tape identity, frame heading.</summary>
    internal static class WeatherKeyTests
    {
        public static void Run()
        {
            OffsetAtIsOneDoubleFunction();
            RebaseKeepsTheTapeWhereItWas();
            ClockResetKeepsTheTapeWhereItWas();
            TapeIdIgnoresKinematics();
            EqualsCoversEveryField();
            HeadingFromTheMissionWind();
            OpeningBaseQuantising();
            IdleNeverReKeysTheHost();
            GarbageInputsAreSanitised();
        }

        private static WeatherKey K(uint seed = 5u, float epoch = 100f, bool dynamic = true, byte missionClass = 2,
            byte sets = 3, byte layoutSalt = 4, bool hasAnchor = true, float anchorX = 7f, float anchorZ = 8f,
            byte story = 1, byte skyPin = 6, float openingBase = 2400f, float headingDeg = 200f, float offset = 5000f,
            float systemSpeed = 30f, float so1 = 11f, float so2 = 12f, float so3 = 13f)
            => new WeatherKey(seed, epoch, dynamic, missionClass, sets, layoutSalt, hasAnchor, anchorX, anchorZ,
                story, skyPin, openingBase, headingDeg, offset, systemSpeed, so1, so2, so3);

        private static WeatherKey Moving(bool dynamic = true) =>
            new WeatherKey(20260918u, 1000f, dynamic, 2, offset: 120000f, systemSpeed: 27f, headingDeg: 90f);

        private static void OffsetAtIsOneDoubleFunction()
        {
            WeatherKey k = Moving();
            TestAssert.That(k.OffsetAt(1000.0) == 120000.0, "at the epoch the offset is the offset");
            TestAssert.That(Math.Abs(k.OffsetAt(1600.0) - (120000.0 + 27.0 * 600.0)) < 1e-9, "27 m/s for 600 s");
            TestAssert.That(Math.Abs(k.OffsetAt(400.0) - (120000.0 - 27.0 * 600.0)) < 1e-9, "before the epoch it runs backwards");
            TestAssert.That(Moving(false).OffsetAt(99999.0) == 120000.0, "a held tape does not move");

            // 72 hours at 45 m/s from an offset of 3000 km: float arithmetic cannot resolve one second here.
            var far = new WeatherKey(1u, 0f, true, offset: 3_000_000f, systemSpeed: 45f);
            double t = 72.0 * 3600.0;
            TestAssert.That(far.OffsetAt(t) == 3_000_000.0 + 45.0 * t, "72 h sweep is exact in double");
            TestAssert.That(far.OffsetAt(t + 1.0) - far.OffsetAt(t) == 45.0, "one second still moves 45 m after 72 h");
        }

        private static void RebaseKeepsTheTapeWhereItWas()
        {
            WeatherKey run = Moving();
            double was = run.OffsetAt(2000.0);

            WeatherKey held = run.Rebased(2000f, dynamic: false);
            for (double t = 2000.0; t <= 2060.0; t += 5.0)
                TestAssert.That(Math.Abs(held.OffsetAt(t) - was) < 1.0, "Hold freezes D where it was");

            WeatherKey faster = run.Rebased(2000f, systemSpeed: 45f);
            TestAssert.That(Math.Abs(faster.OffsetAt(2000.0) - was) < 1.0, "a pace change is continuous at the instant");
            for (double t = 2000.0; t <= 2060.0; t += 5.0)
                TestAssert.That(Math.Abs(faster.OffsetAt(t) - (was + 45.0 * (t - 2000.0))) < 1.0, "and runs at the new speed after it");

            WeatherKey at40 = run.Rebased(2400f, dynamic: false);
            WeatherKey resumed = at40.Rebased(3000f, dynamic: true);
            TestAssert.That(Math.Abs(resumed.OffsetAt(3000.0) - run.OffsetAt(2400.0)) < 1.0,
                "Hold at 40 min then Resume gives the picture that was held");
            TestAssert.That(resumed.Epoch == 3000f && resumed.Dynamic, "Resume restarts the clock at its own time");

            WeatherKey huge = new WeatherKey(1u, 10f, true, offset: 3_000_000f, systemSpeed: 45f).Rebased(86400f);
            TestAssert.That(Math.Abs(huge.OffsetAt(86400.0) - (3_000_000.0 + 45.0 * 86390.0)) < 0.5,
                "a rebase at 3000 km stays within float spacing");
            TestAssert.That(run.Rebased(2000f).TapeId == run.TapeId && run.Rebased(2000f).HeadingDeg == run.HeadingDeg,
                "a rebase is never a cut");
        }

        private static void ClockResetKeepsTheTapeWhereItWas()
        {
            WeatherKey k = Moving();
            WeatherKey back = k.AfterClockReset(5000.0, 0f);
            TestAssert.That(Math.Abs(back.OffsetAt(0.0) - k.OffsetAt(5000.0)) < 1.0, "the tape does not jump when the clock restarts");
            TestAssert.That(back.Epoch == 0f && back.TapeId == k.TapeId, "the epoch restarts, the tape identity does not");
            WeatherKey held = Moving(false).AfterClockReset(5000.0, 3f);
            TestAssert.That(held.OffsetAt(3.0) == 120000.0, "a held tape stays put across a reset");
        }

        private static void TapeIdIgnoresKinematics()
        {
            WeatherKey a = K();
            uint id = a.TapeId;
            WeatherKey[] kinematic =
            {
                a.Rebased(500f, false, 40f), a.WithSets(63), a.WithAnchor(1f, 2f), a.WithoutAnchor(),
                K(epoch: 999f, dynamic: false, sets: 1, hasAnchor: false, offset: 1e6f, systemSpeed: 18f, so1: 5f, so2: 6f, so3: 7f),
            };
            foreach (WeatherKey k in kinematic)
                TestAssert.That(k.TapeId == id && k.SameTape(a), "kinematic edits never change the tape");

            WeatherKey[] tape = { K(seed: 6u), K(story: 2), K(skyPin: 7), K(layoutSalt: 5), K(missionClass: 3), K(openingBase: 2500f) };
            foreach (WeatherKey k in tape)
                TestAssert.That(k.TapeId != id && !k.SameTape(a), "every tape field changes the tape");
            TestAssert.That(a.WithLayoutSalt(5).TapeId != id, "Re-roll changes the tape");
            TestAssert.That(K(openingBase: 2404f).TapeId == id, "the opening base is compared at 10 m");
            WeatherKey turned = K(headingDeg: 201f);
            TestAssert.That(turned.TapeId == id && !turned.SameTape(a), "the frame is compared beside the tape id");
        }

        private static void EqualsCoversEveryField()
        {
            WeatherKey b = K();
            TestAssert.That(b.Equals(K()) && b.GetHashCode() == K().GetHashCode() && !b.Equals(null), "equal keys are equal");
            WeatherKey[] variants =
            {
                K(seed: 6u), K(epoch: 101f), K(dynamic: false), K(missionClass: 3), K(sets: 4), K(layoutSalt: 5),
                K(hasAnchor: false), K(anchorX: 9f), K(anchorZ: 9f), K(story: 2), K(skyPin: 7), K(openingBase: 2410f),
                K(headingDeg: 201f), K(offset: 5001f), K(systemSpeed: 31f), K(so1: 12f), K(so2: 13f), K(so3: 14f),
            };
            for (int i = 0; i < variants.Length; i++)
                TestAssert.That(!b.Equals(variants[i]), "field #" + i + " is part of key equality");
        }

        private static void HeadingFromTheMissionWind()
        {
            TestAssert.That(Near(WeatherKey.HeadingFromWind(0f, 20f, 20f, 1u), 180f), "wind blowing north comes from 180");
            TestAssert.That(Near(WeatherKey.HeadingFromWind(20f, 0f, 20f, 1u), 270f), "wind blowing east comes from 270");
            TestAssert.That(Near(WeatherKey.HeadingFromWind(-20f, 0f, 20f, 1u), 90f), "wind blowing west comes from 90");
            float south = WeatherKey.HeadingFromWind(0f, -20f, 20f, 1u);
            TestAssert.That(south == 0f, "wind blowing south comes from 0, never 360, got " + south);

            float calm = WeatherKey.SeededHeading(77u);
            TestAssert.That(WeatherKey.HeadingFromWind(0f, 0f, 0.1f, 77u) == calm, "vanilla's forced 0.1 m/s is calm");
            TestAssert.That(WeatherKey.HeadingFromWind(3f, 4f, 0.5f, 77u) == calm, "exactly 0.5 m/s is still calm");
            TestAssert.That(WeatherKey.HeadingFromWind(3f, 4f, 0.51f, 77u) != calm, "above 0.5 m/s the wind decides");
            TestAssert.That(WeatherKey.HeadingFromWind(float.NaN, 4f, 9f, 77u) == calm, "a non-finite wind is calm");
            TestAssert.That(calm >= 0f && calm < 360f && WeatherKey.SeededHeading(78u) != calm, "seeded headings are in range and differ");

            TestAssert.That(new WeatherKey(1u, 0f, true, headingDeg: 359.9999999f).HeadingDeg == 0f, "a heading that rounds to 360 wraps to 0");
            WeatherKey tiny = new WeatherKey(1u, 0f, true, headingDeg: -0.0000001f);
            TestAssert.That(tiny.HeadingDeg >= 0f && tiny.HeadingDeg < 360f, "a tiny negative heading stays in [0, 360)");
            TestAssert.That(new WeatherKey(9u, 0f, true).HeadingDeg == WeatherKey.SeededHeading(9u), "no heading given means the seeded one");
            TestAssert.That(Near(new WeatherKey(1u, 0f, true, headingDeg: 90f).DownwindHeading, 270f), "downwind is 180 from the heading");

            // The legacy cloud orientation is preserved for keys that carry no mission wind.
            for (uint seed = 1; seed < 40; seed++)
            {
                float old = WeatherMath.Hash01(seed, 41) * 360f;
                float now = new WeatherKey(seed, 0f, true).DownwindHeading;
                TestAssert.That(Math.Abs(WeatherMath.DeltaAngle(old, now)) < 0.001f, "seeded downwind matches the old hash heading");
            }
        }

        private static bool Near(float a, float b) => Math.Abs(WeatherMath.DeltaAngle(a, b)) < 0.001f;

        private static void OpeningBaseQuantising()
        {
            TestAssert.That(WeatherKey.QuantiseOpeningBase(1800f) == 0f, "the editor default means none");
            TestAssert.That(WeatherKey.QuantiseOpeningBase(1804f) == 0f, "1804 quantises to the default");
            TestAssert.That(WeatherKey.QuantiseOpeningBase(2400f) == 2400f, "an authored altitude is kept");
            TestAssert.That(WeatherKey.QuantiseOpeningBase(2403f) == 2400f && WeatherKey.QuantiseOpeningBase(2405f) == 2410f, "10 m steps, half rounds up");
            TestAssert.That(WeatherKey.QuantiseOpeningBase(9500f) == 9500f, "it is stored unclamped");
            TestAssert.That(WeatherKey.QuantiseOpeningBase(0f) == 0f && WeatherKey.QuantiseOpeningBase(-50f) == 0f, "zero and negative mean none");
            TestAssert.That(WeatherKey.QuantiseOpeningBase(float.NaN) == 0f && WeatherKey.QuantiseOpeningBase(float.PositiveInfinity) == 0f, "non-finite means none");
        }

        private static void IdleNeverReKeysTheHost()
        {
            // Spec 9.6.7: 600 s of idle updates change neither the key nor the tape. Nothing in Domain re-keys by
            // itself; the one thing that can re-key an idle host is the once-per-second settings reconcile in
            // WeatherManager.UpdateField, and that is exactly TapeReconcile.Needed, called with the same inputs.
            float speed = TapePace.Speed(3, 12f);
            foreach (bool dynamic in new[] { true, false })
            {
                WeatherKey key = new WeatherKey(9u, 0f, dynamic, 4, headingDeg: WeatherKey.SeededHeading(9u), systemSpeed: speed);
                WeatherKey held = key;
                for (float t = 0f; t <= 600f; t += 1f)
                    if (TapeReconcile.Needed(held, dynamic, TapePace.Speed(3, 12f)))
                        held = held.Rebased(t, dynamic, TapePace.Speed(3, 12f));
                TestAssert.That(ReferenceEquals(held, key) && held.TapeId == key.TapeId,
                    "600 s of idle reconciles never re-key (dynamic " + dynamic + ")");
            }

            // A real change is detected once and then settles: no second re-key a second later.
            WeatherKey run = new WeatherKey(9u, 0f, true, 4, systemSpeed: speed);
            float faster = TapePace.Speed(4, 12f);
            TestAssert.That(TapeReconcile.Needed(run, true, faster), "a Pace change is a re-key");
            WeatherKey paced = run.Rebased(100f, true, faster);
            TestAssert.That(!TapeReconcile.Needed(paced, true, faster) && paced.TapeId == run.TapeId &&
                Math.Abs(paced.OffsetAt(100.0) - run.OffsetAt(100.0)) < 1.0, "and settles, continuous and the same tape");
            TestAssert.That(TapeReconcile.Needed(run, false, speed), "a TapeMoves flip is a re-key");
            WeatherKey flipped = run.Rebased(100f, false, speed);
            TestAssert.That(!TapeReconcile.Needed(flipped, false, speed), "and settles");
            TestAssert.That(!TapeReconcile.Needed(run, true, float.NaN) && !TapeReconcile.Needed(run, true, float.PositiveInfinity),
                "a non-finite wanted speed never starts a re-key loop");
        }

        private static void GarbageInputsAreSanitised()
        {
            var k = new WeatherKey(1u, float.NaN, true, offset: float.PositiveInfinity, systemSpeed: float.NaN,
                anchorX: float.NaN, hasAnchor: true, openingBase: -5f);
            TestAssert.That(k.Epoch == 0f && k.Offset == 0f && k.SystemSpeed == WeatherKey.DefaultSystemSpeed && !k.HasAnchor && k.OpeningBase == 0f,
                "non-finite or negative inputs fall back");
            TestAssert.That(k.Equals(k) && k.GetHashCode() == k.GetHashCode(), "a sanitised key compares equal to itself");
            TestAssert.That(new WeatherKey(1u, -0f, true, offset: -0f).Equals(new WeatherKey(1u, 0f, true)), "negative zero equals zero");
            TestAssert.That(new WeatherKey(1u, 0f, true, 9, sets: 0xFF).Sets == Superstructures.AllSets, "sets are masked");
            TestAssert.That(new WeatherKey(1u, 0f, true).MissionClass == WeatherKey.None, "no class means 255");
        }
    }
}
