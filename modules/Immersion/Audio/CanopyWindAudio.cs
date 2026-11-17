using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Math;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Cockpit aerodynamic slipstream and canopy wind rush sound.
    /// Uses a shared recording, loaded when the cockpit first needs it.
    /// Modulates volume, timbre and stereo balance with airspeed, rough air and lateral airflow.
    /// Integrates with FxVoiceBus to adhere to loop voice limits.
    /// </summary>
    internal sealed class CanopyWindAudio : MonoBehaviour
    {
        private const string VoiceId = "cockpit-wind";

        private AudioSource windSource;
        private GameObject windRoot;
        private AudioClip windClip;
        private AudioLowPassFilter windFilter;
        private bool clipLoaded;
        private bool clipsReady;
        private bool routed;
        private bool isLoopActive;
        internal bool IsPlaying => windSource != null && windSource.isPlaying;
        internal float Volume => windSource != null ? windSource.volume : 0f;
        internal float Pan => windSource != null ? windSource.panStereo : 0f;

        public void Initialize()
        {
            windRoot = new GameObject("CanopyWind");
            windRoot.transform.SetParent(transform, false);
            windSource = windRoot.AddComponent<AudioSource>();
            windSource.loop = true;
            windSource.playOnAwake = false;
            windSource.spatialBlend = 0f;
            windSource.dopplerLevel = 0f;
            windSource.reverbZoneMix = 0f;
            windSource.volume = 0f;
            windFilter = windRoot.AddComponent<AudioLowPassFilter>();
            windFilter.cutoffFrequency = 3200f;
        }

        public void Tick(float airspeedMps, float gustMps, bool cockpitView, bool enabled, float dt,
            float moisture = 0f, float altitudeM = 0f, float slip01 = 0f, float turbulence01 = 0f)
        {
            if (!enabled || !cockpitView || airspeedMps < 40f ||
                (moisture > 0.08f && FxBus.Scales.Voices < 0.75f))
            {
                Silence();
                return;
            }

            if (!clipsReady && !TryLoadClip()) return;
            if (!routed) return;

            var (targetVolume, targetPitch, targetPan, targetCutoff) = CanopyWindMath.Target(airspeedMps, gustMps,
                Isa.Density(altitudeM) / Isa.SeaLevelDensity, moisture, Time.unscaledTime, slip01, turbulence01);
            dt = Mathf.Clamp(dt, 0f, 0.05f);
            windFilter.cutoffFrequency = Mathf.MoveTowards(windFilter.cutoffFrequency,
                targetCutoff, dt * 3000f);
            windSource.panStereo = Mathf.MoveTowards(windSource.panStereo, targetPan, dt * 0.5f);

            if (targetVolume > 0.01f)
            {
                if (!isLoopActive && FxVoiceBus.TryStartLoop(VoiceId))
                {
                    isLoopActive = true;
                    if (!windSource.isPlaying) windSource.Play();
                }

                if (isLoopActive)
                {
                    windSource.volume = Mathf.MoveTowards(windSource.volume, targetVolume, 0.7f * dt);
                    windSource.pitch = Mathf.MoveTowards(windSource.pitch, targetPitch, 1.5f * dt);
                }
            }
            else
            {
                Silence();
            }
        }

        public void TryRoute()
        {
            if (routed || windSource == null) return;
            SoundManager sound = SoundManager.i;
            AudioMixerGroup group = sound != null ? sound.EffectsMixer : null;
            if (group == null) return;
            windSource.outputAudioMixerGroup = group;
            routed = true;
        }

        public void Silence()
        {
            if (isLoopActive)
            {
                FxVoiceBus.EndLoop(VoiceId);
                isLoopActive = false;
            }
            routed = false;
            if (windSource != null)
            {
                windSource.Stop();
                windSource.volume = 0f;
                windSource.panStereo = 0f;
            }
        }

        public void Release()
        {
            Silence();
            if (windRoot != null) { Destroy(windRoot); windRoot = null; }
            windSource = null;
            windClip = null; // SfxClips owns the shared recording.
            windFilter = null;
            clipsReady = clipLoaded = false;
        }

        private bool TryLoadClip()
        {
            if (clipLoaded) return windClip != null;
            clipLoaded = true;
            windClip = SfxClips.Get("cockpit-wind");
            if (windClip == null) return false;
            windSource.clip = windClip;
            clipsReady = true;
            return true;
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}
