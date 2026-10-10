using System;
using System.Threading;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Audio
{
    /// <summary>Eight pending arrivals and two sources. Inaudible/refused arrivals are dropped.</summary>
    internal sealed class ThunderSoundscape
    {
        private struct Arrival { internal float Due, Distance, Pan; internal uint Seed; }
        private readonly Arrival[] queue = new Arrival[8];
        private readonly AudioSource[] sources = new AudioSource[2];
        private readonly AudioLowPassFilter[] filters = new AudioLowPassFilter[2];
        private readonly float[] distances = new float[2];
        private readonly AudioClip[] clips = new AudioClip[2];
        private static float[][] baked;
        private static int baking;
        private GameObject root;
        private int count;
        internal int Queued => count;
        internal int Playing => (sources[0] != null && sources[0].isPlaying ? 1 : 0) +
            (sources[1] != null && sources[1].isPlaying ? 1 : 0);

        internal void Enqueue(float due, float distance, uint seed, float pan)
        {
            if (distance < 0f || distance > 10000f || float.IsNaN(distance) || float.IsInfinity(distance) ||
                float.IsNaN(due) || float.IsInfinity(due) || count >= queue.Length) return;
            queue[count++] = new Arrival { Due = due, Distance = distance, Seed = seed, Pan = Mathf.Clamp(pan, -0.75f, 0.75f) };
            if (Volatile.Read(ref baked) == null && Interlocked.CompareExchange(ref baking, 1, 0) == 0)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Volatile.Write(ref baked, new[] { Bake(451), Bake(783) }); }
                    catch { Volatile.Write(ref baked, Array.Empty<float[]>()); }
                    finally { Interlocked.Exchange(ref baking, 0); }
                });
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
                float[][] data = Volatile.Read(ref baked); if (mixer == null || data == null || data.Length != 2) continue;
                int free = sources[0] == null || !sources[0].isPlaying ? 0 : sources[1] == null || !sources[1].isPlaying ? 1 : -1;
                if (free < 0) continue;
                EnsureSources(data); if (sources[free] == null) continue;
                AudioSource source = sources[free]; source.pitch = 0.9f + WeatherMath.Hash01(arrival.Seed, 20) * 0.15f;
                source.clip = clips[(int)(arrival.Seed & 1)];
                if (!FxVoiceBus.TryOneShot(source.clip.length / source.pitch)) continue;
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

        private void EnsureSources(float[][] data)
        {
            if (root != null) return;
            root = new GameObject("BoscaliThunder");
            for (int i = 0; i < 2; i++)
            {
                var voiceRoot = new GameObject("ThunderVoice" + i);
                voiceRoot.transform.SetParent(root.transform, false);
                sources[i] = voiceRoot.AddComponent<AudioSource>();
                sources[i].playOnAwake = false; sources[i].spatialBlend = 0f;
                sources[i].dopplerLevel = 0f; sources[i].reverbZoneMix = 0f;
                filters[i] = voiceRoot.AddComponent<AudioLowPassFilter>();
                clips[i] = AudioClip.Create("BoscaliThunder" + i, data[i].Length, 1, 22050, false); clips[i].SetData(data[i], 0);
            }
        }

        internal static float[] Bake(int seed)
        {
            var random = new System.Random(seed); var data = new float[22050 * 6];
            float low = 0f, mid = 0f, peak = 0f, movement = 0f;
            float restrike = 0.11f + (float)random.NextDouble() * 0.16f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / 22050f, white = (float)(random.NextDouble() * 2 - 1);
                low += 0.008f * (white - low); mid += 0.04f * (white - mid);
                float envelope = Math.Min(1f, t * 50f) * (float)Math.Exp(-t / 1.5f) * Math.Min(1f, (6f - t) * 2f);
                movement += 0.0001f * (white - movement);
                float roll = Math.Max(0.45f, Math.Min(1.2f, 0.8f + movement * 20f));
                float crack = (float)Math.Exp(-t * 48f) * Math.Min(1f, t * 600f);
                if (t >= restrike) crack += 0.5f * (float)Math.Exp(-(t - restrike) * 65f)
                    * Math.Min(1f, (t - restrike) * 600f);
                // A nearby arrival retains its brief crack; distance/shelter filtering
                // leaves a rolling low-frequency body instead of pitch-shifting all thunder.
                data[i] = (low * 4f + mid * 0.5f) * envelope * roll + (white - mid) * crack * 0.16f;
                peak = Math.Max(peak, Math.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < data.Length; i++) data[i] *= 0.72f / peak;
            return data;
        }
        internal void Clear() { count = 0; foreach (AudioSource source in sources) if (source != null) source.Stop(); }
        internal void Dispose()
        {
            Clear(); if (root != null) UnityEngine.Object.Destroy(root); root = null;
            for (int i = 0; i < 2; i++) { sources[i] = null; filters[i] = null; if (clips[i] != null) UnityEngine.Object.Destroy(clips[i]); clips[i] = null; }
        }
    }
}
