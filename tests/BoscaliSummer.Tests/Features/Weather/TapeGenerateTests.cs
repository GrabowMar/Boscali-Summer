using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The tape generator (spec 5.1, 5.2), its bake (7.2) and the mission-led opening (5.5) with invariants 3 and 4 of 9.6: 500-seed sweeps, 100%.</summary>
    internal static class TapeGenerateTests
    {
        private const float HalfX = 81920f, HalfZ = 40960f;

        public static void Run()
        {
            GenerateIsAPureFunctionOfTheTapeIdentity();
            ShapeRules();
            OpeningRunIsOneRow();
            RampsKeepTheCapsAndTheBakeIsExact();
            FrontBookkeeping();
            Invariant3ArrivalByConstruction();
            Invariant4OpeningClass();
            PinnedStoryAndPinnedSky();
        }

        private static WeatherKey Key(uint seed, byte cls = WeatherKey.None, byte story = WeatherKey.None, byte pin = WeatherKey.None, float openingBase = 0f,
            float heading = 270f, float speed = 27f, float offset = 0f, bool dynamic = true, byte salt = 0, float epoch = 0f) =>
            new WeatherKey(seed, epoch, dynamic, cls, 0, salt, false, 0f, 0f, story, pin, openingBase, heading, offset, speed);

        private static void GenerateIsAPureFunctionOfTheTapeIdentity()
        {
            // The grid of spec 10.1: 500 seeds x 7 classes x {auto, a pinned story or sky} x OpeningBase {0, non-zero}.
            for (uint seed = 1; seed <= 500; seed++)
                for (byte cls = 0; cls < WeatherKey.ClassCount; cls++)
                    for (int pinned = 0; pinned < 2; pinned++)
                        for (int open = 0; open < 2; open++)
                        {
                            byte story = pinned == 1 && seed % 2 == 0 ? (byte)(seed % WeatherStories.Count) : WeatherKey.None;
                            byte pin = pinned == 1 && story == WeatherKey.None ? (byte)(seed % SkyLibrary.Count) : WeatherKey.None;
                            WeatherKey a = Key(seed, cls, story, pin, openingBase: open == 0 ? 0f : 600f + seed * 3u % 1900u);
                            // Dynamic, offset, epoch, speed, sets and anchor never reach Generate (the front rule included).
                            var b = new WeatherKey(seed, 500f, !a.Dynamic, cls, 31, 0, true, 100f, 200f, story, pin, a.OpeningBase, a.HeadingDeg, 123456f, 41f);
                            TestAssert.That(a.TapeId == b.TapeId, "kinematics are not tape identity");
                            uint ha = TapeGolden.Hash(WeatherTape.Generate(TapeInputs.From(a))), hb = TapeGolden.Hash(WeatherTape.Generate(TapeInputs.From(b)));
                            TestAssert.That(ha == hb, "byte-identical tapes, seed " + seed + " class " + cls);
                            // The two-peer rule: a key rebuilt from wire fields alone gives the same tape.
                            WeatherKey client = WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(a));
                            TestAssert.That(TapeGolden.Hash(WeatherTape.Generate(TapeInputs.From(client))) == ha, "host and client tapes agree, seed " + seed + " class " + cls);
                            if (seed <= 40 && open == 1) TestAssert.That(ReferenceEquals(WeatherTape.For(a), WeatherTape.For(b)), "one cached object serves both keys");
                        }
            // OpeningBase enters the identity in 10 m steps and Generate reads the same steps: one TapeId is one tape.
            for (uint seed = 1; seed <= 60; seed++)
            {
                WeatherKey near = Key(seed, (byte)(seed % 7), openingBase: 1501f), far = Key(seed, (byte)(seed % 7), openingBase: 1504f);
                TestAssert.That(near.TapeId == far.TapeId && TapeGolden.Hash(WeatherTape.Generate(TapeInputs.From(near))) == TapeGolden.Hash(WeatherTape.Generate(TapeInputs.From(far))),
                    "bases 1501 m and 1504 m share a TapeId and a tape (seed " + seed + ")");
            }
            WeatherKey salted = Key(5u, 2, salt: 1), plain = Key(5u, 2);
            TestAssert.That(TapeGolden.Hash(WeatherTape.Generate(TapeInputs.From(salted))) != TapeGolden.Hash(WeatherTape.Generate(TapeInputs.From(plain))), "Re-roll changes the tape");
        }

        private static void ShapeRules()
        {
            for (uint seed = 1; seed <= 120; seed++)
                foreach (byte story in new byte[] { WeatherKey.None, 0, 1, 2, 3, 4 })
                {
                    WeatherTape t = WeatherTape.Generate(TapeInputs.From(Key(seed, (byte)(seed % 7), story)));
                    TapeKeyframe[] k = t.Keyframes;
                    TestAssert.That(k.Length >= 2 && k.Length <= WeatherTape.MaxKeyframes, "at most 256 keyframes (got " + k.Length + ")");
                    TestAssert.That(k[0].S == 0f && k[k.Length - 1].S < WeatherTape.SpanMetres, "the first keyframe starts at 0, the last inside the span");
                    for (int i = 1; i < k.Length; i++)
                    {
                        TestAssert.That(k[i].Width >= RampWidth.MinMetres - 1f, "no ramp under 12 km");
                        TestAssert.That(i == 1 || k[i - 1].RampEnd <= k[i].RampStart + 1f, "ramps never overlap");
                        TestAssert.That(k[i].S - k[i - 1].S >= RampWidth.MinMetres - 1f, "no segment under 12 km");
                    }
                    foreach (TapeMarker m in t.Markers.Items)
                    {
                        float need = FrontLine.MinSegmentMetres(m.Kind);
                        float after = m.Boundary + 1 < k.Length ? k[m.Boundary + 1].S - m.S : need, before = m.S - k[m.Boundary - 1].S;
                        TestAssert.That(after >= need - 1f && before >= need - 1f, m.Kind + " marker keeps its clearance");
                        TestAssert.That(m.Strength >= 0.7f && m.Strength <= 1f, "marker strength 0.7-1");
                    }
                    TestAssert.That(t.MeanderAmplitude >= 6000f && t.MeanderAmplitude <= 14000f && t.MeanderWavelength >= 8.1f * t.MeanderAmplitude - 1f && t.MeanderWavelength >= 70000f, "meander ranges");
                    if (story == WeatherKey.None)
                    {
                        // The tail: at least 300 km of high pressure before the end of the span.
                        int tail = k.Length - 1;
                        while (tail > 0 && (SkyLibrary.Get(k[tail - 1].SkyIndex).Roles & Role.HighPress) != 0) tail--;
                        TestAssert.That(k[tail].S <= 2700001f, "the tail starts by 2700 km (got " + (k[tail].S / 1000f).ToString("0") + ")");
                        TestAssert.That(WeatherTape.SpanMetres - k[tail].S >= WeatherTape.TailMinMetres - 1f || k[tail].S < 2700001f, "a high-pressure tail of 300 km at the end");
                    }
                }
        }

        private static void OpeningRunIsOneRow()
        {
            for (uint seed = 1; seed <= 100; seed++)
            {
                WeatherTape t = WeatherTape.Generate(TapeInputs.From(Key(seed, (byte)(seed % 7), openingBase: seed % 3 == 0 ? 2400f : 0f)));
                TestAssert.That(t.OpeningEnd >= WeatherTape.OpeningRunMinMetres, "the opening run is at least 250 km to its end boundary");
                var sampler = new TapeSampler(t.Baked);
                SkyParams first = sampler.At(0f);
                float last = t.Keyframes[1].RampStart;
                for (int i = 1; i <= 20; i++)
                {
                    SkyParams row = sampler.At(last * i / 20f - 800f);
                    TestAssert.That(row.CloudBase == first.CloudBase && row.SheetCover == first.SheetCover && row.Mid.Cover == first.Mid.Cover && row.High.Cover == first.High.Cover,
                        "the row is constant up to the end ramp's start (seed " + seed + ")");
                }
            }
        }

        private static void RampsKeepTheCapsAndTheBakeIsExact()
        {
            double worstRate = 0;
            for (uint seed = 1; seed <= 40; seed++)
            {
                WeatherTape t = WeatherTape.Generate(TapeInputs.From(Key(seed, (byte)(seed % 7), (byte)(seed % 5 == 0 ? seed % 5 : 255))));
                BakedTape b = t.Baked;
                var sampler = new TapeSampler(b);
                for (int i = 1; i < b.Texels; i++)
                {
                    SkyParams p = b.Texel(i - 1), q = b.Texel(i);
                    float d = Math.Max(Math.Max(Math.Abs(p.SheetCover - q.SheetCover), Math.Abs(p.CumulusCover - q.CumulusCover)),
                        Math.Max(Math.Abs(p.Mid.Cover - q.Mid.Cover), Math.Abs(p.High.Cover - q.High.Cover)));
                    worstRate = Math.Max(worstRate, d / b.TexelMetres * 45.0);
                }
                for (float s = 0f; s < 3000000f; s += 997f)
                {
                    SkyParams exact = TapeBaker.Exact(t, s), got = sampler.At(s);
                    TestAssert.That(Math.Abs(exact.SheetCover - got.SheetCover) <= 0.005f && Math.Abs(exact.Mid.Cover - got.Mid.Cover) <= 0.005f &&
                        Math.Abs(exact.High.Cover - got.High.Cover) <= 0.005f && Math.Abs(exact.CumulusCover - got.CumulusCover) <= 0.005f,
                        "the baked array is within 0.005 of the exact keyframe function (seed " + seed + ", s " + s.ToString("0") + ")");
                }
                TestAssert.That(b.DeckAltitude > 300f && b.DeckAltitude < 4000f, "a deck altitude from the tape");
            }
            TestAssert.That(worstRate <= 0.004 * 1.06, "cover changes at most 0.004/s at 45 m/s (got " + worstRate.ToString("0.00000") + ")");
            TestAssert.That(Math.Abs(TapeBaker.DeckAltitude(new TapeKeyframe[0]) - 1700f) < 0.01f, "a tape with no low layer uses the library deck, 1700 m");
        }

        private static void FrontBookkeeping()
        {
            for (uint seed = 1; seed <= 80; seed++)
                foreach (byte story in new byte[] { 0, 1, 3 })
                {
                    WeatherTape t = WeatherTape.Generate(TapeInputs.From(Key(seed, 1, story)));
                    TestAssert.That(t.HasFront && t.Fronts[0].SLead > 0f && t.Fronts[0].Marker >= 0, "front story " + story + " bears a front with a marker");
                    TapeFront f = t.Fronts[0];
                    if (story == 1) TestAssert.That(f.SMain == f.SLead && t.Markers.Items[f.Marker].Kind != MarkerKind.Warm, "a cold front's main change is its marker");
                    else TestAssert.That(f.Dpre >= 55000f && f.Dpre <= 90000f, "Dpre is about 65-80 km (story " + story + ", got " + (f.Dpre / 1000f).ToString("0.0") + " km)");
                }
            TestAssert.That(!WeatherTape.Generate(TapeInputs.From(Key(9u, 3))).OpensWithFront && WeatherTape.Generate(TapeInputs.From(Key(9u, 0))).OpensWithFront, "the front rule fires for Clear and Fair only");
            TestAssert.That(WeatherTape.Generate(TapeInputs.From(Key(9u, 1, 2))).Fronts.Length == 0, "a convective day has no front");
        }

        private static float CentreS(WeatherTape t, WeatherKey key, double time) =>
            t.Frame(key.HeadingDeg, (float)key.OffsetAt(time)).S(0f, 0f);

        /// <summary>Invariant 3: with Dynamic on and a front-bearing tape the centre reaches s_ev within 1 m at Epoch + 60*T1, shows the opening run's one row before, and a changed row after.</summary>
        private static void Invariant3ArrivalByConstruction()
        {
            foreach (float v in new[] { 18f, 23f, 27f, 31f, 45f })
                for (uint seed = 1; seed <= 500; seed++)
                {
                    float heading = seed * 137.5f % 360f;
                    byte cls = (byte)(seed % 2);
                    WeatherKey probe = Key(seed, cls, heading: heading, speed: v);
                    WeatherTape t = WeatherTape.For(probe);
                    OpeningPlan plan = TapeOpening.Solve(t, probe, true);
                    TestAssert.That(plan.Front && t.HasFront, "a Clear or Fair mission opens with a front");
                    WeatherKey key = Key(seed, cls, heading: heading, speed: v, offset: plan.Offset);
                    TestAssert.That(Math.Abs(CentreS(t, key, 60.0 * plan.T1Minutes) - t.SEv) < 1f, "the centre reaches s_ev at T1 (seed " + seed + ", v " + v + ")");
                    float tm = plan.TmMinutes;
                    if (t.Fronts[0].SMain != t.Fronts[0].SLead)
                        TestAssert.That(Math.Abs(CentreS(t, key, 60.0 * tm) - t.Fronts[0].SMain) < 1f, "the warm body reaches the centre at Tm");
                    TestAssert.That(plan.T1Minutes >= 15f && plan.T1Minutes <= 60f && tm >= plan.T1Minutes, "T1 and Tm in range");
                    var sampler = new TapeSampler(t.Baked);
                    SkyParams run = sampler.At(CentreS(t, key, 0.0));
                    for (double sec = 0.0; sec < 60.0 * plan.T1Minutes; sec += 10.0)
                    {
                        SkyParams row = sampler.At(CentreS(t, key, sec));
                        TestAssert.That(Math.Abs(row.SheetCover - run.SheetCover) < 0.01f && Math.Abs(row.High.Cover - run.High.Cover) < 0.01f && Math.Abs(row.CloudBase - run.CloudBase) < 25f, "the centre shows the opening run before T1 (to the texel lerp at the ramp start)");
                    }
                    SkyParams after = sampler.At(CentreS(t, key, 60.0 * plan.T1Minutes + 1500.0));
                    TestAssert.That(after.SheetCover != run.SheetCover || after.High.Cover != run.High.Cover || after.Mid.Cover != run.Mid.Cover || after.CloudBase != run.CloudBase, "the row changes after T1");
                }
        }

        /// <summary>Invariant 4: the class of the centre's row at Epoch equals the mission class, for every class, 500 seeds each, 100%; downwind points are within one class.</summary>
        private static void Invariant4OpeningClass()
        {
            for (int cls = 0; cls < WeatherKey.ClassCount; cls++)
                for (uint seed = 1; seed <= 500; seed++)
                {
                    float heading = seed * 61.8f % 360f;
                    WeatherKey probe = Key(seed * 7u + (uint)cls, (byte)cls, heading: heading, openingBase: seed % 4 == 0 ? 2400f : seed % 4 == 1 ? 600f : 0f);
                    WeatherTape t = WeatherTape.For(probe);
                    OpeningPlan plan = TapeOpening.Solve(t, probe, true);
                    WeatherKey key = Key(probe.Seed, (byte)cls, heading: heading, openingBase: probe.OpeningBase, offset: plan.Offset);
                    var sampler = new TapeSampler(t.Baked);
                    TapeFrame frame = t.Frame(heading, plan.Offset);
                    SkyParams row = sampler.At(frame.S(0f, 0f));
                    TestAssert.That(SkyMath.ClassOf(row) == (WeatherRegimeType)cls, "the centre's nominal class is the mission class (class " + cls + ", seed " + seed + ")");
                    WeatherMath.HeadingToVector(heading, out float ux, out float uz);
                    foreach (float[] p in new[] { new[] { -HalfX, -HalfZ }, new[] { HalfX, -HalfZ }, new[] { -HalfX, HalfZ }, new[] { HalfX, HalfZ }, new[] { -ux * 91000f, -uz * 91000f } })
                    {
                        if (p[0] * ux + p[1] * uz > 0f) continue;
                        int near = (int)SkyMath.ClassOf(sampler.At(frame.S(p[0], p[1])));
                        TestAssert.That(Math.Abs(near - cls) <= 1, "points at or downwind of the centre are within one class (front openings included)");
                    }
                }
        }

        private static void PinnedStoryAndPinnedSky()
        {
            WeatherKey pin = Key(11u, 2, pin: 36);
            WeatherTape one = WeatherTape.For(pin);
            TestAssert.That(one.Keyframes.Length == 1 && one.Keyframes[0].SkyIndex == 36 && one.OpeningEnd == 0f && !one.HasFront, "a sky pin is one keyframe and no run");
            OpeningPlan p = TapeOpening.Solve(one, pin, true);
            TestAssert.That(p.Offset >= 0f && p.Offset <= 250000f && !p.Front, "a sky pin takes an offset in [0, 250] km");
            WeatherKey story = Key(12u, 2, story: 0);
            WeatherTape st = WeatherTape.For(story);
            TestAssert.That(st.OpeningEnd == 0f && st.HasFront && st.Segments[0].Role == Role.Ridge, "a pinned story starts with its own first segment");
            OpeningPlan held = TapeOpening.Solve(st, story, false);
            TestAssert.That(held.Offset >= 0f && held.Offset <= 100000f && !held.Front, "a held pinned story takes [0, 100] km");
            TestAssert.That(TapeOpening.Solve(st, story, true).Front, "a pinned front-bearing story places a front when asked");
        }
    }
}
