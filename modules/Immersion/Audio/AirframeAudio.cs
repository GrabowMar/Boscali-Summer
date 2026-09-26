using System.Threading;
using BoscaliSummer.Features.Immersion.Domain;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Features.Immersion.Audio
{
    /// <summary>
    /// Procedural airframe creaks: one baked stick-slip groan clip, fired when the specific-force
    /// jerk spikes (hard pull, buffet onset, touchdown thump) with a cooldown so it never nags.
    /// Same shape as the Weather rain audio: worker-thread bake, main-thread clip, mixer-routed.
    /// </summary>
    internal sealed class AirframeAudio : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const float CreakSeconds = 1.4f;

        private AudioSource creakSource;
        private AudioClip creakClip;
        private float[] creakBake;
        private volatile int bakeState; // 0 pending, 1 ready, 2 failed
        private bool clipsReady;
        private bool routed;
        private Vector3 lastForce = Vector3.up;
        private bool primed;
        private float cooldown;
        private readonly System.Random rng = new System.Random(9182);

        /// <summary>Creaks fired since the scene loaded (automation readout).</summary>
        public int Creaks { get; private set; }

        public void Initialize()
        {
            creakSource = gameObject.AddComponent<AudioSource>();
            creakSource.loop = false;
            creakSource.playOnAwake = false;
            creakSource.spatialBlend = 0f;
            bakeState = 0;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    creakBake = BakeCreak(CreakSeconds, SampleRate);
                    bakeState = 1;
                }
                catch (System.Exception)
                {
                    bakeState = 2;
                }
            });
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
            if (!enabled || !cockpitView || !clipsReady && !TryCreateClip()) return;
            if (cooldown > 0f || masterVolume <= 0.01f) return;
            float volume = ImmersionMath.CreakVolume(jerk);
            if (volume <= 0f) return;
            creakSource.pitch = 0.7f + (float)rng.NextDouble() * 0.6f;
            creakSource.PlayOneShot(creakClip, volume * masterVolume);
            cooldown = 0.5f + (float)rng.NextDouble() * 1.2f;
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
            if (creakSource != null) creakSource.Stop();
        }

        private bool TryCreateClip()
        {
            if (bakeState != 1) return false;
            creakClip = AudioClip.Create("BoscaliAirframeCreak", creakBake.Length / 2, 2, SampleRate, false);
            creakClip.SetData(creakBake, 0);
            creakBake = null;
            clipsReady = true;
            return true;
        }

        /// <summary>Pure sample bake; safe on a worker thread (no Unity objects touched).</summary>
        private static float[] BakeCreak(float duration, int sampleRate)
        {
            int total = (int)(duration * sampleRate);
            float[] samples = new float[total * 2];
            var rng = new System.Random(5150);
            // Overlapping stick-slip groans: each is a downward-chirping resonant mode pair
            // with gritty friction noise, under a slow swell so it breathes rather than ticks.
            int slips = 7;
            for (int k = 0; k < slips; k++)
            {
                float start = (float)rng.NextDouble() * duration * 0.55f;
                float len = 0.25f + (float)rng.NextDouble() * 0.4f;
                float f0 = 160f + (float)rng.NextDouble() * 500f;
                float f1 = f0 * (0.45f + (float)rng.NextDouble() * 0.25f);
                float amp = 0.25f + (float)rng.NextDouble() * 0.5f;
                float pan = (float)rng.NextDouble();
                float left = Mathf.Sqrt(1f - pan), right = Mathf.Sqrt(pan);
                float reson = 0f, grit = 0f;
                int s0 = (int)(start * sampleRate), s1 = Mathf.Min(total, s0 + (int)(len * sampleRate));
                for (int s = s0; s < s1; s++)
                {
                    float t = (float)(s - s0) / sampleRate;
                    float frac = t / len;
                    float freq = f0 + (f1 - f0) * frac;
                    float drive = Mathf.Sin(2f * Mathf.PI * freq * t)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * freq * 2.02f * t);
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    grit += 0.25f * (noise - grit);
                    reson += 0.06f * (drive * 0.6f + grit * 0.5f - reson);
                    float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(frac)) * Mathf.Exp(-frac * 2.2f);
                    float val = reson * env * amp;
                    samples[s * 2] += val * left;
                    samples[s * 2 + 1] += val * right;
                }
            }
            float peak = 0.0001f;
            for (int i = 0; i < samples.Length; i++)
            {
                float a = Mathf.Abs(samples[i]);
                if (a > peak) peak = a;
            }
            float norm = 0.7f / peak;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Clamp(samples[i] * norm, -1f, 1f);
            return samples;
        }

        private void OnDestroy()
        {
            if (creakClip != null) Destroy(creakClip);
        }
    }
}
