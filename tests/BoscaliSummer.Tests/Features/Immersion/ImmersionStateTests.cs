using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class ImmersionStateTests
    {
        public static void Run()
        {
            TestAssert.That(ImmersionMath.GAudioCutoffFrequency(1f, true) == 22000f,
                "Ordinary flight does not muffle native audio.");
            TestAssert.That(ImmersionMath.ExposureAudioCutoff(0f, 0f) == 22000f &&
                ImmersionMath.ExposureAudioCutoff(float.NaN, 0f) == 22000f,
                "Neutral and invalid exposure bypass filtering.");
            TestAssert.That(ImmersionMath.ExposureAudioCutoff(1f, 0f) >= 1200f &&
                ImmersionMath.ExposureAudioCutoff(1f, 0f) < 1300f,
                "Sustained exposure has a bounded low cutoff.");
            var brief = new PilotExposure(); brief.Step(9f, .05f);
            var sustained = new PilotExposure();
            for (int i = 0; i < 120; i++) sustained.Step(9f, 1f / 60f);
            TestAssert.That(sustained.Positive > brief.Positive * 5f,
                "A sustained load causes more exposure than a brief spike.");
            float exposure = sustained.Positive;
            sustained.Step(9f, 0f); sustained.Step(9f, 5f); sustained.Step(float.NaN, .02f);
            TestAssert.That(sustained.Positive == exposure, "Paused/stalled/invalid load cannot accumulate exposure.");
            for (int i = 0; i < 600; i++) sustained.Step(1f, 1f / 60f);
            TestAssert.That(sustained.Positive < .001f, "Normal flight recovers exposure.");
            for (int i = 0; i < 120; i++) sustained.Step(-3f, 1f / 60f);
            TestAssert.That(sustained.Negative > .9f && sustained.Positive < .001f,
                "Negative load uses independent exposure.");
            sustained.Reset();
            TestAssert.That(sustained.Positive == 0 && sustained.Negative == 0, "Binding reset clears pilot exposure.");

            var motion = new MotionEnvelope();
            for (int i = 0; i < 1000; i++) motion.AddGun(10000f, 2f);
            TestAssert.That(motion.Recoil <= 1f, "A burst of gun impulses remains bounded.");
            motion.Step(1f);
            TestAssert.That(motion.Recoil < .02f, "Gun impulse decays after firing stops.");
            motion.AddLanding(-100f, 2f);
            TestAssert.That(motion.Landing <= 1f, "Landing impulse remains bounded.");
            TestAssert.That(!motion.SonicCrossing(1.1f, 0f), "First Mach sample primes without a false crossing.");
            TestAssert.That(motion.SonicCrossing(.97f, 3f), "A real transonic crossing fires.");
            for (int i = 0; i < 50; i++)
                TestAssert.That(!motion.SonicCrossing(i % 2 == 0 ? 1.005f : .995f, 3.1f + i * .01f),
                    "Mach jitter inside hysteresis remains silent.");
            motion.SonicCrossing(.96f, 6f);
            TestAssert.That(motion.SonicCrossing(1.03f, 7f), "A later outside-band crossing rearms.");
            motion.Reset();
            TestAssert.That(motion.Recoil == 0 && motion.Landing == 0 && motion.Sonic == 0 && motion.GearLock == 0,
                "Binding reset clears event envelopes.");
            var gear = new MotionEnvelope();
            TestAssert.That(!gear.GearTransition(1, false, true, .02f) && gear.GearLock == 0f,
                "Binding to already locked gear does not create an impulse.");
            TestAssert.That(!gear.GearTransition(1, true, true, .02f) &&
                gear.GearTransition(1, false, true, .02f) && gear.GearLock == -.04f,
                "Extension creates a small impulse only when the moving gear locks down.");
            TestAssert.That(!gear.GearTransition(1, false, true, .02f), "A held gear lock fires only once.");
            gear.Step(.5f);
            TestAssert.That(Math.Abs(gear.GearLock) < .0001f, "The mechanical impulse settles quickly.");
            gear.GearTransition(-1, true, true, .02f);
            TestAssert.That(gear.GearTransition(-1, false, true, .02f) && gear.GearLock == .04f,
                "Retraction creates an equally restrained impulse at its native lock.");
            gear.GearTransition(1, true, true, .02f);
            TestAssert.That(!gear.GearTransition(-1, false, true, .02f),
                "A mismatched direction or snapped state cannot impersonate a completed gear cycle.");
            foreach (float badDt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                gear.GearTransition(1, true, true, .02f);
                TestAssert.That(!gear.GearTransition(1, false, true, badDt) && gear.GearLock == 0f &&
                    !gear.GearTransition(1, false, true, .02f),
                    "Pause or invalid time clears gear history and cannot defer a latch impulse.");
            }
            gear.GearTransition(1, true, true, .02f);
            TestAssert.That(!gear.GearTransition(1, false, false, .02f) && gear.GearLock == 0f &&
                !gear.GearTransition(1, false, true, .02f),
                "Disabled extra shake or zero strength suppresses the transition through re-enable.");
            gear.GearTransition(1, true, true, .02f);
            gear.Reset();
            TestAssert.That(!gear.GearTransition(1, false, true, .02f) && gear.GearLock == 0f,
                "Binding, camera or discontinuity reset discards a pending gear transition.");
            gear.GearTransition(1, true, true, .02f);
            TestAssert.That(!gear.GearTransition(0, false, true, .02f) &&
                !gear.GearTransition(1, false, true, .02f), "Uninitialized or unsupported gear resets silently.");
            gear.GearTransition(-1, true, true, .02f);
            gear.GearTransition(-1, false, true, .02f);
            gear.ClearExtra();
            TestAssert.That(gear.GearLock == 0f, "Disabling extra motion clears any residual gear impulse.");
            var zero = ImmersionMath.ComposeMotion(100f, -100f, 100f, 0f, false);
            var full = ImmersionMath.ComposeMotion(100f, -100f, 100f, 1f, false);
            var comfort = ImmersionMath.ComposeMotion(100f, -100f, 100f, 1f, true);
            TestAssert.That(zero.pitch == 0 && zero.yaw == 0 && zero.roll == 0, "Zero strength removes every source.");
            TestAssert.That(full.pitch == 2f && full.yaw == -1.2f && full.roll == 2f, "Final camera composition is bounded.");
            TestAssert.That(comfort.pitch == .5f && comfort.yaw == -.3f && comfort.roll == .5f,
                "Comfort scales the complete camera composition.");
        }
    }
}
