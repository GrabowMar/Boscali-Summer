using System;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Rain and airflow, synthesised in memory at first use and never shipped as an asset:
    /// <c>rain</c> is band-limited noise with a slow wobble, <c>wind</c> is the same noise
    /// pushed down into an airframe rumble. Both loop by crossfading the generated tail over
    /// the head, so the seam is inaudible, and a source whose level falls to the floor is
    /// stopped — not merely muted.
    ///
    /// Two clips, two 2D sources, one owned GameObject. Nothing here routes through a mixer:
    /// this is world sound, not radio, so it stays on the game's default effects output.
    /// </summary>
    internal sealed class RainAudio
    {
        private const int SampleRate = 44100;
        private const float ClipSeconds = 3.2f;
        private const int SeamSamples = 400;
        private const float Peak = 0.85f;

        private const int RainSeed = 0x5261696E;
        private const int WindSeed = 0x57696E64;

        /// <summary>Below this the source is stopped; the owner also removes it at zero rain.</summary>
        private const float MinAudible = 0.02f;
        private const float MaxRainVolume = 0.85f;
        private const float MaxWindVolume = 0.7f;

        /// <summary>Rain on a fast canopy is louder; 300 m/s is the reference that earns full gain.</summary>
        private const float AirspeedReference = 300f;
        private const float RainBaseGain = 0.72f;
        private const float RainAirspeedGain = 0.55f;

        /// <summary>Below this the mean wind is a breeze, not storm airflow worth hearing.</summary>
        private const float WindThreshold = 2f;
        private const float WindReference = 26f;
        private const float WindBaseGain = 0.45f;
        private const float WindTurbulenceGain = 0.55f;

        private GameObject hostObject;
        private AudioSource rainSource;
        private AudioSource windSource;
        private AudioClip rainClip;
        private AudioClip windClip;
        private bool failed;

        public bool Active => (rainSource != null && rainSource.isPlaying) ||
                              (windSource != null && windSource.isPlaying);

        public bool Created => hostObject != null;

        public bool Failed => failed;

        /// <summary>Build both clips and sources once. A synthesis failure latches audio off.</summary>
        public bool TryCreate(Transform parent)
        {
            if (hostObject != null) return true;
            if (failed) return false;
            try
            {
                var host = new GameObject("BoscaliRainAudio");
                host.transform.SetParent(parent, false);
                rainSource = host.AddComponent<AudioSource>();
                windSource = host.AddComponent<AudioSource>();
                rainClip = BuildRainClip();
                windClip = BuildWindClip();
                Configure(rainSource, rainClip);
                Configure(windSource, windClip);
                hostObject = host;
                return true;
            }
            catch (Exception)
            {
                failed = true;
                Teardown();
                return false;
            }
        }

        /// <summary>
        /// One 10 Hz update: rain tracks precipitation and airspeed, wind tracks the mean wind
        /// and its turbulence. Both levels are clamped and both sources stop at the floor.
        /// </summary>
        public void Tick(float intensity, float airspeed, float windSpeed, float turbulence)
        {
            if (rainSource == null || windSource == null) return;
            float rain = Mathf.Clamp01(intensity) *
                         (RainBaseGain + RainAirspeedGain * Mathf.Clamp01(airspeed / AirspeedReference)) *
                         MaxRainVolume;
            SetLevel(rainSource, rain);
            float wind = Mathf.Clamp01((windSpeed - WindThreshold) / WindReference) *
                         (WindBaseGain + WindTurbulenceGain * Mathf.Clamp01(turbulence)) *
                         MaxWindVolume;
            SetLevel(windSource, wind);
        }

        public void Teardown()
        {
            if (rainSource != null)
            {
                rainSource.Stop();
                rainSource.clip = null;
            }
            if (windSource != null)
            {
                windSource.Stop();
                windSource.clip = null;
            }
            rainSource = null;
            windSource = null;
            if (rainClip != null) UnityEngine.Object.Destroy(rainClip);
            if (windClip != null) UnityEngine.Object.Destroy(windClip);
            rainClip = null;
            windClip = null;
            if (hostObject != null) UnityEngine.Object.Destroy(hostObject);
            hostObject = null;
        }

        private static void Configure(AudioSource source, AudioClip clip)
        {
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.dopplerLevel = 0f;
        }

        private static void SetLevel(AudioSource source, float level)
        {
            if (level < MinAudible)
            {
                if (source.isPlaying) source.Stop();
                return;
            }
            source.volume = Mathf.Clamp01(level);
            if (!source.isPlaying) source.Play();
        }

        // ---- Synthesis -------------------------------------------------------------------------

        /// <summary>
        /// One-pole low-pass at roughly 350 Hz is the heavy body of the shower; the remainder is
        /// the broadband patter. Two slow sines wobble the whole bed so it reads as rain rather
        /// than as static.
        /// </summary>
        private static AudioClip BuildRainClip()
        {
            int length = (int)(SampleRate * ClipSeconds);
            float[] raw = new float[length + SeamSamples];
            var random = new System.Random(RainSeed);
            float low = 0f, body = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (white - low) * 0.05f;
                body += (low - body) * 0.05f;
                float t = i / (float)SampleRate;
                float wobble = 1f + 0.14f * Mathf.Sin(2f * Mathf.PI * 0.9f * t) +
                               0.06f * Mathf.Sin(2f * Mathf.PI * 4.3f * t + 1.1f);
                raw[i] = ((white - low) * 0.55f + body * 3.2f) * wobble;
            }
            return Finish("BoscaliRain", raw);
        }

        /// <summary>Two cascaded one-poles at roughly 90 Hz: a low, steady airflow rumble.</summary>
        private static AudioClip BuildWindClip()
        {
            int length = (int)(SampleRate * ClipSeconds);
            float[] raw = new float[length + SeamSamples];
            var random = new System.Random(WindSeed);
            float low = 0f, body = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (white - low) * 0.012f;
                body += (low - body) * 0.012f;
                raw[i] = body * 6f;
            }
            return Finish("BoscaliWind", raw);
        }

        /// <summary>
        /// Crossfade the generated tail into the head so the clip loops without a seam, normalise
        /// to a fixed peak, and hand the result to an in-memory clip.
        /// </summary>
        private static AudioClip Finish(string name, float[] raw)
        {
            int length = raw.Length - SeamSamples;
            float[] samples = new float[length];
            float highest = 0f;
            for (int i = 0; i < length; i++)
            {
                float value = raw[i];
                if (i < SeamSamples)
                {
                    float weight = i / (float)SeamSamples;
                    value = value * weight + raw[length + i] * (1f - weight);
                }
                samples[i] = value;
                float magnitude = Mathf.Abs(value);
                if (magnitude > highest) highest = magnitude;
            }
            float scale = highest > 1e-4f ? Peak / highest : 0f;
            for (int i = 0; i < length; i++) samples[i] *= scale;
            AudioClip clip = AudioClip.Create(name, length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
