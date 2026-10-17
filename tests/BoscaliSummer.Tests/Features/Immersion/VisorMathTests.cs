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

            const int W = 512, H = 288;
            byte[] mask = VisorMask.Build(W, H);
            TestAssert.That(mask != null && mask.Length == W * H * 4 && VisorMask.Build(0, H) == null, "The visor mask is one RGBA layer image.");
            byte[] again = VisorMask.Build(W, H);
            bool same = true;
            for (int i = 0; same && i < mask.Length; i++) same = mask[i] == again[i];
            TestAssert.That(same, "The visor mask is deterministic.");
            double fogBottom = Mean(mask, W, H, 1, 0f, .3f), fogTop = Mean(mask, W, H, 1, .7f, 1f);
            TestAssert.That(fogBottom > 40 && fogBottom > fogTop * 5 + 1, "Breath fog sits at the lower edge.");
            TestAssert.That(Mean(mask, W, H, 2, .96f, 1f) > 100 && Mean(mask, W, H, 2, 0f, .85f) == 0, "The helmet brow lives only at the top edge.");
            TestAssert.That(Mean(mask, W, H, 0, 0f, 1f) == 0 && Mean(mask, W, H, 3, 0f, 1f) == 0,
                "Scratches and the shell frame are drawn by the shader, so the mask carries neither.");
        }

        private static double Mean(byte[] rgba, int w, int h, int channel, float v0, float v1)
        {
            double sum = 0; int n = 0;
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                if (v < v0 || v > v1) continue;
                for (int x = 0; x < w; x++) { sum += rgba[(y * w + x) * 4 + channel]; n++; }
            }
            return n > 0 ? sum / n : 0;
        }
    }
}
