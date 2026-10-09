using System;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class BreathSynthTests
    {
        private const int Rate = 22050;

        public static void Run()
        {
            foreach (BreathSound sound in Enum.GetValues(typeof(BreathSound)))
            {
                float[] a = BreathSynth.Build(sound, Rate);
                TestAssert.That(a != null, sound + " builds.");
                float seconds = a.Length / (float)Rate;
                TestAssert.That(seconds >= .2f && seconds <= 1.5f, sound + " lasts 0.2-1.5 s, got " + seconds);
                double sum = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    TestAssert.That(!float.IsNaN(a[i]) && !float.IsInfinity(a[i]) && Math.Abs(a[i]) <= 1f, sound + " samples are finite and in range.");
                    sum += a[i] * a[i];
                }
                TestAssert.That(Math.Sqrt(sum / a.Length) > .01, sound + " is audible.");
                float[] b = BreathSynth.Build(sound, Rate);
                bool same = b.Length == a.Length;
                for (int i = 0; same && i < a.Length; i++) same = a[i] == b[i];
                TestAssert.That(same, sound + " is deterministic.");
            }

            float[] inhale = BreathSynth.Build(BreathSound.Inhale0, Rate);
            TestAssert.That(PeakIndex(inhale, 0, inhale.Length) < .02f * Rate, "An inhale opens with the demand-valve click.");

            float[] heart = BreathSynth.Build(BreathSound.Heart, Rate);
            int lub = PeakIndex(heart, 0, (int)(.08f * Rate)), dub = PeakIndex(heart, (int)(.08f * Rate), (int)(.25f * Rate));
            float gap = (dub - lub) / (float)Rate;
            TestAssert.That(gap >= .10f && gap <= .14f, "Heartbeat is a lub-dub 0.10-0.14 s apart, got " + gap);

            TestAssert.That(BreathSynth.Build((BreathSound)999, Rate) == null, "Unknown sounds build nothing.");
            TestAssert.That(BreathSynth.Build(BreathSound.Inhale0, 100) == null, "An unusable sample rate builds nothing.");
        }

        private static int PeakIndex(float[] samples, int from, int to)
        {
            int best = from;
            for (int i = from; i < to && i < samples.Length; i++) if (Math.Abs(samples[i]) > Math.Abs(samples[best])) best = i;
            return best;
        }
    }
}
