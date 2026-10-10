using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The baked tape and its CPU reader (spec 7.2), the tape frame (3.1) and the hand-built two-keyframe fixture tape (11 item 2).</summary>
    internal static class TapeBakeTests
    {
        public static void Run()
        {
            ConstantTapeIsOneTexel();
            TwoKeyframesFollowTheExactRamp();
            ChordsAndPresenceFollowTheLayers();
            FrameIsAStrictlyIncreasingAxis();
            SamplerClampsAtTheEnds();
        }

        private static SkyParams Humilis(float lobe = 1f, float midCover = 0f, float midBase = 4200f)
        {
            var s = new SkyParams { LobeRung = lobe, PuffDepth = 500f, BaseWobble = 30f, CloudBase = 1500f, Dome = 0.9f };
            s.Mid = new SkyLayer { Base = midBase, Thick = 500f, Cover = midCover, PeriodRung = 4f, Detail = 0.7f, Strength = 0.36f, Streaks = 1f };
            return s;
        }

        private static void ConstantTapeIsOneTexel()
        {
            var tape = new BakedTape(1);
            SkyParams a = Humilis(1f);
            tape.SetConstant(a);
            int revision = tape.Revision;
            tape.SetConstant(a);
            TestAssert.That(tape.Revision == revision, "the same row again changes nothing, so the texture is not rebuilt");
            tape.SetConstant(Humilis(2f));
            TestAssert.That(tape.Revision == revision + 1, "a changed row bumps the revision once");
            var sampler = new TapeSampler(tape);
            TestAssert.That(sampler.IsConstant && sampler.At(-1e6f).LobeRung == 2f && sampler.At(0f).LobeRung == 2f && sampler.At(9e9f).LobeRung == 2f,
                "a one-texel tape is the same row at every s");
            TestAssert.That(TapeSampler.Constant(a).At(5f).Dome == 0.9f, "TapeSampler.Constant wraps a row");
            bool threw = false;
            try { new BakedTape(2).SetConstant(a); } catch (InvalidOperationException) { threw = true; }
            TestAssert.That(threw, "SetConstant is for one-texel tapes only");
        }

        private static void TwoKeyframesFollowTheExactRamp()
        {
            SkyParams a = Humilis(1f, 0f), b = Humilis(5f, 0.8f, 3300f);
            const float boundary = 1500000f, ramp = 12000f;
            BakedTape tape = BakedTape.TwoKeyframes(a, b, boundary, ramp);
            var sampler = new TapeSampler(tape);
            TestAssert.That(tape.Texels == BakedTape.FullTexels && Math.Abs(tape.TexelMetres - 732.42f) < 0.01f, "4096 texels over 3000 km");
            TestAssert.That(sampler.At(boundary - 20000f).LobeRung == 1f && sampler.At(boundary + 20000f).LobeRung == 5f, "each keyframe holds on its side");
            SkyParams middle = sampler.At(boundary);
            TestAssert.That(Math.Abs(middle.LobeRung - 3f) < 0.02f && Math.Abs(middle.Mid.Cover - 0.4f) < 0.01f, "the ramp midpoint is the half-way row");
            // The baked array stays within 0.005 of the exact smoothstep at every point of the ramp (spec 5.2).
            float worst = 0f, previous = 0f;
            for (float s = boundary - 7000f; s <= boundary + 7000f; s += 61f)
            {
                float exact = WeatherMath.Smoothstep(boundary - 0.5f * ramp, boundary + 0.5f * ramp, s);
                float baked = (sampler.At(s).LobeRung - 1f) / 4f;
                worst = Math.Max(worst, Math.Abs(baked - exact));
                TestAssert.That(baked >= previous - 1e-5f, "the ramp never goes backwards");
                previous = baked;
            }
            TestAssert.That(worst < 0.005f, "bake error " + worst + " is under 0.005");
            TestAssert.That(Math.Abs(sampler.At(boundary + 100f).Mid.Base - 3300f) < 800f && sampler.At(boundary + 100f).Mid.Base < 4200f,
                "a layer's base ramps with its keyframes");
            // Inheritance: layer A has no mid deck, so the deck fades in at B's altitude, never sweeping up from 4200 to 3300 through the ramp.
            for (float s = boundary - 6000f; s <= boundary + 6000f; s += 500f)
                TestAssert.That(Math.Abs(sampler.At(s).Mid.Base - 3300f) < 1e-2f || sampler.At(s).Mid.Base == 4200f,
                    "an absent layer inherits its neighbour's base across the ramp");
            // The cumulus level is recomputed from the lerped cover and capacity at every texel, not lerped: with cumulus cover 0 and 0.3
            // (capacity 0.36) the level differs from the lerp of the two levels by 0.03-0.10 anywhere near the midpoint.
            SkyParams dry = Humilis(1f), heaped = Humilis(1f);
            dry.CumulusCover = 0f; heaped.CumulusCover = 0.3f; dry.CapacityCover = heaped.CapacityCover = 0.36f;
            BakedTape cumulus = BakedTape.TwoKeyframes(dry, heaped, boundary, ramp);
            int mid = (int)(boundary / cumulus.TexelMetres);
            float wMid = WeatherMath.Smoothstep(boundary - 0.5f * ramp, boundary + 0.5f * ramp, (mid + 0.5f) * cumulus.TexelMetres);
            float recomputed = CumulusLevels.For(0.3f * wMid, 0.36f), lerped = CumulusLevels.For(0.3f, 0.36f) * wMid;
            TestAssert.That(Math.Abs(cumulus.Texel(mid).CumulusLevel - recomputed) < 1e-5f && Math.Abs(recomputed - lerped) > 0.03f &&
                Math.Abs(cumulus.Texel(mid).CumulusLevel - lerped) > 0.03f, "the cumulus level is recomputed per texel (" + cumulus.Texel(mid).CumulusLevel + "), not lerped (" + lerped + ")");
            var cumulusSampler = new TapeSampler(cumulus);
            float texel = cumulus.TexelMetres;
            TestAssert.That(cumulusSampler.At((mid + 0.75f) * texel).CumulusLevel == cumulus.Texel(mid).CumulusLevel &&
                cumulusSampler.At((mid + 1.25f) * texel).CumulusLevel == cumulus.Texel(mid + 1).CumulusLevel,
                "between two texels the sampler takes the nearer texel's level instead of lerping it");
            // Aspect and ripple wavelength are held across the ramp: every baked texel carries one keyframe's value, never a value between.
            SkyParams streakA = Humilis(1f, 0.5f), streakB = Humilis(1f, 0.5f);
            streakA.Mid.Aspect = 5f; streakB.Mid.Aspect = 3f; streakA.Mid.RippleWavelength = 900f; streakB.Mid.RippleWavelength = 600f;
            BakedTape held = BakedTape.TwoKeyframes(streakA, streakB, boundary, ramp);
            for (int i = 0; i < held.Texels; i++)
            {
                SkyLayer l = held.Texel(i).Mid;
                TestAssert.That((l.Aspect == 5f || l.Aspect == 3f) && (l.RippleWavelength == 900f || l.RippleWavelength == 600f),
                    "texel " + i + " holds a keyframe's aspect and ripple wavelength through the ramp: " + l.Aspect + ", " + l.RippleWavelength);
            }
        }

        private static void ChordsAndPresenceFollowTheLayers()
        {
            SkyParams a = Humilis(1f, 0.3f, 4200f), b = Humilis(1f, 1f, 3300f);
            b.Mid.Thick = 1800f;
            BakedTape tape = BakedTape.TwoKeyframes(a, b, 1500000f, 12000f);
            TestAssert.That(tape.MidPresent && !tape.HighPresent, "the mid deck is present, the high deck is not");
            TestAssert.That(Math.Abs(tape.MidChord0 - 3300f) < 1f && Math.Abs(tape.MidChord1 - 5100f) < 1f, "the chord spans the lowest base to the highest top: " + tape.MidChord0 + ".." + tape.MidChord1);
            TestAssert.That(tape.MidChordRatio > 3f && tape.MidChordRatio <= 4f,
                "chord thickness over the thinnest local layer is 3.6, inside the spec's limit of 4 (got " + tape.MidChordRatio + ")");
            // Every texel whose layer shows at all (cover 0.005, the march's own gate) lies inside the chord, even where the layer is
            // fading in or out below the 0.02 that decides whether the tape has the layer.
            for (int i = 0; i < tape.Texels; i++)
            {
                SkyLayer l = tape.Texel(i).Mid;
                if (l.Cover < 0.005f) continue;
                TestAssert.That(l.Base >= tape.MidChord0 - 1f && l.Top <= tape.MidChord1 + 1f, "texel " + i + " of a fading layer sits inside the chord");
            }
            SkyParams absent = Humilis(1f, 0f, 4200f), present = Humilis(1f, 0.8f, 3300f);
            BakedTape fadeIn = BakedTape.TwoKeyframes(absent, present, 1500000f, 12000f);
            for (int i = 0; i < fadeIn.Texels; i++)
            {
                SkyLayer l = fadeIn.Texel(i).Mid;
                if (l.Cover >= 0.005f)
                    TestAssert.That(l.Base >= fadeIn.MidChord0 - 1f && l.Top <= fadeIn.MidChord1 + 1f, "a layer fading in keeps its altitude inside the chord");
            }
            var none = new BakedTape(1);
            none.SetConstant(Humilis());
            TestAssert.That(!none.MidPresent && none.MidChord1 == 0f, "a tape with no layer has no chord");
        }

        private static void FrameIsAStrictlyIncreasingAxis()
        {
            var flat = new TapeFrame(90f, 1000f);
            TestAssert.That(Math.Abs(flat.S(500f, 0f) - 1500f) < 1e-3f && Math.Abs(flat.S(0f, 700f) - 1000f) < 1e-3f,
                "heading 90 degrees: the axis points east, s grows to the east, D shifts it");
            var frame = new TapeFrame(37f, 0f, 14000f, 100000f, 1.3f);
            TestAssert.That(frame.Wavelength >= 8.1f * 14000f, "the wavelength is held at 8.1 amplitudes so the meander slope stays under 1");
            var rng = new Random(5);
            for (int i = 0; i < 1000; i++)
            {
                float a = (float)(rng.NextDouble() - 0.5) * 400000f;
                float prev = float.NegativeInfinity;
                for (float along = -50000f; along <= 50000f; along += 777f)
                {
                    // a point at 'along' on the axis and 'a' across it
                    float x = along * frame.UX - a * frame.UZ, z = along * frame.UZ + a * frame.UX;
                    float s = frame.S(x, z);
                    TestAssert.That(s > prev, "s is strictly increasing along the axis for every across position");
                    prev = s;
                }
            }
            TestAssert.That(Math.Abs(frame.Meander(123456f)) <= 14000f * 1.0001f, "the meander never exceeds its amplitude");
            TestAssert.That(new TapeFrame(10f, 5f).Meander(1e5f) == 0f, "no amplitude, no meander");
        }

        private static void SamplerClampsAtTheEnds()
        {
            BakedTape ends = BakedTape.TwoKeyframes(Humilis(1f), Humilis(5f), 1500000f, 12000f);
            var guard = new TapeSampler(ends);
            TestAssert.That(guard.At(float.NaN).LobeRung == guard.At(0f).LobeRung && guard.At(float.PositiveInfinity).LobeRung == 5f &&
                guard.At(float.NegativeInfinity).LobeRung == 1f, "a NaN position reads the first texel and infinities clamp, none throws");
            BakedTape tape = BakedTape.TwoKeyframes(Humilis(1f), Humilis(5f), 1500000f, 12000f);
            var sampler = new TapeSampler(tape);
            TestAssert.That(sampler.At(-1e7f).LobeRung == 1f && sampler.At(float.MaxValue / 4f).LobeRung == 5f, "downwind of 0 the first keyframe holds, upwind of 3000 km the last");
            TestAssert.That(Math.Abs(sampler.At(0f).LobeRung - 1f) < 1e-6f && Math.Abs(sampler.At(BakedTape.SpanMetres).LobeRung - 5f) < 1e-6f, "the ends are exact");
        }
    }
}
