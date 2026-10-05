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
            if (distance > 10000f || count >= queue.Length) return;
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
                source.volume = LightningMath.ThunderGain(arrival.Distance, cockpit ? 0.48f : 0.7f);
                source.panStereo = arrival.Pan; source.Play();
            }
        }

        private void EnsureSources(float[][] data)
        {
            if (root != null) return;
            root = new GameObject("BoscaliThunder");
            for (int i = 0; i < 2; i++)
            {
                sources[i] = root.AddComponent<AudioSource>();
                sources[i].playOnAwake = false; sources[i].spatialBlend = 0f;
                clips[i] = AudioClip.Create("BoscaliThunder" + i, data[i].Length, 1, 22050, false); clips[i].SetData(data[i], 0);
            }
        }

        internal static float[] Bake(int seed)
        {
            var random = new System.Random(seed); var data = new float[22050 * 6];
            float low = 0f, mid = 0f, peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / 22050f, white = (float)(random.NextDouble() * 2 - 1);
                low += 0.008f * (white - low); mid += 0.04f * (white - mid);
                float envelope = Math.Min(1f, t * 50f) * (float)Math.Exp(-t / 1.5f) * Math.Min(1f, (6f - t) * 2f);
                float roll = 0.7f + 0.3f * (float)Math.Sin(t * 10f + seed);
                data[i] = (low * 4f + mid * 0.5f) * envelope * roll; peak = Math.Max(peak, Math.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < data.Length; i++) data[i] *= 0.72f / peak;
            return data;
        }
        internal void Clear() { count = 0; foreach (AudioSource source in sources) if (source != null) source.Stop(); }
        internal void Dispose()
        {
            Clear(); if (root != null) UnityEngine.Object.Destroy(root); root = null;
            for (int i = 0; i < 2; i++) { sources[i] = null; if (clips[i] != null) UnityEngine.Object.Destroy(clips[i]); clips[i] = null; }
        }
    }
}
