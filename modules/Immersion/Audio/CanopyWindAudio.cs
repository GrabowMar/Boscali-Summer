using System.Threading;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Math;
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
        private const int LoopSeconds = 8;
        private const string VoiceId = "cockpit-wind";

        private AudioSource windSource;
        private GameObject windRoot;
        private AudioClip windClip;
        private AudioLowPassFilter windFilter;
        private float[] bakeBuffer;
        private volatile int bakeState; // 0 pending, 1 ready, 2 failed
        private volatile bool released;
        private bool clipsReady;
        private bool routed;
        private bool isLoopActive;
        internal bool IsPlaying => windSource != null && windSource.isPlaying;
        internal float Volume => windSource != null ? windSource.volume : 0f;

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

            bakeState = 0;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    float[] samples = BuildWindNoise(LoopSeconds, SampleRate);
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

        public void Tick(float airspeedMps, float gustMps, bool cockpitView, bool enabled, float dt,
            float moisture = 0f, float altitudeM = 0f)
        {
            if (!enabled || !cockpitView || airspeedMps < 40f ||
                (moisture > 0.08f && FxBus.Scales.Voices < 0.75f))
            {
                Silence();
                return;
            }

            if (!clipsReady && !TryCreateClip()) return;
            if (!routed) return;

            var (targetVolume, targetPitch) = CanopyWindMath.Target(airspeedMps, gustMps,
                Isa.Density(altitudeM) / Isa.SeaLevelDensity, moisture, Time.unscaledTime);
            dt = Mathf.Clamp(dt, 0f, 0.05f);
            windFilter.cutoffFrequency = Mathf.MoveTowards(windFilter.cutoffFrequency,
                1800f + targetPitch * 1200f, dt * 3000f);

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
            }
        }

        public void Release()
        {
            released = true;
            Silence();
            if (windRoot != null) { Destroy(windRoot); windRoot = null; }
            windSource = null;
            if (windClip != null) { Destroy(windClip); windClip = null; }
            windFilter = null;
            bakeBuffer = null;
            clipsReady = false;
        }

        private bool TryCreateClip()
        {
            if (bakeState != 1 || bakeBuffer == null) return false;
            windClip = AudioClip.Create("BoscaliCanopyWind", bakeBuffer.Length / 2, 2, SampleRate, false);
            windClip.SetData(bakeBuffer, 0);
            bakeBuffer = null;
            windSource.clip = windClip;
            clipsReady = true;
            return true;
        }

        /// <summary>
        /// Synthesizes a smooth, seamless pink/turbulent noise loop on a background thread.
        /// </summary>
        internal static float[] BuildWindNoise(float seconds, int sampleRate)
        {
            int total = (int)(seconds * sampleRate);
            int seam = sampleRate / 8;
            float[] data = new float[total * 2];
            var rng = new System.Random(0x3B47);

            // Pink noise filter state (Paul Kellet filter)
            float b0 = 0f, b1 = 0f, b2 = 0f, c0 = 0f, c1 = 0f, c2 = 0f;
            for (int i = 0; i < total; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float side = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.2f;
                float left = white + side, right = white - side;
                b0 = 0.99886f * b0 + left * 0.0555179f;
                b1 = 0.99332f * b1 + left * 0.0750759f;
                b2 = 0.96900f * b2 + left * 0.1538520f;
                c0 = 0.99886f * c0 + right * 0.0555179f;
                c1 = 0.99332f * c1 + right * 0.0750759f;
                c2 = 0.96900f * c2 + right * 0.1538520f;
                data[i * 2] = (b0 + b1 + b2 + left * 0.5362f) * 0.12f;
                data[i * 2 + 1] = (c0 + c1 + c2 + right * 0.5362f) * 0.12f;
            }

            // Fold the tail into the head, then trim the tail. The loop crosses adjacent
            // samples rather than replaying the same 20 ms seam at every short repetition.
            for (int i = 0; i < seam; i++)
            {
                float t = i / (float)seam;
                int head = i * 2, tail = (total - seam + i) * 2;
                data[head] = data[tail] * (1f - t) + data[head] * t;
                data[head + 1] = data[tail + 1] * (1f - t) + data[head + 1] * t;
            }
            System.Array.Resize(ref data, (total - seam) * 2);
            double mean = 0;
            foreach (float sample in data) mean += sample;
            mean /= data.Length;
            for (int i = 0; i < data.Length; i++) data[i] -= (float)mean;
            double energy = 0;
            float peak = 0f;
            foreach (float sample in data) { energy += sample * sample; peak = System.Math.Max(peak, System.Math.Abs(sample)); }
            if (peak > 0f)
            {
                float gain = System.Math.Min(0.11f / (float)System.Math.Sqrt(energy / data.Length), 0.65f / peak);
                for (int i = 0; i < data.Length; i++) data[i] *= gain;
            }
            return data;
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}
