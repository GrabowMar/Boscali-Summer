using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Pure helpers for synthesized rain audio: volume/pitch response curves, fade
    /// envelopes, and loop-seam blending. Engine-agnostic; covered by RainAudioMathTests.
    /// </summary>
    internal static class RainAudioMath
    {
        /// <summary>Time for a full-scale volume fade so mutes and view switches never pop.</summary>
        public const float FadeSeconds = 0.15f;

        public const float SilenceEpsilon = 0.001f;

        /// <summary>
        /// Continuous aerodynamic rain hiss. Louder with airspeed; slightly muffled inside
        /// the cockpit where the canopy patter layer carries the detail.
        /// </summary>
        public static float HissVolume(float speedNorm, float intensity, float master, bool cockpit)
        {
            float n = Clamp01(speedNorm);
            float viewAtten = cockpit ? 0.06f : 0.40f;
            return (0.25f + n * 0.75f) * Clamp01(intensity) * 0.50f * Math.Max(0f, master) * viewAtten;
        }

        /// <summary>Cockpit-only canopy droplet impacts; silent in external views.</summary>
        public static float PatterVolume(float speedNorm, float intensity, float master, bool cockpit)
        {
            if (!cockpit) return 0f;
            float n = Clamp01(speedNorm);
            return (0.35f + n * 0.65f) * Clamp01(intensity) * 0.65f * Math.Max(0f, master);
        }

        /// <summary>
        /// Hiss lowpass cutoff in Hz: the closed canopy dulls the roar (opening slightly
        /// with airspeed as seals rattle), while external views stay wide open.
        /// </summary>
        public static float MuffleCutoff(bool cockpit, float speedNorm)
        {
            if (!cockpit) return 20000f;
            return 900f + Clamp01(speedNorm) * 1700f;
        }

        public static float HissPitch(float speedNorm) => 0.95f + Clamp01(speedNorm) * 0.30f;

        public static float PatterPitch(float speedNorm) => 1f;

        /// <summary>
        /// Move a volume level toward its target at full-scale-per-<see cref="FadeSeconds"/>.
        /// </summary>
        public static float FadeToward(float current, float target, float deltaTime)
        {
            if (deltaTime <= 0f) return current;
            float step = deltaTime / FadeSeconds;
            float d = target - current;
            if (d > step) return current + step;
            if (d < -step) return current - step;
            return target;
        }

        /// <summary>
        /// Overlap the tail with the head, then remove the duplicated head. Returns the
        /// playable frame count; creating a clip with the old length reintroduces a seam.
        /// </summary>
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

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
