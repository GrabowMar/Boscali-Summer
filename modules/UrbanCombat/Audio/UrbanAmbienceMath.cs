using System;

namespace BoscaliSummer.Features.UrbanCombat.Audio
{
    /// <summary>
    /// Pure siren curves: base volume, anchor hysteresis, fades,
    /// loop seams. Engine-free so the curves stay testable without Unity.
    /// </summary>
    internal static class UrbanAmbienceMath
    {
        public const float FadeSeconds = 0.25f;
        public const float SilenceEpsilon = 0.001f;
        public const float SirenInnerMeters = 500f;
        public const float SirenOuterMeters = 6000f;
        public const float SirenReleaseMeters = 6900f;
        public const float AnchorSwitchRatio = 0.85f;
        public const float SirenLevel = 0.35f;

        public static float SirenVolume(float master)
        {
            return SirenLevel * Math.Max(0f, master);
        }

        public static bool ShouldSwitchAnchor(float currentDist, float candidateDist)
        {
            return candidateDist < currentDist * AnchorSwitchRatio;
        }

        public static float FadeToward(float current, float target, float deltaTime)
        {
            if (deltaTime <= 0f) return current;
            float step = deltaTime / FadeSeconds;
            float d = target - current;
            if (d > step) return current + step;
            if (d < -step) return current - step;
            return target;
        }

        public static int ApplyLoopCrossfade(float[] samples, int channels, int fadeSamples)
        {
            if (samples == null || channels < 1) return 0;
            int frames = samples.Length / channels;
            if (fadeSamples < 2 || fadeSamples > frames / 2) return frames;
            for (int i = 0; i < fadeSamples; i++)
            {
                float frac = (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / (fadeSamples - 1)));
                for (int c = 0; c < channels; c++)
                {
                    int head = i * channels + c;
                    int tail = (frames - fadeSamples + i) * channels + c;
                    samples[tail] = samples[tail] * (1f - frac) + samples[head] * frac;
                }
            }
            int playable = frames - fadeSamples;
            Array.Copy(samples, fadeSamples * channels, samples, 0, playable * channels);
            return playable;
        }
    }
}
