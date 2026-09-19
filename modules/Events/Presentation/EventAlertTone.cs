using UnityEngine;

namespace BoscaliSummer.Features.Events.Presentation
{
    /// <summary>
    /// The superevent alert tone: a short synthesized three-tone whoop, flat and client-local.
    /// No file is bundled or downloaded; the clip is generated once in memory and the same
    /// whoop is rate limited so a stacked rotation cannot machine-gun it.
    /// </summary>
    internal sealed class EventAlertTone
    {
        private const int SampleRate = 22050;
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
            source.volume = 0.32f;
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
            const float toneSeconds = 0.26f;
            const int tones = 3;
            int perTone = (int)(SampleRate * toneSeconds);
            var samples = new float[perTone * tones];
            double phase = 0.0;
            for (int t = 0; t < tones; t++)
            {
                // Rising urgency: a mid broadcast tone climbing into the alert pair.
                double frequency = t == 0 ? 480.0 : t == 1 ? 700.0 : 940.0;
                for (int i = 0; i < perTone; i++)
                {
                    float progress = i / (float)perTone;
                    float envelope = Mathf.Clamp01(progress * 25f) * Mathf.Clamp01((1f - progress) * 16f);
                    phase += frequency / SampleRate;
                    float wave = Mathf.Clamp(
                        (float)System.Math.Sin(phase * 2.0 * System.Math.PI) * 2.6f, -1f, 1f);
                    samples[t * perTone + i] = wave * envelope * 0.5f;
                }
            }
            clip = AudioClip.Create("BoscaliEventAlertTone", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
