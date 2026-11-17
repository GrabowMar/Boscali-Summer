using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Recorded airframe creaks, fired when specific-force
    /// jerk spikes (hard pull, buffet onset, touchdown thump) with a cooldown so it seasons without nagging.
    /// Uses FxVoiceBus to adhere to the client FX voice budget.
    /// </summary>
    internal sealed class AirframeAudio : MonoBehaviour
    {
        private AudioSource creakSource;
        private AudioClip creakClip;
        private bool clipLoaded;
        private bool clipsReady;
        private bool routed;
        private Vector3 lastForce = Vector3.up;
        private bool primed;
        private float cooldown;
        private readonly System.Random rng = new System.Random(9182);

        /// <summary>Creaks fired since the scene loaded (automation readout).</summary>
        public int Creaks { get; private set; }
        internal bool IsPlaying => creakSource != null && creakSource.isPlaying;

        public void Initialize()
        {
            creakSource = gameObject.AddComponent<AudioSource>();
            creakSource.loop = false;
            creakSource.playOnAwake = false;
            creakSource.spatialBlend = 0f;
        }

        public void Tick(Vector3 forceG, float dt, bool cockpitView, float masterVolume, bool enabled)
        {
            cooldown -= dt;
            if (!primed)
            {
                lastForce = forceG;
                primed = true;
            }
            float jerk = dt > 0f ? (forceG - lastForce).magnitude / dt : 0f;
            lastForce = forceG;

            if (!enabled || !cockpitView) { Silence(); return; }
            if (!routed) return;
            if (!clipsReady && !TryLoadClip()) return;
            if (cooldown > 0f || masterVolume <= 0.01f || creakSource.isPlaying) return;

            float volume = ImmersionMath.CreakVolume(jerk);
            if (volume <= 0f) return;

            // Check global client FX voice bus allocation
            creakSource.pitch = 0.75f + (float)rng.NextDouble() * 0.5f;
            if (!FxVoiceBus.TryOneShot(creakClip.length / creakSource.pitch)) return;
            creakSource.PlayOneShot(creakClip, volume * masterVolume);
            cooldown = 0.6f + (float)rng.NextDouble() * 1.2f;
            Creaks++;
        }

        public void TryRoute()
        {
            if (routed || creakSource == null) return;
            SoundManager sound = SoundManager.i;
            AudioMixerGroup group = sound != null ? sound.EffectsMixer : null;
            if (group == null) return;
            creakSource.outputAudioMixerGroup = group;
            routed = true;
        }

        public void Silence()
        {
            primed = false;
            cooldown = 0f;
            routed = false;
            if (creakSource != null) creakSource.Stop();
        }

        public void Release()
        {
            Silence();
            if (creakSource != null) { Destroy(creakSource); creakSource = null; }
            creakClip = null; // SfxClips owns the shared recording.
            clipsReady = clipLoaded = false;
        }

        private bool TryLoadClip()
        {
            if (clipLoaded) return creakClip != null;
            clipLoaded = true;
            creakClip = SfxClips.Get("airframe-creak");
            if (creakClip == null) return false;
            clipsReady = true;
            return true;
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}
