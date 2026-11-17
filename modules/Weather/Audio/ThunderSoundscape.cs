using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Audio
{
    /// <summary>Twelve pending arrivals and three sources. Inaudible/refused arrivals are dropped.</summary>
    internal sealed class ThunderSoundscape
    {
        private struct Arrival { internal float Due, Distance, Pan; internal uint Seed; }
        /// <summary>Thunder is queued from this far (2026-10-18: was 10 km, which left most storms silent; past ~10 km it is a filtered rumble).</summary>
        internal const float MaxDistance = 25000f;
        private const int Voices = 3;
        private readonly Arrival[] queue = new Arrival[12];
        private readonly AudioSource[] sources = new AudioSource[Voices];
        private readonly AudioLowPassFilter[] filters = new AudioLowPassFilter[Voices];
        private readonly float[] distances = new float[Voices];
        private readonly AudioClip[] clips = new AudioClip[2];
        private GameObject root;
        private int count;
        internal int Queued => count;
        internal int Playing
        {
            get
            {
                int playing = 0;
                foreach (AudioSource source in sources) if (source != null && source.isPlaying) playing++;
                return playing;
            }
        }

        internal void Enqueue(float due, float distance, uint seed, float pan)
        {
            if (distance < 0f || distance > MaxDistance || float.IsNaN(distance) || float.IsInfinity(distance) ||
                float.IsNaN(due) || float.IsInfinity(due) || count >= queue.Length) return;
            queue[count++] = new Arrival { Due = due, Distance = distance, Seed = seed, Pan = Mathf.Clamp(pan, -0.75f, 0.75f) };
        }

        internal void Tick(float missionTime, bool enabled, bool cockpit)
        {
            if (!enabled) { Clear(); return; }
            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null || !sources[i].isPlaying) continue;
                // A sounding arrival follows the current view, including a cockpit switch.
                sources[i].volume = Mathf.MoveTowards(sources[i].volume,
                    LightningMath.ThunderGain(distances[i], cockpit ? 0.55f : 0.7f), dt * 1.5f);
                filters[i].cutoffFrequency = Mathf.MoveTowards(filters[i].cutoffFrequency,
                    EnvironmentAudioMath.ThunderCutoff(distances[i], cockpit), dt * 9000f);
            }
            for (int i = count - 1; i >= 0; i--)
            {
                Arrival arrival = queue[i]; if (arrival.Due > missionTime) continue;
                queue[i] = queue[--count]; if (missionTime - arrival.Due > 1f) continue;
                var mixer = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
                if (mixer == null) continue;
                int free = -1;
                for (int v = 0; v < Voices && free < 0; v++)
                    if (sources[v] == null || !sources[v].isPlaying) free = v;
                if (free < 0) continue;
                EnsureSources(); if (sources[free] == null) continue;
                AudioSource source = sources[free]; source.pitch = 0.9f + WeatherMath.Hash01(arrival.Seed, 20) * 0.15f;
                source.clip = clips[(int)(arrival.Seed & 1)];
                if (source.clip == null || !FxVoiceBus.TryOneShot(source.clip.length / source.pitch)) continue;
                source.outputAudioMixerGroup = mixer;
                distances[free] = arrival.Distance;
                filters[free].cutoffFrequency = EnvironmentAudioMath.ThunderCutoff(arrival.Distance, cockpit);
                source.volume = LightningMath.ThunderGain(arrival.Distance, cockpit ? 0.55f : 0.7f);
                source.panStereo = arrival.Pan; source.Play();
                // Close strikes rumble through the airframe (view-only; also drives the cockpit rattle).
                float rumble = cockpit ? 1f - Mathf.Clamp01(arrival.Distance / 3000f) : 0f;
                if (rumble > 0f) SceneSingleton<CameraStateManager>.i?.cockpitState?.AddShake(rumble * 0.3f, rumble * 0.1f);
            }
        }

        private void EnsureSources()
        {
            if (root != null) return;
            root = new GameObject("BoscaliThunder");
            clips[0] = SfxClips.Get("weather-thunder-0");
            clips[1] = SfxClips.Get("weather-thunder-1");
            for (int i = 0; i < Voices; i++)
            {
                var voiceRoot = new GameObject("ThunderVoice" + i);
                voiceRoot.transform.SetParent(root.transform, false);
                sources[i] = voiceRoot.AddComponent<AudioSource>();
                sources[i].playOnAwake = false; sources[i].spatialBlend = 0f;
                sources[i].dopplerLevel = 0f; sources[i].reverbZoneMix = 0f;
                filters[i] = voiceRoot.AddComponent<AudioLowPassFilter>();
            }
        }

        internal void Clear() { count = 0; foreach (AudioSource source in sources) if (source != null) source.Stop(); }
        internal void Dispose()
        {
            Clear(); if (root != null) UnityEngine.Object.Destroy(root); root = null;
            for (int i = 0; i < Voices; i++) { sources[i] = null; filters[i] = null; }
            for (int i = 0; i < clips.Length; i++) clips[i] = null;
        }
    }
}
