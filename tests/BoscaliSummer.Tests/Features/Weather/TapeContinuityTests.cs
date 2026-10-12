using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// Invariant 7 (spec 9.6): no automatic path cuts the tape. A rebase never moves D at the instant it happens; Hold at 40 minutes and Resume later shows the identical picture;
    /// 600 s of idle updates change neither the key nor the TapeId, at any rolled heading; a key round trip through the wire reproduces <c>OffsetAt</c> within a metre.
    /// </summary>
    internal static class TapeContinuityTests
    {
        public static void Run()
        {
            RebaseNeverMovesD();
            HoldThenResumeIsTheSamePicture();
            IdleUpdatesChangeNothing();
            WireRoundTripKeepsOffsetAt();
            AutomaticPathsAreNotCuts();
        }

        private static WeatherKey Moving(uint seed, float heading = 200f) =>
            new WeatherKey(seed, 100f, true, 2, headingDeg: heading, offset: -40000f + 7000f * seed, systemSpeed: 18f + (seed % 4) * 9f);

        private static void RebaseNeverMovesD()
        {
            for (uint seed = 1; seed <= 40; seed++)
            {
                WeatherKey key = Moving(seed);
                float now = 400f + seed * 37f;
                foreach (WeatherKey next in new[] { key.Rebased(now), key.Rebased(now, false), key.Rebased(now, true, 45f), key.Rebased(now, null, 18f) })
                    TestAssert.That(Math.Abs(next.OffsetAt(now) - key.OffsetAt(now)) < 1.0, "a rebase leaves D where it was at the instant it happens (seed " + seed + ")");
                WeatherKey same = key.Rebased(now);
                for (double t = now; t <= now + 60.0; t += 5.0)
                    TestAssert.That(Math.Abs(same.OffsetAt(t) - key.OffsetAt(t)) < 1.0, "a rebase that changes nothing keeps D for the next 60 s");
                WeatherKey reset = key.AfterClockReset(now, 10f);
                TestAssert.That(Math.Abs(reset.OffsetAt(10.0) - key.OffsetAt(now)) < 1.0, "a clock that steps back keeps the tape where it was");
            }
        }

        /// <summary>The picture is the field at a time with a key: Hold at 2400 s, Resume at 3000 s. While held D does not move, so the sky at the resume instant is the sky at the hold instant.</summary>
        private static void HoldThenResumeIsTheSamePicture()
        {
            for (uint seed = 1; seed <= 12; seed++)
            {
                WeatherKey running = Moving(seed);
                WeatherKey held = running.Rebased(2400f, false);
                WeatherKey resumed = held.Rebased(3000f, true);
                var before = new WeatherField(); var after = new WeatherField(); var during = new WeatherField();
                before.Build(running, 2400f, 81920f, 40960f, 12f, 1f, true);
                during.Build(held, 2700f, 81920f, 40960f, 12f, 1f, true);
                after.Build(resumed, 3000f, 81920f, 40960f, 12f, 1f, true);
                for (int i = 0; i < 24; i++)
                {
                    float x = WeatherMath.HashRange(seed, i, 1, 0, -70000f, 70000f), z = WeatherMath.HashRange(seed, i, 2, 0, -40000f, 40000f);
                    WeatherPoint a = before.Sample(x, z), b = during.Sample(x, z), c = after.Sample(x, z);
                    TestAssert.That(a.SkyCover == b.SkyCover && b.SkyCover == c.SkyCover && a.RainRate == c.RainRate && a.CloudBase == c.CloudBase && a.WindX == c.WindX,
                        "Hold at 40 minutes then Resume gives the identical picture (seed " + seed + ", point " + i + ")");
                }
                WeatherField later = new WeatherField();
                later.Build(resumed, 3060f, 81920f, 40960f, 12f, 1f, true);
                TestAssert.That(Math.Abs(later.Frame.Offset - after.Frame.Offset - resumed.SystemSpeed * 60f) < 0.5f, "and it moves again after the resume");
            }
        }

        /// <summary>
        /// The 1 Hz reconcile of <c>UpdateField</c> as the manager writes it: the key is built the way <c>NewFieldKey</c> builds it (Dynamic from TapeMoves, SystemSpeed from <c>TapePace.Speed(pace, windSpeed)</c>), round-tripped through
        /// the wire, and every second the wanted state is recomputed from the settings and compared with the key. An idle host never re-keys (a needless re-key restarts the step counter and broadcasts); a changed pace or Hold is
        /// noticed, and the rebase it causes leaves D where it was.
        /// </summary>
        private static void IdleUpdatesChangeNothing()
        {
            var rng = new Random(20261012);
            int noticed = 0;
            for (int run = 0; run < 60; run++)
            {
                int pace = 1 + run % 6;
                float windSpeed = 0.1f + (float)(rng.NextDouble() * 24.9);   // 0.1-25 m/s, the calm of 0.1 to a gale
                bool tapeMoves = run % 5 != 4;
                float heading = (float)(rng.NextDouble() * 360.0);
                var built = new WeatherKey((uint)(run + 1), 100f, tapeMoves, 2, headingDeg: heading, offset: -40000f + 7000f * (run % 9), systemSpeed: TapePace.Speed(pace, windSpeed));
                WeatherKey live = WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(built));
                uint tapeId = live.TapeId;
                for (int second = 0; second < 600; second++)
                {
                    bool wantDynamic = tapeMoves;                       // settings.TapeMoves.Value
                    float wantSpeed = TapePace.Speed(pace, windSpeed);   // recomputed every second, as the reconcile does
                    TestAssert.That(!TapeReconcile.Needed(live, wantDynamic, wantSpeed), "an idle second asks for no re-key (run " + run + ", second " + second + ", heading " + heading + ")");
                    WeatherKey echoed = WeatherKeyWire.Reuse(live, WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(live)));
                    TestAssert.That(ReferenceEquals(echoed, live), "an idle resend returns the same instance");
                }
                TestAssert.That(live.Equals(built) && live.TapeId == tapeId && live.HeadingDeg == heading, "600 s of idle updates change neither the key nor the TapeId (heading " + heading + ")");
                TestAssert.That(TapeReconcile.Needed(live, !tapeMoves, live.SystemSpeed), "Hold or Resume is noticed");
                float otherSpeed = TapePace.Speed(pace % 6 + 1, windSpeed);
                if (Math.Abs(otherSpeed - live.SystemSpeed) < 1e-3f) continue;   // two paces that clamp to the same speed (18 or 45 m/s) are one setting
                noticed++;
                TestAssert.That(TapeReconcile.Needed(live, tapeMoves, otherSpeed), "a changed pace is noticed (run " + run + ")");
                WeatherKey rebased = live.Rebased(700f, null, otherSpeed);
                TestAssert.That(Math.Abs(rebased.OffsetAt(700.0) - live.OffsetAt(700.0)) < 1.0 && !TapeReconcile.Needed(rebased, tapeMoves, otherSpeed) && rebased.SameTape(live),
                    "the rebase leaves D where it was, settles the reconcile and keeps the tape");
                TestAssert.That(Math.Abs(rebased.OffsetAt(760.0) - live.OffsetAt(700.0) - (tapeMoves ? otherSpeed * 60.0 : 0.0)) < 1.0, "and D moves at the new speed from then on");
            }
            TestAssert.That(noticed >= 30, "the pace change was exercised (" + noticed + " of 60 runs)");
        }

        private static void WireRoundTripKeepsOffsetAt()
        {
            foreach (float offset in new[] { -250000f, 0f, 3.2e6f, 123456.78f })
                foreach (bool dynamic in new[] { true, false })
                {
                    var key = new WeatherKey(77u, 513.25f, dynamic, 3, offset: offset, systemSpeed: 31.7f, headingDeg: 359.9f);
                    WeatherKey back = WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(key));
                    for (double t = 400.0; t <= 4000.0; t += 300.0)
                        TestAssert.That(Math.Abs(back.OffsetAt(t) - key.OffsetAt(t)) < 1.0, "a wire round trip reproduces OffsetAt within a metre (offset " + offset + ", t " + t + ")");
                    TestAssert.That(back.Equals(key) && back.TapeId == key.TapeId, "and the whole key");
                }
        }

        /// <summary>Every path that runs without the host asking: the 1 Hz settings reconcile, a clock reset, the held re-key, a pace press, a join and the 8 s resend.</summary>
        private static void AutomaticPathsAreNotCuts()
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                WeatherKey key = Moving(seed, seed * 41f % 360f);
                uint tapeId = key.TapeId;
                WeatherKey[] automatic =
                {
                    key.Rebased(500f, !key.Dynamic, 40f),
                    key.AfterClockReset(900.0, 3f),
                    TapeSwitch.Held(key, key.Rebased(500f, false)),
                    TapeSwitch.Plan(key, false, 500f, 27f).Key,
                    TapeSwitch.Pace(key, 500f, 45f),
                    WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(key)),
                };
                foreach (WeatherKey next in automatic)
                    TestAssert.That(next.TapeId == tapeId && next.HeadingDeg == key.HeadingDeg && next.SameTape(key), "no automatic path changes the tape identity (seed " + seed + ")");
            }
        }
    }
}
