using UnityEngine;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// A restrained two-note dispatch chime, flat and client-local. Generated once and
    /// rate-limited so rapid rotations cannot stack a loud alert.
    /// </summary>
    internal sealed class EventAlertTone
    {
        private const int SampleRate = 44100;
        private const float Interval = 8f;
        private static AudioClip clip;

        private readonly AudioSource source;
        private float nextAllowed;

        public EventAlertTone(Transform parent)
        {
            var host = new GameObject("BoscaliEventAlertTone");
            host.transform.SetParent(parent, false);
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.16f;
        }

        public void Play()
        {
            if (source == null || Time.unscaledTime < nextAllowed) return;
            nextAllowed = Time.unscaledTime + Interval;
            source.PlayOneShot(Clip());
        }

        private static AudioClip Clip()
        {
            if (clip != null) return clip;
            const float toneSeconds = 0.18f;
            const int tones = 2;
            int perTone = (int)(SampleRate * toneSeconds);
            var samples = new float[perTone * tones];
            double phase = 0.0;
            for (int t = 0; t < tones; t++)
            {
                double frequency = t == 0 ? 330.0 : 440.0;
                for (int i = 0; i < perTone; i++)
                {
                    float progress = i / (float)perTone;
                    float envelope = Mathf.Clamp01(progress * 18f) *
                        Mathf.Clamp01((1f - progress) * 9f) * Mathf.Exp(-progress * 2.2f);
                    phase += frequency / SampleRate;
                    float wave = (float)System.Math.Sin(phase * 2.0 * System.Math.PI);
                    samples[t * perTone + i] = wave * envelope * 0.42f;
                }
            }
            clip = AudioClip.Create("BoscaliEventAlertTone", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
