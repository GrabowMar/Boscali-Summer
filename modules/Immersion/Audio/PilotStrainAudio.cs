using System.Threading;
using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Procedural pilot breathing and Anti-G Straining Maneuver (AGSM) sound.
    /// Under sustained G-loads &gt; 4.5G, plays rhythmic pressurized exhalation and strain grunts.
    /// Synthesized on a worker thread and managed under FxVoiceBus one-shot budgeting.
    /// </summary>
    internal sealed class PilotStrainAudio : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const float BreathSeconds = 0.85f;

        private AudioSource audioSource;
        private AudioClip strainClip;
        private float[] bakeBuffer;
        private volatile int bakeState; // 0 pending, 1 ready, 2 failed
        private bool clipsReady;
        private bool routed;
        private float nextStrainTime;
        private readonly System.Random rng = new System.Random(4041);

        public void Initialize()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = false;
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.7f;

            bakeState = 0;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    bakeBuffer = BuildStrainBreath(BreathSeconds, SampleRate);
                    bakeState = 1;
                }
                catch (System.Exception)
                {
                    bakeState = 2;
                }
            });
        }

        public void Tick(Vector3 forceG, bool cockpitView, bool enabled, float dt)
        {
            if (!enabled || !cockpitView || forceG.y < 4.5f)
            {
                nextStrainTime = 0f;
                return;
            }

            if (!clipsReady && !TryCreateClip()) return;

            float interval = ImmersionMath.PilotStrainInterval(forceG.y);
            if (interval <= 0f) return;

            if (Time.unscaledTime >= nextStrainTime)
            {
                nextStrainTime = Time.unscaledTime + interval + (float)rng.NextDouble() * 0.4f;

                if (FxVoiceBus.TryOneShot(0.85f))
                {
                    audioSource.pitch = 0.9f + (float)rng.NextDouble() * 0.2f;
                    float vol = Mathf.Clamp01(0.4f + (forceG.y - 4.5f) * 0.12f);
                    audioSource.PlayOneShot(strainClip, vol);
                }
            }
        }

        public void TryRoute()
        {
            if (routed || audioSource == null) return;
            SoundManager sound = SoundManager.i;
            AudioMixerGroup group = sound != null ? sound.EffectsMixer : null;
            if (group == null) return;
            audioSource.outputAudioMixerGroup = group;
            routed = true;
        }

        public void Silence()
        {
            nextStrainTime = 0f;
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        private bool TryCreateClip()
        {
            if (bakeState != 1 || bakeBuffer == null) return false;
            strainClip = AudioClip.Create("BoscaliPilotStrain", bakeBuffer.Length, 1, SampleRate, false);
            strainClip.SetData(bakeBuffer, 0);
            bakeBuffer = null;
            clipsReady = true;
            return true;
        }

        /// <summary>
        /// Synthesizes a vocalized grunting exhalation: sharp pressurized breath noise through glottis.
        /// </summary>
        private static float[] BuildStrainBreath(float duration, int sampleRate)
        {
            int total = (int)(duration * sampleRate);
            float[] data = new float[total];
            var rng = new System.Random(8812);

            for (int i = 0; i < total; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / duration));
                envelope = envelope * envelope; // Sharp attack and release

                // Breath noise
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Resonant throat vocalization (~180 Hz)
                float vocal = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.35f;

                data[i] = (noise * 0.65f + vocal) * envelope * 0.28f;
            }

            return data;
        }

        private void OnDestroy()
        {
            Silence();
            if (strainClip != null) Destroy(strainClip);
        }
    }
}
