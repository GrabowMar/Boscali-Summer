using System;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Plays native radio clicks. Retry missing singletons; disable playback after exceptions
    /// until mission reset. Subtitles remain available.</summary>
    internal static class WingRadioAudio
    {
        /// <summary>Minimum click spacing in seconds to prevent overlap.</summary>
        private const float MinimumGap = 0.35f;

        private static float lastPlayed = -100f;

        /// <summary>Stop retrying after a playback failure.</summary>
        private static bool unavailable;

        /// <summary>Distinct cockpit avionics audio cues.</summary>
        public enum Earcon
        {
            Transmission,
            ThreatAlarm,
            Splash,
        }

        private static AudioClip threatClip;
        private static AudioClip splashClip;

        /// <summary>Play a distinct avionics earcon.</summary>
        public static void Play(Earcon earcon)
        {
            if (unavailable || WingSettings.Instance == null || WingSettings.Instance.Radio.Value == RadioLevel.Off) return;
            if (earcon == Earcon.Transmission && Time.unscaledTime - lastPlayed < MinimumGap) return;

            try
            {
                if (SoundManager.i == null) return;

                AudioClip clip = ResolveClip(earcon);
                if (clip == null) return;

                if (earcon == Earcon.ThreatAlarm)
                    SoundManager.PlayRadarWarningOneShot(clip);
                else
                    SoundManager.PlayInterfaceOneShot(clip);

                lastPlayed = Time.unscaledTime;
            }
            catch (Exception e)
            {
                unavailable = true;
                WingLog.Verbose("[Comms] audio playback unavailable: " + e.Message);
            }
        }

        private static AudioClip ResolveClip(Earcon earcon)
        {
            switch (earcon)
            {
                case Earcon.Transmission:
                    GameAssets assets = GameAssets.i;
                    return assets != null ? assets.radioStatic : null;
                case Earcon.ThreatAlarm:
                    return threatClip ?? (threatClip = CreateWarble("Earcon_Threat", 950f, 1300f, 0.14f, 0.30f));
                case Earcon.Splash:
                    return splashClip ?? (splashClip = CreateChime("Earcon_Splash", 880f, 1100f, 0.07f, 0.25f));
                default:
                    return null;
            }
        }

        private static AudioClip CreateChime(string name, float fStart, float fEnd, float duration, float volume)
        {
            const int sampleRate = 22050;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float progress = t / duration;
                float freq = Mathf.Lerp(fStart, fEnd, progress);
                float envelope = Mathf.Sin(Mathf.PI * progress);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * volume;
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip CreateWarble(string name, float fStart, float fEnd, float duration, float volume)
        {
            const int sampleRate = 22050;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float progress = t / duration;
                float envelope = Mathf.Sin(Mathf.PI * progress);
                float freq = Mathf.Lerp(fStart, fEnd, Mathf.PingPong(progress * 4f, 1f));
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * volume;
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Retry audio discovery next mission.</summary>
        public static void Reset()
        {
            lastPlayed = -100f;
            unavailable = false;
        }
    }
}
