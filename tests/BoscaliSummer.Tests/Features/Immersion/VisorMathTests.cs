using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class VisorMathTests
    {
        public static void Run()
        {
            TestAssert.That(VisorMath.SunVisor(-10f) == 0f && VisorMath.SunVisor(10f) == 1f && Math.Abs(VisorMath.SunVisor(4f) - .5f) < .001f &&
                VisorMath.SunVisor(float.NaN) == 0f, "The sun visor lowers with sun elevation, clear at night.");
            float last = -1f;
            for (float g = 0f; g <= 12f; g += .25f)
            {
                float brow = VisorMath.Brow(g);
                TestAssert.That(brow >= last && brow >= 0f && brow <= 1f, "Helmet brow grows monotonically with G.");
                last = brow;
            }
            TestAssert.That(VisorMath.Brow(5f) == 0f && VisorMath.Brow(9f) == 1f && VisorMath.Brow(float.NaN) == 0f,
                "Helmet brow starts above 6 G and is full at 9 G.");
            TestAssert.That(VisorMath.Ease(.2f, 1f, 0f, 2f) == .2f && VisorMath.Ease(.2f, 1f, 3f, 2f) == 1f &&
                VisorMath.Ease(.2f, float.NaN, .1f, 2f) == .2f && VisorMath.Ease(.2f, 1f, .1f, 0f) == 1f,
                "Easing holds when paused, completes over its span and ignores invalid targets.");
            TestAssert.That(VisorMath.FogsOnExhale(.6f, 20f, true) && !VisorMath.FogsOnExhale(.3f, 20f, true) &&
                VisorMath.FogsOnExhale(.3f, -15f, true) && !VisorMath.FogsOnExhale(.3f, -15f, false),
                "Breath fogs the visor when breathing hard or in known cold air.");

            var fog = new FogPulse();
            fog.Trigger(1f);
            float t = 0f;
            while (fog.Value < .95f && t < 1f) { fog.Step(1f / 60f); t += 1f / 60f; }
            TestAssert.That(t <= .26f, "Breath fog blooms within a quarter second, took " + t);
            fog.Trigger(1f);
            fog.Step(1f / 60f);
            TestAssert.That(fog.Value <= 1f, "Re-fogging at the peak never exceeds full.");
            float clear = 0f;
            while (fog.Value > VisorMath.Idle && clear < 5f) { fog.Step(1f / 60f); clear += 1f / 60f; }
            TestAssert.That(clear <= 1.9f && fog.Value == 0f, "Breath fog clears to idle within 1.9 s, took " + clear);
            fog.Step(float.NaN); fog.Step(-1f);
            TestAssert.That(fog.Value == 0f, "Invalid steps leave a clear visor clear.");
        }
    }
}
