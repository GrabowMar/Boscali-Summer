using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>One pressure arrival per observed event, followed by a long original roar and dust-wind tail.</summary>
    [DefaultExecutionOrder(1950)]
    internal sealed class Sa74BlastAudio : MonoBehaviour, ISceneService
    {
        private const int MaximumVoices = 3, MaximumPending = 32;
        private const float MaximumDistance = 160000f;
        private static readonly float MaximumArrivalAge = ArrivalSeconds(MaximumDistance) + 2f;
        private static Sa74BlastAudio instance;
        private readonly AudioSource[] sources = new AudioSource[MaximumVoices];
        private readonly AudioLowPassFilter[] filters = new AudioLowPassFilter[MaximumVoices];
        private readonly float[] output = new float[512];
        private readonly HashSet<uint> seen = new HashSet<uint>();
        private readonly List<Arrival> arrivals = new List<Arrival>();
        private GameObject audioRoot;
        private AudioClip clip;
        private bool audioFailed;
        private uint epoch;
        private float nextOutputSample;

        private sealed class Arrival
        {
            internal Sa74Fallout.Site Site;
            internal float LastAge;
            internal int Voice = -1;
            internal bool Resolved;
        }

        internal static bool Ready => instance != null && instance.clip != null;
        internal static string ClipName => Ready ? instance.clip.name : "";
        internal static bool EffectsRouted
        {
            get
            {
                var mixer = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
                if (instance == null || mixer == null) return false;
                foreach (AudioSource source in instance.sources)
                    if (source != null && source.isPlaying && source.outputAudioMixerGroup == mixer) return true;
                return false;
            }
        }
        internal static bool OwnsNativeAudio => Ready;
        internal static int Played { get; private set; }
        internal static int Observed { get; private set; }
        internal static int Skipped { get; private set; }
        internal static int NativeSuppressed { get; private set; }
        internal static uint LastEvent { get; private set; }
        internal static float LastDelay { get; private set; }
        internal static float Gain { get; private set; }
        internal static float OutputRms { get; private set; }
        internal static int Pending
        {
            get
            {
                int count = 0;
                if (instance != null) foreach (Arrival arrival in instance.arrivals) if (!arrival.Resolved) count++;
                return count;
            }
        }
        internal static int Voices
        {
            get
            {
                int count = 0;
                if (instance != null) foreach (AudioSource source in instance.sources) if (source != null && source.isPlaying) count++;
                return count;
            }
        }

        private void Awake()
        {
            instance = this;
            epoch = Sa74Fallout.Epoch;
            if (Application.isBatchMode) return;
            EnsureAudio();
        }

        /// <summary>Call once from ApplySite. Aged recovery states never replay a pressure arrival.</summary>
        internal static void Observe(Sa74Fallout.Site site, bool fresh)
        {
            Sa74BlastAudio owner = instance;
            if (owner == null || Application.isBatchMode || site == null) return;
            if (owner.epoch != Sa74Fallout.Epoch) owner.ResetForScene();
            if (!owner.seen.Add(site.Id)) return;
            float age = Mathf.Max(0f, Time.time - site.Started);
            Observed++;
            if (!fresh || age > 2f || owner.audioFailed) { Skipped++; return; }
            owner.arrivals.RemoveAll(a => a.Resolved && a.Voice < 0);
            if (owner.arrivals.Count >= MaximumPending) { Skipped++; return; }
            owner.arrivals.Add(new Arrival { Site = site, LastAge = age });
        }

        internal static float ArrivalSeconds(float distance) => Math.Max(0f,
            (distance - (float)Math.Pow(Sa74Fallout.YieldKilotons * 1e6, .3333) * .5f) / 340f);

        private void EnsureAudio()
        {
            if (clip != null || audioFailed) return;
            clip = SfxClips.Get("sa74-blast");
            if (clip == null) { audioFailed = true; return; }
            audioRoot = new GameObject("SA-74N pressure arrival voices");
            audioRoot.transform.SetParent(transform, false);
            for (int i = 0; i < MaximumVoices; i++)
            {
                var voice = new GameObject("PressureVoice" + i);
                voice.transform.SetParent(audioRoot.transform, false);
                AudioSource source = sources[i] = voice.AddComponent<AudioSource>();
                source.clip = clip; source.playOnAwake = source.loop = false;
                source.spatialBlend = source.dopplerLevel = source.reverbZoneMix = 0f;
                source.priority = 20; source.volume = 0f;
                filters[i] = voice.AddComponent<AudioLowPassFilter>();
            }
        }

        private void LateUpdate()
        {
            if (Application.isBatchMode) return;
            EnsureAudio();
            if (epoch != Sa74Fallout.Epoch || (!Sa74Fallout.Active && (arrivals.Count != 0 || seen.Count != 0))) ResetForScene();
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera camera = cameras != null ? cameras.mainCamera : Camera.main;
            if (camera == null) return;
            bool cockpit = cameras != null && cameras.currentState == cameras.cockpitState;
            var mixer = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
            Gain = 0f;
            foreach (Arrival arrival in arrivals)
            {
                if (arrival.Resolved && arrival.Voice < 0) continue;
                float age = Mathf.Max(0f, Time.time - arrival.Site.Started);
                if (arrival.Voice < 0 && age > MaximumArrivalAge)
                {
                    arrival.Resolved = true; Skipped++;
                    continue;
                }
                float delta = age - arrival.LastAge;
                arrival.LastAge = age;
                // Fixture scrubbing/clock rewinds cannot turn an old view into another audible detonation.
                if (delta < -.15f || delta > Mathf.Max(.75f, Time.deltaTime * 2f + .15f))
                {
                    if (arrival.Voice >= 0) sources[arrival.Voice].Stop();
                    arrival.Voice = -1; arrival.Resolved = true; Skipped++;
                    continue;
                }
                Vector3 direction = arrival.Site.Position.ToLocalPosition() - camera.transform.position;
                float distance = direction.magnitude;
                float gain = .95f / Mathf.Sqrt(1f + distance * distance / (70000f * 70000f));
                if (cockpit) gain *= .72f;
                if (arrival.Voice >= 0)
                {
                    if (!sources[arrival.Voice].isPlaying) { arrival.Voice = -1; continue; }
                }
                else
                {
                    if (age < ArrivalSeconds(distance) || clip == null || mixer == null) continue;
                    if (distance > MaximumDistance) { arrival.Resolved = true; Skipped++; continue; }
                    int free = -1;
                    for (int i = 0; i < MaximumVoices && free < 0; i++) if (!sources[i].isPlaying) free = i;
                    arrival.Resolved = true;
                    // This replaces the native nuclear blast: ordinary Foley must never consume its three dedicated voices.
                    if (free < 0) { Skipped++; continue; }
                    arrival.Voice = free;
                    Played++; LastEvent = arrival.Site.Id; LastDelay = ArrivalSeconds(distance);
                    if (cameras != null) cameras.ShakeCamera(.65f * gain, .15f * gain);
                }
                AudioSource source = sources[arrival.Voice];
                source.outputAudioMixerGroup = mixer;
                source.volume = gain;
                source.panStereo = distance > 1f ? Mathf.Clamp(Vector3.Dot(camera.transform.right, direction / distance) * .45f, -.45f, .45f) : 0f;
                filters[arrival.Voice].cutoffFrequency = Mathf.Min(cockpit ? 2600f : 9000f, 9000f / (1f + distance / 18000f));
                Gain = Mathf.Max(Gain, gain);
                if (!source.isPlaying) source.Play();
            }
            if (Time.unscaledTime < nextOutputSample) return;
            nextOutputSample = Time.unscaledTime + .1f;
            OutputRms = 0f;
            foreach (AudioSource source in sources)
            {
                if (source == null || !source.isPlaying) continue;
                source.GetOutputData(output, 0);
                float energy = 0f;
                foreach (float sample in output) energy += sample * sample;
                OutputRms = Mathf.Max(OutputRms, Mathf.Sqrt(energy / output.Length));
            }
        }

        public void ResetForScene()
        {
            foreach (AudioSource source in sources) if (source != null) source.Stop();
            arrivals.Clear(); seen.Clear(); epoch = Sa74Fallout.Epoch;
            Played = Observed = Skipped = NativeSuppressed = 0;
            LastEvent = 0; LastDelay = Gain = OutputRms = 0f;
        }
        private void OnDestroy()
        {
            ResetForScene();
            if (audioRoot != null) Destroy(audioRoot);
            if (instance == this) instance = null;
        }

        internal static void CountNativeSuppressed() => NativeSuppressed++;
    }

    /// <summary>SA-74N has one dedicated delayed voice; other nuclear/conventional effects keep their original audio.</summary>
    [HarmonyPatch(typeof(ExplosionAudio), "Start")]
    internal static class Sa74NativeBlastAudioPatch
    {
        private static bool Prefix(ExplosionAudio __instance)
        {
            if (!Sa74BlastAudio.OwnsNativeAudio || __instance.GetComponentInParent<Sa74BlastTag>() == null) return true;
            foreach (AudioSource source in __instance.GetComponentsInChildren<AudioSource>(true)) { source.Stop(); source.enabled = false; }
            Sa74BlastAudio.CountNativeSuppressed();
            UnityEngine.Object.Destroy(__instance);
            return false;
        }
    }
}
