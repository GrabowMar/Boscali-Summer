using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Audio
{
    /// <summary>One audible loop per view: exterior rush or cockpit glass patter.</summary>
    internal sealed class RainSoundscape : MonoBehaviour
    {
        private const string VoiceId = "weather-rain";

        private AudioSource rush;
        private AudioSource patter;
        private AudioSource active;
        private AudioLowPassFilter rushFilter;
        private AudioLowPassFilter patterFilter;
        private AudioClip rushClip;
        private AudioClip patterClip;
        private bool clipsLoaded;
        private bool playing;

        internal bool ClipsReady => rushClip != null && patterClip != null;
        internal bool IsRouted => rush != null && rush.outputAudioMixerGroup != null;
        internal bool IsPlaying => playing && ((rush != null && rush.isPlaying) || (patter != null && patter.isPlaying));
        internal string ActiveClipName => active != null && active.clip != null ? active.clip.name : "";
        internal bool EffectsRouted => rush != null && patter != null && SoundManager.i != null && SoundManager.i.EffectsMixer != null &&
            rush.outputAudioMixerGroup == SoundManager.i.EffectsMixer && patter.outputAudioMixerGroup == SoundManager.i.EffectsMixer;
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
        }

        internal void UpdateAudio(float rain, float cloud, float glassMoisture, bool cockpit, bool enabled,
            float airspeedMps = 0f, float heightAboveGroundM = 0f, float exposure01 = 1f)
        {
            if (rush == null || patter == null) return;
            if (!ClipsReady && !LoadClips()) return;

            var mixer = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
            if (mixer != null && rush.outputAudioMixerGroup != mixer)
            {
                rush.outputAudioMixerGroup = mixer;
                patter.outputAudioMixerGroup = mixer;
            }
            if (!enabled || mixer == null) { Stop(); return; }
            // Rain and cloud are unexposed incoming water; shelter is applied here so a
            // hangar retains the quiet surrounding rain without canopy patter. Residual
            // glass wetness is silent: only striking droplets make sound.
            _ = glassMoisture;
            var target = EnvironmentAudioMath.Rain(rain, cockpit, airspeedMps,
                heightAboveGroundM, exposure01, Time.unscaledTime, cloud);
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

        private bool LoadClips()
        {
            if (clipsLoaded) return ClipsReady;
            clipsLoaded = true;
            rushClip = SfxClips.Get("weather-rain-exterior");
            patterClip = SfxClips.Get("weather-rain-canopy");
            rush.clip = rushClip;
            patter.clip = patterClip;
            return ClipsReady;
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

        private void Stop()
        {
            if (rush != null) { rush.Stop(); rush.volume = 0f; }
            if (patter != null) { patter.Stop(); patter.volume = 0f; }
            FxVoiceBus.EndLoop(VoiceId);
            playing = false;
            active = null;
        }

        internal void Release() => Stop();

        private void OnDestroy() => Stop();
    }
}
