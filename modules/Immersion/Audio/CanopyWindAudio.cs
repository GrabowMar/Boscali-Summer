using System.Threading;
using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Cockpit aerodynamic slipstream and canopy wind rush sound.
    /// Uses a procedurally synthesized pink-noise loop pre-baked on a worker thread.
    /// Modulates volume and pitch according to airspeed and angle of attack.
    /// Integrates with FxVoiceBus to adhere to loop voice limits.
    /// </summary>
    internal sealed class CanopyWindAudio : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const int LoopSeconds = 2;
        private const string VoiceId = "cockpit-wind";

        private AudioSource windSource;
        private AudioClip windClip;
        private float[] bakeBuffer;
        private volatile int bakeState; // 0 pending, 1 ready, 2 failed
        private bool clipsReady;
        private bool routed;
        private bool isLoopActive;

        public void Initialize()
        {
            windSource = gameObject.AddComponent<AudioSource>();
            windSource.loop = true;
            windSource.playOnAwake = false;
            windSource.spatialBlend = 0f;
            windSource.volume = 0f;

            bakeState = 0;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    bakeBuffer = BuildWindNoise(LoopSeconds, SampleRate);
                    bakeState = 1;
                }
                catch (System.Exception)
                {
                    bakeState = 2;
                }
            });
        }

        public void Tick(float airspeedMps, float gLoad, bool cockpitView, bool enabled, float dt)
        {
            if (!enabled || !cockpitView || airspeedMps < 40f)
            {
                Silence();
                return;
            }

            if (!clipsReady && !TryCreateClip()) return;

            var (targetVolume, targetPitch) = ImmersionMath.WindRush(airspeedMps, gLoad, 1f);

            if (targetVolume > 0.01f)
            {
                if (!isLoopActive && FxVoiceBus.TryStartLoop(VoiceId))
                {
                    isLoopActive = true;
                    if (!windSource.isPlaying) windSource.Play();
                }

                if (isLoopActive)
                {
                    windSource.volume = Mathf.MoveTowards(windSource.volume, targetVolume * 0.7f, 2.5f * dt);
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
            if (windSource != null && windSource.isPlaying)
            {
                windSource.Stop();
                windSource.volume = 0f;
            }
        }

        private bool TryCreateClip()
        {
            if (bakeState != 1 || bakeBuffer == null) return false;
            windClip = AudioClip.Create("BoscaliCanopyWind", bakeBuffer.Length, 1, SampleRate, false);
            windClip.SetData(bakeBuffer, 0);
            bakeBuffer = null;
            windSource.clip = windClip;
            clipsReady = true;
            return true;
        }

        /// <summary>
        /// Synthesizes a smooth, seamless pink/turbulent noise loop on a background thread.
        /// </summary>
        private static float[] BuildWindNoise(float seconds, int sampleRate)
        {
            int total = (int)(seconds * sampleRate);
            float[] data = new float[total];
            var rng = new System.Random(0x3B47);

            // Pink noise filter state (Paul Kellet filter)
            float b0 = 0f, b1 = 0f, b2 = 0f;
            for (int i = 0; i < total; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                b0 = 0.99886f * b0 + white * 0.0555179f;
                b1 = 0.99332f * b1 + white * 0.0750759f;
                b2 = 0.96900f * b2 + white * 0.1538520f;
                float pink = b0 + b1 + b2 + white * 0.5362f;
                data[i] = pink * 0.12f;
            }

            // Crossfade seam so looping is inaudible
            const int seam = 440;
            for (int i = 0; i < seam; i++)
            {
                float t = i / (float)seam;
                data[total - seam + i] = data[total - seam + i] * (1f - t) + data[i] * t;
            }

            return data;
        }

        private void OnDestroy()
        {
            Silence();
            if (windClip != null) Destroy(windClip);
        }
    }
}
