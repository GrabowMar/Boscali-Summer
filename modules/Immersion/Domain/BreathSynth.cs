using System;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    internal enum BreathSound
    {
        Inhale0, Inhale1, Inhale2, Exhale0, Exhale1, Exhale2, PressureInhale, SuitInflate, SuitVent,
        Keh0, Keh1, Keh2, Gasp0, Gasp1, Recovery, Heart
    }

    /// <summary>
    /// Deterministic one-time synthesis of oxygen-mask and body sounds (no Unity types; baked off the main thread).
    /// Mask sounds are genuinely shaped noise through a valve; voiced effort sounds are formant-filtered
    /// pulse trains and stand in until recorded clips are bundled.
    /// </summary>
    internal static class BreathSynth
    {
        public static float[] Build(BreathSound sound, int rate)
        {
            if (rate < 8000 || rate > 96000) return null;
            var rng = new Random(9001 + (int)sound * 7919);
            switch (sound)
            {
                case BreathSound.Inhale0: case BreathSound.Inhale1: case BreathSound.Inhale2:
                {
                    int v = sound - BreathSound.Inhale0;
                    float[] a = Breath(rng, rate, .85f + .07f * v, 1800f + 400f * v, 2f, .3f, .2f, 0f, .5f);
                    AddClick(a, rate, 0f, 3000f, .9f);
                    return a;
                }
                case BreathSound.Exhale0: case BreathSound.Exhale1: case BreathSound.Exhale2:
                {
                    int v = sound - BreathSound.Exhale0;
                    float[] a = Breath(rng, rate, 1.05f + .1f * v, 750f + 200f * v, 1.6f, .12f, .35f, 25f + 5f * v, .5f);
                    AddClick(a, rate, a.Length / (float)rate - .008f, 2200f, .45f);
                    return a;
                }
                case BreathSound.PressureInhale: return Breath(rng, rate, .45f, 3500f, 1.4f, .05f, .1f, 0f, .55f);
                case BreathSound.SuitInflate: return Hiss(rng, rate, .6f, true);
                case BreathSound.SuitVent: return Hiss(rng, rate, .45f, false);
                case BreathSound.Keh0: case BreathSound.Keh1: case BreathSound.Keh2:
                {
                    int v = sound - BreathSound.Keh0;
                    return Voiced(rng, rate, .3f + .05f * v, 120f + 10f * v, 550f + 60f * v, 1800f, .35f, .02f, false);
                }
                case BreathSound.Gasp0: case BreathSound.Gasp1:
                {
                    int v = sound - BreathSound.Gasp0;
                    return Voiced(rng, rate, .55f + .1f * v, 135f, 800f, 1400f, .7f, .01f, true);
                }
                case BreathSound.Recovery: return Voiced(rng, rate, .9f, 115f, 700f, 1300f, .6f, 0f, true);
                case BreathSound.Heart: return Heart(rate);
                default: return null;
            }
        }

        private static float[] Breath(Random rng, int rate, float seconds, float centre, float q, float attack, float release,
            float flutterHz, float peak)
        {
            int n = (int)(seconds * rate);
            var a = new float[n];
            var band = new Biquad(rate, centre, q, false);
            var air = new Biquad(rate, centre * 1.9f, q * .7f, false);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float flutter = flutterHz > 0f ? 1f + .35f * (float)Math.Sin(2.0 * Math.PI * flutterHz * i / rate) : 1f;
                a[i] = (band.Step(noise) + .35f * air.Step(noise)) * Envelope(t, attack, release) * flutter;
            }
            return Normalize(a, peak);
        }

        private static float[] Hiss(Random rng, int rate, float seconds, bool swell)
        {
            int n = (int)(seconds * rate);
            var a = new float[n];
            var high = new Biquad(rate, 5000f, .8f, true);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float shape = swell ? Envelope(t, .55f, .25f) : Envelope(t, .03f, .9f);
                a[i] = high.Step((float)(rng.NextDouble() * 2.0 - 1.0)) * shape;
            }
            return Normalize(a, .5f);
        }

        // Glottal pulse train through two formants, a short plosive burst ("k"), breathy noise and a sharp envelope.
        private static float[] Voiced(Random rng, int rate, float seconds, float pitch, float f1, float f2, float breath,
            float burst, bool ragged)
        {
            int n = (int)(seconds * rate);
            var a = new float[n];
            var formant1 = new Biquad(rate, f1, 3f, false);
            var formant2 = new Biquad(rate, f2, 4f, false);
            var plosive = new Biquad(rate, 2600f, 1.2f, false);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float time = i / (float)rate, t = i / (float)n;
                float wobble = ragged ? 1f + .06f * (float)Math.Sin(2.0 * Math.PI * 7.0 * time) : 1f;
                phase += pitch * wobble * (1f - .15f * t) / rate;
                if (phase >= 1.0) phase -= 1.0;
                float pulse = phase < .12 ? (float)Math.Sin(Math.PI * phase / .12) : 0f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float source = pulse * (1f - breath) + noise * breath;
                float voice = formant1.Step(source) + .6f * formant2.Step(source);
                float tremble = ragged ? .75f + .25f * (float)Math.Sin(2.0 * Math.PI * 11.0 * time) : 1f;
                float k = burst > 0f && time < burst ? plosive.Step(noise) * 3f * (1f - time / burst) : 0f;
                a[i] = voice * Envelope(t, .1f, .55f) * tremble + k;
            }
            return Normalize(a, .7f);
        }

        private static float[] Heart(int rate)
        {
            int n = (int)(.35f * rate);
            var a = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                a[i] = Thump(t, .02f, 55f, .04f, 1f) + Thump(t, .14f, 45f, .035f, .75f);
            }
            return Normalize(a, .8f);
        }

        private static float Thump(float t, float start, float hz, float decay, float gain)
        {
            float local = t - start;
            if (local < 0f) return 0f;
            float ramp = Math.Min(1f, local / .003f);
            return gain * ramp * (float)(Math.Sin(2.0 * Math.PI * hz * local) * Math.Exp(-local / decay));
        }

        private static void AddClick(float[] a, int rate, float atSeconds, float hz, float gain)
        {
            int start = Math.Max(0, (int)(atSeconds * rate)), length = (int)(.006f * rate);
            for (int i = 0; i < length && start + i < a.Length; i++)
            {
                float t = i / (float)rate;
                float value = a[start + i] + gain * (float)(Math.Sin(2.0 * Math.PI * hz * t + Math.PI / 2) * Math.Exp(-t / .0015f));
                a[start + i] = Math.Max(-1f, Math.Min(1f, value));
            }
        }

        private static float Envelope(float t, float attack, float release)
        {
            if (attack > 0f && t < attack) return Smooth(t / attack);
            if (release > 0f && t > 1f - release) return Smooth((1f - t) / release);
            return 1f;
        }

        private static float Smooth(float x) { x = Math.Max(0f, Math.Min(1f, x)); return x * x * (3f - 2f * x); }

        private static float[] Normalize(float[] a, float peak)
        {
            float max = 0f;
            for (int i = 0; i < a.Length; i++) max = Math.Max(max, Math.Abs(a[i]));
            if (max <= 0f) return a;
            float scale = peak / max;
            for (int i = 0; i < a.Length; i++) a[i] *= scale;
            return a;
        }

        // RBJ cookbook biquad: constant-0dB-peak band-pass, or high-pass.
        private struct Biquad
        {
            private readonly float b0, b1, b2, a1, a2;
            private float x1, x2, y1, y2;

            public Biquad(int rate, float hz, float q, bool highPass)
            {
                double w = 2.0 * Math.PI * Math.Min(hz, rate * .45f) / rate, alpha = Math.Sin(w) / (2.0 * q), cos = Math.Cos(w);
                double a0 = 1.0 + alpha;
                if (highPass)
                {
                    b0 = (float)((1.0 + cos) / 2.0 / a0); b1 = (float)(-(1.0 + cos) / a0); b2 = b0;
                }
                else
                {
                    b0 = (float)(alpha / a0); b1 = 0f; b2 = (float)(-alpha / a0);
                }
                a1 = (float)(-2.0 * cos / a0); a2 = (float)((1.0 - alpha) / a0);
                x1 = x2 = y1 = y2 = 0f;
            }

            public float Step(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
