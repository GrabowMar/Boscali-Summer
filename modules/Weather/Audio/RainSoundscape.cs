using System;
using System.Threading;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Audio
{
    /// <summary>One audible loop per view: exterior rush or cockpit glass patter.</summary>
    internal sealed class RainSoundscape : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const string VoiceId = "weather-rain";

        private AudioSource rush;
        private AudioSource patter;
        private AudioSource active;
        private AudioLowPassFilter rushFilter;
        private AudioLowPassFilter patterFilter;
        private AudioClip rushClip;
        private AudioClip patterClip;
        private float[] rushSamples;
        private float[] patterSamples;
        private int rushFrames;
        private int patterFrames;
        private volatile int bakeState; // 0 baking, 1 ready, 2 failed
        private volatile bool destroyed;
        private bool playing;

        internal bool ClipsReady => rushClip != null && patterClip != null;
        internal bool IsRouted => rush != null && rush.outputAudioMixerGroup != null;
        internal bool IsPlaying => playing && ((rush != null && rush.isPlaying) || (patter != null && patter.isPlaying));
        internal float RushVolume => rush != null ? rush.volume : 0f;
        internal float PatterVolume => patter != null ? patter.volume : 0f;
        internal float ExteriorCutoff => rushFilter != null ? rushFilter.cutoffFrequency : 22000f;
        internal float CanopyCutoff => patterFilter != null ? patterFilter.cutoffFrequency : 22000f;

        internal void Initialize()
        {
            rush = gameObject.AddComponent<AudioSource>();
            Configure(rush);
            rushFilter = gameObject.AddComponent<AudioLowPassFilter>();
            rushFilter.cutoffFrequency = 7500f;
            var patterRoot = new GameObject("CanopyPatter");
            patterRoot.transform.SetParent(transform, false);
            patter = patterRoot.AddComponent<AudioSource>();
            Configure(patter);
            patterFilter = patterRoot.AddComponent<AudioLowPassFilter>();
            patterFilter.cutoffFrequency = 4200f;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    float[] rushData = BakeRush(8, out int rushLength);
                    float[] patterData = BakePatter(11, out int patterLength);
                    Array.Resize(ref rushData, rushLength * 2);
                    Array.Resize(ref patterData, patterLength * 2);
                    if (destroyed) return;
                    rushSamples = rushData;
                    patterSamples = patterData;
                    rushFrames = rushLength;
                    patterFrames = patterLength;
                    bakeState = 1;
                }
                catch (Exception) { bakeState = 2; }
            });
        }

        internal void UpdateAudio(float rain, float cloud, float glassMoisture, bool cockpit, bool enabled,
            float airspeedMps = 0f, float heightAboveGroundM = 0f, float exposure01 = 1f)
        {
            if (rush == null || patter == null || bakeState == 2) return;
            if (!ClipsReady && !CreateClips()) return;

            var mixer = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
            if (mixer != null && rush.outputAudioMixerGroup != mixer)
            {
                rush.outputAudioMixerGroup = mixer;
                patter.outputAudioMixerGroup = mixer;
            }
            if (!enabled || mixer == null) { Stop(); return; }
            // Condensation has no impact sound. Rain is unexposed water; shelter is applied
            // here so a hangar retains the quiet surrounding rain without canopy patter.
            var target = EnvironmentAudioMath.Rain(rain, cockpit, airspeedMps,
                heightAboveGroundM, exposure01, Time.unscaledTime);
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            AudioSource selected = cockpit ? patter : rush;
            AudioLowPassFilter selectedFilter = cockpit ? patterFilter : rushFilter;
            selectedFilter.cutoffFrequency = Mathf.MoveTowards(selectedFilter.cutoffFrequency, target.cutoff, dt * 6000f);
            // A view change fades the outgoing loop before starting the incoming loop.
            // This preserves the one-voice reservation instead of crossfading two voices.
            if (playing && active != null && active != selected)
            {
                active.volume = Mathf.MoveTowards(active.volume, 0f, dt * 0.8f);
                if (active.volume > 0.001f) return;
                active.Stop(); active.volume = 0f; active = null;
            }
            if (!playing && target.gain > 0.005f && FxVoiceBus.TryStartLoop(VoiceId)) playing = true;
            if (!playing) return;
            if (active == null && target.gain > 0.005f) { active = selected; active.Play(); }
            if (active == null) { Stop(); return; }
            active.volume = Mathf.MoveTowards(active.volume, target.gain, dt * 0.5f);
            if (target.gain <= 0.005f && active.volume <= 0.001f) Stop();
        }

        private bool CreateClips()
        {
            if (bakeState != 1) return false;
            rushClip = AudioClip.Create("BoscaliRainRush", rushFrames, 2, SampleRate, false);
            patterClip = AudioClip.Create("BoscaliCanopyPatter", patterFrames, 2, SampleRate, false);
            rushClip.SetData(rushSamples, 0);
            patterClip.SetData(patterSamples, 0);
            rushSamples = patterSamples = null;
            rush.clip = rushClip;
            patter.clip = patterClip;
            return true;
        }

        private static void Configure(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.reverbZoneMix = 0f;
            source.volume = 0f;
        }

        internal static float[] BakeRush(int seconds, out int playableFrames)
        {
            int frames = seconds * SampleRate;
            var samples = new float[frames * 2];
            var random = new System.Random(39821);
            float left = 0f, right = 0f, low = 0f;
            for (int i = 0; i < frames; i++)
            {
                float white = (float)(random.NextDouble() * 2 - 1);
                float side = (float)(random.NextDouble() * 2 - 1);
                low += 0.002f * (white - low);
                left += 0.035f * (white + side * 0.25f - left);
                right += 0.035f * (white - side * 0.25f - right);
                samples[i * 2] = (left - low) * 0.65f + white * 0.015f;
                samples[i * 2 + 1] = (right - low) * 0.65f + white * 0.015f;
            }
            // Warm surface impacts give low views texture; the per-source height/shelter
            // filter and gain soften these into the quiet free-air bed farther from ground.
            for (int hit = 0; hit < seconds * 58; hit++)
            {
                int start = random.Next(frames);
                float size = (float)random.NextDouble();
                float pan = 0.1f + (float)random.NextDouble() * 0.8f;
                float filter = 0f;
                int length = (int)(SampleRate * (0.012f + size * 0.033f));
                for (int i = 0; i < length; i++)
                {
                    filter += (0.13f + size * 0.1f) * ((float)(random.NextDouble() * 2 - 1) - filter);
                    float t = (float)i / SampleRate;
                    float value = filter * Math.Min(1f, t * 650f) * (float)Math.Exp(-t * 95f)
                        * (0.18f + size * 0.25f);
                    int index = ((start + i) % frames) * 2;
                    samples[index] += value * (float)Math.Sqrt(1f - pan);
                    samples[index + 1] += value * (float)Math.Sqrt(pan);
                }
            }
            playableFrames = CrossfadeLoop(samples, frames);
            Normalize(samples, playableFrames, 0.16f);
            return samples;
        }

        internal static float[] BakePatter(int seconds, out int playableFrames)
        {
            int frames = seconds * SampleRate;
            var samples = new float[frames * 2];
            var random = new System.Random(77129);
            for (int hit = 0; hit < seconds * 42; hit++)
            {
                int start = random.Next(frames);
                float size = (float)random.NextDouble();
                float pan = 0.15f + (float)random.NextDouble() * 0.7f;
                float left = (float)Math.Sqrt(1f - pan);
                float right = (float)Math.Sqrt(pan);
                float filtered = 0f;
                int length = (int)(SampleRate * (0.025f + size * 0.05f));
                for (int i = 0; i < length; i++)
                {
                    float noise = (float)(random.NextDouble() * 2 - 1);
                    filtered += (0.08f + size * 0.06f) * (noise - filtered);
                    float t = (float)i / SampleRate;
                    float attack = Math.Min(1f, t * 400f);
                    float value = filtered * attack * (float)Math.Exp(-t * (65f - 30f * size))
                        * (0.25f + size * 0.35f);
                    int index = ((start + i) % frames) * 2;
                    samples[index] += value * left;
                    samples[index + 1] += value * right;
                }
            }
            playableFrames = CrossfadeLoop(samples, frames);
            Normalize(samples, playableFrames, 0.12f);
            return samples;
        }

        // Bake-time gain only: filtered noise had ample peak headroom but was almost
        // inaudible beneath engines. Preserve dynamics and cap peaks before playback.
        private static void Normalize(float[] samples, int frames, float targetRms)
        {
            double energy = 0;
            float peak = 0f;
            int count = frames * 2;
            for (int i = 0; i < count; i++)
            {
                energy += samples[i] * samples[i];
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }
            if (peak <= 0f || count == 0) return;
            float gain = Math.Min(targetRms / (float)Math.Sqrt(energy / count), 0.78f / peak);
            for (int i = 0; i < count; i++) samples[i] *= gain;
        }

        private static int CrossfadeLoop(float[] samples, int frames)
        {
            int fade = SampleRate / 8;
            for (int i = 0; i < fade; i++)
            {
                float t = (float)i / fade;
                int tail = (frames - fade + i) * 2;
                int head = i * 2;
                samples[head] = samples[tail] * (1f - t) + samples[head] * t;
                samples[head + 1] = samples[tail + 1] * (1f - t) + samples[head + 1] * t;
            }
            return frames - fade;
        }

        private void Stop()
        {
            if (rush != null) { rush.Stop(); rush.volume = 0f; }
            if (patter != null) { patter.Stop(); patter.volume = 0f; }
            FxVoiceBus.EndLoop(VoiceId);
            playing = false;
            active = null;
        }

        internal void Release() => Stop();

        private void OnDestroy()
        {
            destroyed = true;
            if (playing) Stop();
            if (rushClip != null) Destroy(rushClip);
            if (patterClip != null) Destroy(patterClip);
        }
    }
}
