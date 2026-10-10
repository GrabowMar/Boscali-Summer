using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Recorded cockpit one-shots on a single 2D voice: a hull thud when a warhead hits, a sparse ricochet over
    /// the game's own bullet thunk, harness creaks as the load changes, and the kneeboard page flip.
    /// Event-driven: the only per-frame work is one comparison for the creak.
    /// </summary>
    internal sealed class CockpitFoley
    {
        private const float PingGap = .45f, ThudGap = .3f;
        private static readonly string[] Pings = { "hit-ping-0", "hit-ping-1", "hit-ping-2", "hit-ping-3" };
        private static readonly string[] Creaks = { "harness-creak-0", "harness-creak-1", "harness-creak-2" };
        private readonly System.Random rng = new System.Random(7331);
        private AudioSource source;
        private StrainCreak strain;
        private float pingAt, thudAt;
        private bool routed;

        internal int Played { get; private set; }

        internal void Initialize(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.loop = false;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }

        internal void Hit(float pierce, float blast)
        {
            float now = Time.unscaledTime;
            switch (FoleyMath.Hit(pierce, blast))
            {
                case HitSound.Thud:
                    if (now >= thudAt && Play("hull-thud", .8f)) thudAt = now + ThudGap;
                    break;
                case HitSound.Ping:
                    if (now >= pingAt && Play(Pings[rng.Next(Pings.Length)], FoleyMath.PingVolume(pierce))) pingAt = now + PingGap;
                    break;
            }
        }

        internal void Tick(float loadG, float dt, bool enabled)
        {
            if (strain.Step(loadG, dt) && enabled)
                Play(Creaks[rng.Next(Creaks.Length)], Mathf.Lerp(.25f, .55f, (loadG - 3f) / 5f));
        }

        internal void PageFlip() => Play("page-flip", .5f);

        /// <summary>Scene or cockpit change: forget the load history and look the mixer up again.</summary>
        internal void Reset() { strain = default; routed = false; }

        internal void Release()
        {
            if (source != null) Object.Destroy(source);
            source = null;
            routed = false;
        }

        private bool Play(string name, float volume)
        {
            if (source == null) return false;
            if (!routed)
            {
                SoundManager sound = SoundManager.i;
                AudioMixerGroup group = sound != null ? sound.EffectsMixer : null;
                if (group == null) return false;
                source.outputAudioMixerGroup = group;
                routed = true;
            }
            AudioClip clip = SfxClips.Get(name);
            if (clip == null || !FxVoiceBus.TryOneShot(clip.length)) return false;
            source.PlayOneShot(clip, volume);
            Played++;
            return true;
        }
    }
}
