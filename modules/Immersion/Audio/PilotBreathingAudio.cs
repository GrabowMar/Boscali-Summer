using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Oxygen-mask breathing, anti-G straining grunts, G-suit hiss and heartbeat, paced by
    /// <see cref="PilotPhysiology"/> cues. Three pooled 2D voices on the effects mixer, so vanilla's
    /// G-LOC master low-pass muffles them for free. Shared recordings are loaded once;
    /// the per-frame path only reads cue flags and follows the existing breath clock.
    /// </summary>
    internal sealed class PilotBreathingAudio : MonoBehaviour
    {
        private const int SoundCount = (int)BreathSound.Heart + 1;
        private readonly AudioClip[] clips = new AudioClip[SoundCount];
        private readonly System.Random rng = new System.Random(4041);
        private AudioSource breath, effort, body;
        private static readonly string[] ClipIds =
        {
            "pilot-inhale-0", "pilot-inhale-1", "pilot-inhale-2",
            "pilot-exhale-0", "pilot-exhale-1", "pilot-exhale-2",
            "pilot-pressure-inhale", "pilot-suit-inflate", "pilot-suit-vent",
            "pilot-keh-0", "pilot-keh-1", "pilot-keh-2", "pilot-keh-3", "pilot-keh-4",
            "pilot-gasp-0", "pilot-gasp-1", "pilot-recovery", "pilot-heart"
        };
        private bool ready, routed, silent = true;

        internal int Played { get; private set; }
        /// <summary>How many shared recordings are available (automation readout).</summary>
        internal int RecordedVoices { get; private set; }
        internal string LastClipId { get; private set; } = "";
        internal bool EffectsRouted => breath != null && SoundManager.i != null && SoundManager.i.EffectsMixer != null &&
            breath.outputAudioMixerGroup == SoundManager.i.EffectsMixer &&
            effort.outputAudioMixerGroup == SoundManager.i.EffectsMixer && body.outputAudioMixerGroup == SoundManager.i.EffectsMixer;
        internal bool IsPlaying => (breath != null && breath.isPlaying) || (effort != null && effort.isPlaying) || (body != null && body.isPlaying);

        public void Initialize()
        {
            breath = Voice(); effort = Voice(); body = Voice();
        }

        public void Tick(PilotCue cues, PilotPhysiology p, bool enabled)
        {
            if (!enabled || p == null) { Silence(); return; }
            if (!ready) LoadClips();
            if (!routed || cues == PilotCue.None) return;
            if ((cues & PilotCue.LostConsciousness) != 0) { breath.Stop(); effort.Stop(); }
            float e = p.Exertion01;
            float breathVolume = Mathf.Lerp(.07f, .45f, e); // cruise sits under the engine
            if ((cues & PilotCue.Inhale) != 0)
                Play(breath, p.Straining ? BreathSound.PressureInhale : Pick(BreathSound.Inhale0, 3), breathVolume, p.SegmentSeconds);
            if ((cues & PilotCue.Exhale) != 0) Play(breath, Pick(BreathSound.Exhale0, 3), breathVolume * .9f, p.SegmentSeconds);
            if ((cues & PilotCue.Grunt) != 0)
                Play(effort, p.Ragged ? Pick(BreathSound.Gasp0, 2) : Pick(BreathSound.Keh0, 5), Mathf.Lerp(.35f, .6f, e), 0f, p.Ragged ? 1.08f : 1f);
            if ((cues & PilotCue.RegainedConsciousness) != 0) Play(effort, BreathSound.Recovery, .55f, 0f);
            if ((cues & PilotCue.GSuitInflate) != 0) Play(body, BreathSound.SuitInflate, .18f, 0f);
            if ((cues & PilotCue.GSuitVent) != 0) Play(body, BreathSound.SuitVent, .15f, 0f);
            if ((cues & PilotCue.Heartbeat) != 0) Play(body, BreathSound.Heart, .45f * p.Pulse01, 0f);
        }

        public void TryRoute()
        {
            if (routed || breath == null) return;
            SoundManager sound = SoundManager.i;
            AudioMixerGroup group = sound != null ? sound.EffectsMixer : null;
            if (group == null) return;
            breath.outputAudioMixerGroup = effort.outputAudioMixerGroup = body.outputAudioMixerGroup = group;
            routed = true;
        }

        /// <summary>Stop all voices once; <paramref name="unroute"/> on scene reset, when the mixer may change.</summary>
        public void Silence(bool unroute = false)
        {
            if (unroute) routed = false;
            if (silent) return;
            silent = true;
            if (breath != null && breath.isPlaying) breath.Stop();
            if (effort != null && effort.isPlaying) effort.Stop();
            if (body != null && body.isPlaying) body.Stop();
        }

        public void Release()
        {
            silent = false;
            Silence(true);
            if (breath != null) Destroy(breath);
            if (effort != null) Destroy(effort);
            if (body != null) Destroy(body);
            breath = effort = body = null;
            for (int i = 0; i < clips.Length; i++) clips[i] = null; // Shared clips remain cached.
            ready = false;
        }

        private void Play(AudioSource source, BreathSound sound, float volume, float fitSeconds, float pitch = 1f)
        {
            AudioClip clip = clips[(int)sound];
            if (clip == null || volume <= .001f) return;
            // Fit a breath into its clock segment so the sound and the chest move together.
            float fitted = fitSeconds > 0f ? Mathf.Clamp(clip.length / fitSeconds, .85f, 1.3f) : pitch;
            // A refused voice must not re-pitch the clip already playing on this source.
            if (!FxVoiceBus.TryOneShot(clip.length / fitted)) return;
            source.pitch = fitted;
            source.PlayOneShot(clip, volume);
            LastClipId = ClipIds[(int)sound];
            silent = false;
            Played++;
        }

        private BreathSound Pick(BreathSound first, int count) => (BreathSound)((int)first + rng.Next(count));

        private AudioSource Voice()
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.loop = false;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            return source;
        }

        private void LoadClips()
        {
            RecordedVoices = 0;
            for (int i = 0; i < SoundCount; i++)
            {
                clips[i] = SfxClips.Get(ClipIds[i]);
                if (clips[i] != null) RecordedVoices++;
            }
            ready = true;
        }

        private void OnDestroy() => Release();
    }
}
