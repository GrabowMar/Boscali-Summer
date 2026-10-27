using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Immersion.Runtime;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// The snap of a supersonic round going past (2026-10-27): vanilla is silent until a round hits something. A bullet
    /// cracks once, in the trace step where it passes abeam the camera (closest point strictly inside the step), when it is
    /// still faster than sound and misses by under <see cref="Radius"/>. The clips are recorded snaps (Core/Fx/Sfx near-crack-*,
    /// ElevenLabs); heavy rounds play them lower and jolt the view. Three 3D voices round-robin.
    /// </summary>
    internal sealed class NearMissCrack
    {
        internal const float Radius = 40f;
        private const float MinGap = .07f, SpeedOfSound = 340f;
        private static float lastAt = -1f;
        private readonly AudioSource[] voices = new AudioSource[3];
        private static readonly string[] Clips = { "near-crack-0", "near-crack-1" };
        private int next;
        private bool routed;

        internal int Played { get; private set; }

        internal NearMissCrack(GameObject host)
        {
            for (int i = 0; i < voices.Length; i++)
            {
                var go = new GameObject("BoscaliCrack" + i) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(host.transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 4f;
                s.maxDistance = 200f;
                s.dopplerLevel = 0f;
                voices[i] = s;
            }
        }

        /// <summary>Hot path, once per bullet per physics step: plain math until a round really passes close.</summary>
        internal static void Consider(GlobalPosition from, GlobalPosition to, Vector3 velocity, WeaponInfo info, Unit owner, GlobalPosition ear)
        {
            if (velocity.sqrMagnitude < SpeedOfSound * SpeedOfSound) return;
            Vector3 step = to - from, rel = ear - from;
            float length2 = step.sqrMagnitude;
            if (length2 < .01f) return;
            float t = Vector3.Dot(rel, step) / length2;
            if (t <= 0f || t > 1f) return;
            float miss = (rel - step * t).magnitude;
            if (miss > Radius) return;
            float now = Time.unscaledTime;
            if (now - lastAt < MinGap) return;
            if (owner != null && GameManager.IsLocalAircraft(owner)) return;
            lastAt = now;
            WorldFx.Live?.Crack.Play((from + step * t).ToLocalPosition(), info != null ? info.massPerRound : 0f, miss);
        }

        private void Play(Vector3 at, float massPerRound, float miss)
        {
            AudioClip clip = SfxClips.Get(Clips[Random.Range(0, Clips.Length)]);
            if (clip == null || !FxVoiceBus.TryOneShot(clip.length)) return;
            AudioSource s = voices[next];
            next = (next + 1) % voices.Length;
            if (s == null) return;
            if (!routed)
            {
                AudioMixerGroup group = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
                if (group != null) { for (int i = 0; i < voices.Length; i++) if (voices[i] != null) voices[i].outputAudioMixerGroup = group; routed = true; }
            }
            // 10 g rifle rounds high and light, 1 kg cannon shells low and heavy; no mass data plays neutral.
            float heavy = massPerRound > 0f ? Mathf.InverseLerp(Mathf.Log(.01f), Mathf.Log(1f), Mathf.Log(massPerRound)) : .4f;
            s.transform.position = at;
            s.pitch = Mathf.Lerp(1.15f, .7f, heavy) * Random.Range(.92f, 1.08f);
            s.PlayOneShot(clip, Mathf.Lerp(.6f, 1f, heavy));
            Played++;
            if (heavy > .55f && SceneSingleton<CameraStateManager>.i != null)
                SceneSingleton<CameraStateManager>.i.ShakeCamera(Mathf.Clamp01(1f - miss / Radius) * .6f, 0f);
        }

        internal void Reset() => next = 0;

        internal void Release()
        {
            for (int i = 0; i < voices.Length; i++) if (voices[i] != null) Object.Destroy(voices[i].gameObject);
            routed = false;
        }
    }
}
