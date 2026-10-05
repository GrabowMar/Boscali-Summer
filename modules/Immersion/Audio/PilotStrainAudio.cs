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
        private volatile bool released;
        private bool clipsReady;
        private bool routed;
        private float nextStrainTime;
        private readonly System.Random rng = new System.Random(4041);
        internal bool IsPlaying => audioSource != null && audioSource.isPlaying;

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
                    float[] samples = BuildStrainBreath(BreathSeconds, SampleRate);
                    if (released) return;
                    bakeBuffer = samples;
                    bakeState = 1;
                }
                catch (System.Exception)
                {
                    bakeState = 2;
                }
            });
        }

        public void Tick(float exposure, bool cockpitView, bool enabled, float dt)
        {
            if (!enabled || !cockpitView || exposure < 0.12f)
            {
                Silence();
                return;
            }

            if (!clipsReady && !TryCreateClip()) return;
            if (!routed || dt <= 0f || audioSource.isPlaying) return;

            float interval = 3.2f - Mathf.Clamp01(exposure);
            if (interval <= 0f) return;

            if (Time.time >= nextStrainTime)
            {
                nextStrainTime = Time.time + interval + (float)rng.NextDouble() * 0.4f;
                audioSource.pitch = 0.9f + (float)rng.NextDouble() * 0.2f;
                if (FxVoiceBus.TryOneShot(strainClip.length / audioSource.pitch))
                {
                    float vol = Mathf.Clamp01(0.25f + exposure * 0.45f);
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
            routed = false;
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        public void Release()
        {
            released = true;
            Silence();
            if (audioSource != null) { Destroy(audioSource); audioSource = null; }
            if (strainClip != null) { Destroy(strainClip); strainClip = null; }
            bakeBuffer = null;
            clipsReady = false;
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
            Release();
        }
    }
}
