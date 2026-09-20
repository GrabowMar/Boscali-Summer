using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The watch-floor klaxon: a synthesized two-tone whoop played flat (no 3D) when the
    /// network takes a serious hit. Client-local, rate limited to once every six seconds, one
    /// cached clip, one source that dies with the OPS screen.
    /// </summary>
    internal sealed class CyberAlarm
    {
        private const int SampleRate = 22050;
        private const float Interval = 6f;
        private static AudioClip clip;

        private readonly GameObject host;
        private readonly AudioSource source;
        private float nextAllowed;

        public CyberAlarm(Transform parent)
        {
            host = new GameObject("BoscaliCyberAlarm");
            host.transform.SetParent(parent, false);
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.3f;
        }

        public void Sound()
        {
            if (source == null || Time.unscaledTime < nextAllowed) return;
            nextAllowed = Time.unscaledTime + Interval;
            source.PlayOneShot(Clip());
        }

        public void Dispose()
        {
            if (host != null) Object.Destroy(host);
        }

        private static AudioClip Clip()
        {
            if (clip != null) return clip;
            const float toneSeconds = 0.32f;
            const int tones = 4;
            int perTone = (int)(SampleRate * toneSeconds);
            var samples = new float[perTone * tones];
            double phase = 0.0;
            for (int t = 0; t < tones; t++)
            {
                double frequency = t % 2 == 0 ? 640.0 : 880.0;
                for (int i = 0; i < perTone; i++)
                {
                    float progress = i / (float)perTone;
                    float envelope = Mathf.Clamp01(progress * 30f) * Mathf.Clamp01((1f - progress) * 20f);
                    phase += frequency / SampleRate;
                    // A soft-clipped square: urgent without being a pure beep.
                    float wave = Mathf.Clamp((float)System.Math.Sin(phase * 2.0 * System.Math.PI) * 3f, -1f, 1f);
                    samples[t * perTone + i] = wave * envelope * 0.45f;
                }
            }
            clip = AudioClip.Create("BoscaliCyberKlaxon", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
