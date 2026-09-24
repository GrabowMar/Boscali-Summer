using System.Threading;
using BoscaliSummer.Features.Weather.Domain;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Features.Weather.Audio
{
    /// <summary>
    /// Synthesizes procedural rain and cockpit canopy patter audio in RAM. Requires 0 external
    /// .wav or .mp3 files. The sample bake runs on a worker thread; clips are created on the
    /// main thread once the buffers exist, so the first rain never hitches. Volume changes
    /// always ramp through a short fade envelope so mutes and view switches never pop.
    /// Both sources play through whatever mixer group the owner routes them to, so the
    /// game's master and effects sliders apply.
    /// </summary>
    internal sealed class ProceduralRainAudio : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const float HissSeconds = 17.0f;
        private const float PatterSeconds = 24.0f;

        private AudioSource hissSource;
        private AudioSource patterSource;
        private AudioLowPassFilter hissMuffle;
        private AudioClip hissClip;
        private AudioClip patterClip;
        private float hissLevel;
        private float patterLevel;

        // Worker-thread bake results; read on the main thread only after bakeState says so.
        private float[] hissBake;
        private float[] patterBake;
        private int hissFrames;
        private int patterFrames;
        private volatile int bakeState; // 0 pending, 1 ready, 2 failed
        private bool clipsReady;

        public void Initialize()
        {
            hissSource = gameObject.AddComponent<AudioSource>();
            hissSource.loop = true;
            hissSource.playOnAwake = false;
            hissSource.spatialBlend = 0f;
            hissMuffle = gameObject.AddComponent<AudioLowPassFilter>();

            // Patter lives on a child object: the hiss lowpass must not dull the droplet
            // transients, and filters process every source on their own GameObject.
            GameObject patterRoot = new GameObject("CanopyPatter");
            patterRoot.transform.SetParent(transform, false);
            patterSource = patterRoot.AddComponent<AudioSource>();
            patterSource.loop = true;
            patterSource.playOnAwake = false;
            patterSource.spatialBlend = 0f;

            bakeState = 0;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    hissBake = BakePinkHiss(HissSeconds, SampleRate, out hissFrames);
                    patterBake = BakeCanopyPatter(PatterSeconds, SampleRate, out patterFrames);
                    bakeState = 1;
                }
                catch (System.Exception)
                {
                    bakeState = 2;
                }
            });
        }

        /// <summary>Route both layers through a mixer group so the game's volume sliders apply.</summary>
        public void SetOutputGroup(AudioMixerGroup group)
        {
            if (hissSource != null) hissSource.outputAudioMixerGroup = group;
            if (patterSource != null) patterSource.outputAudioMixerGroup = group;
        }

        /// <summary>True once the sources play through a mixer group.</summary>
        public bool IsRouted => hissSource != null && hissSource.outputAudioMixerGroup != null;

        /// <summary>Name of the mixer group both layers play through, or empty.</summary>
        public string OutputGroupName =>
            hissSource != null && hissSource.outputAudioMixerGroup != null ? hissSource.outputAudioMixerGroup.name : "";

        /// <summary>True once the worker bake finished and both clips exist.</summary>
        public bool ClipsReady => clipsReady;

        /// <summary>True once both layers faded out and stopped; the owner may tear down.</summary>
        public bool IsSilent =>
            (hissSource == null || !hissSource.isPlaying) &&
            (patterSource == null || !patterSource.isPlaying);

        public void UpdateAudio(float indicatedAirspeedMs, float rainIntensity, bool isCockpitView, float masterVolume = 1.0f)
        {
            if (hissSource == null || patterSource == null) return;
            if (!clipsReady && !TryCreateClips()) return;

            float speedNorm = Mathf.Clamp01(indicatedAirspeedMs / 250f);
            bool audible = rainIntensity > 0.02f && masterVolume > 0.01f;
            float intensity = audible ? rainIntensity : 0f;

            // 1. Continuous aerodynamic rain hiss scales with airspeed and rain intensity
            float hissTarget = RainAudioMath.HissVolume(speedNorm, intensity, masterVolume, isCockpitView);
            hissLevel = RainAudioMath.FadeToward(hissLevel, hissTarget, Time.deltaTime);
            DriveLayer(hissSource, ref hissLevel, hissTarget, RainAudioMath.HissPitch(speedNorm));
            if (hissMuffle != null) hissMuffle.cutoffFrequency = Mathf.Lerp(hissMuffle.cutoffFrequency,
                RainAudioMath.MuffleCutoff(isCockpitView, speedNorm), 1f - Mathf.Exp(-Time.deltaTime * 8f));

            // 2. Canopy patter: loud at low/medium speeds, blending into dense rush at high speeds
            float patterTarget = RainAudioMath.PatterVolume(speedNorm, intensity, masterVolume, isCockpitView);
            patterLevel = RainAudioMath.FadeToward(patterLevel, patterTarget, Time.deltaTime);
            DriveLayer(patterSource, ref patterLevel, patterTarget, RainAudioMath.PatterPitch(speedNorm));
        }

        private bool TryCreateClips()
        {
            if (bakeState != 1) return false;
            hissClip = CreateClip("BoscaliRainHiss", hissBake, hissFrames, SampleRate);
            patterClip = CreateClip("BoscaliCanopyPatter", patterBake, patterFrames, SampleRate);
            hissBake = null;
            patterBake = null;
            hissSource.clip = hissClip;
            patterSource.clip = patterClip;
            clipsReady = true;
            return true;
        }

        private static void DriveLayer(AudioSource source, ref float level, float target, float pitch)
        {
            source.volume = level;
            source.pitch = pitch;
            if (target > RainAudioMath.SilenceEpsilon && !source.isPlaying)
            {
                source.Play();
            }

            if (target <= RainAudioMath.SilenceEpsilon &&
                level <= RainAudioMath.SilenceEpsilon && source.isPlaying)
            {
                source.Stop();
                level = 0f;
            }
        }

        private static AudioClip CreateClip(string name, float[] samples, int frames, int sampleRate)
        {
            var clip = AudioClip.Create(name, frames, 2, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Main-thread convenience used by the standalone fixture: bake and create at once.</summary>
        private static AudioClip SynthesizePinkHissClip(string name, float duration, int sampleRate)
        {
            float[] samples = BakePinkHiss(duration, sampleRate, out int frames);
            return CreateClip(name, samples, frames, sampleRate);
        }

        /// <summary>Main-thread convenience used by the standalone fixture: bake and create at once.</summary>
        private static AudioClip SynthesizeCanopyPatterClip(string name, float duration, int sampleRate)
        {
            float[] samples = BakeCanopyPatter(duration, sampleRate, out int frames);
            return CreateClip(name, samples, frames, sampleRate);
        }

        /// <summary>Pure sample bake; safe on a worker thread (no Unity objects touched).</summary>
        private static float[] BakePinkHiss(float duration, int sampleRate, out int playableFrames)
        {
            int totalSamples = (int)(duration * sampleRate);
            float[] samples = new float[totalSamples * 2]; // Stereo

            // Paul Kellet's 6-pole pinking filter
            float b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            var rng = new System.Random(1337);
            float gust = 0f, width = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                b0 = 0.99886f * b0 + white * 0.0555179f;
                b1 = 0.99332f * b1 + white * 0.0750759f;
                b2 = 0.96900f * b2 + white * 0.1538520f;
                b3 = 0.86650f * b3 + white * 0.3104856f;
                b4 = 0.55000f * b4 + white * 0.5329522f;
                b5 = -0.7616f * b5 - white * 0.0168980f;
                float pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362f;
                b6 = white * 0.115926f;

                // Aperiodic drift, with a quiet diffuse stereo bed. No audible pumping LFO.
                gust += 0.00004f * (white - gust);
                width += 0.08f * ((float)(rng.NextDouble() * 2 - 1) - width);
                float outSample = pink * 0.10f * (1f + Mathf.Clamp(gust * 12f, -0.12f, 0.12f));
                samples[i * 2] = outSample + width * 0.025f;
                samples[i * 2 + 1] = outSample - width * 0.025f;
            }

            // True 100 ms loop crossfade as a post-pass: the tail must exist before the
            // head can blend into it.
            playableFrames = RainAudioMath.ApplyLoopCrossfade(samples, 2, sampleRate / 10);
            System.Array.Resize(ref samples, playableFrames * 2);
            return samples;
        }

        /// <summary>Pure sample bake; safe on a worker thread (no Unity objects touched).</summary>
        private static float[] BakeCanopyPatter(float duration, int sampleRate, out int playableFrames)
        {
            int totalSamples = (int)(duration * sampleRate);
            float[] samples = new float[totalSamples * 2];
            var rng = new System.Random(777);

            // Mostly tiny ticks with occasional warm, larger drops. The longer independent
            // loops avoid a recognizable rhythm without any per-frame synthesis.
            int impactCount = (int)(duration * 38);

            for (int k = 0; k < impactCount; k++)
            {
                int startSample = rng.Next(0, totalSamples);
                float size = (float)rng.NextDouble();
                size *= size;
                float filter = Mathf.Lerp(0.20f, 0.065f, size);
                float low = 0f, soft = 0f, body = 0f; // Two softening poles, no ringing oscillators.
                float amp = Mathf.Lerp(0.12f, 0.48f, size);
                float decayRate = Mathf.Lerp(170f, 55f, size);
                float pan = Mathf.Lerp(0.12f, 0.88f, (float)rng.NextDouble());
                float left = Mathf.Sqrt(1f - pan), right = Mathf.Sqrt(pan);
                float attackSeconds = Mathf.Lerp(0.0025f, 0.006f, size);
                int len = (int)(sampleRate * 0.12f);
                for (int s = 0; s < len; s++)
                {
                    // Wrap around the loop point so boundary-crossing impacts stay continuous.
                    int frame = (startSample + s) % totalSamples;
                    float t = (float)s / sampleRate;
                    float attack = 1f - Mathf.Exp(-t / attackSeconds);
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    low += filter * (noise - low);
                    soft += filter * (low - soft);
                    body += 0.008f * (soft - body);
                    float tail = Mathf.Clamp01((len - 1 - s) / (sampleRate * 0.008f));
                    float val = attack * (soft - body) * Mathf.Exp(-decayRate * t) * amp * tail;
                    int idx = frame * 2;
                    samples[idx] += val * left;
                    samples[idx + 1] += val * right;
                }
            }

            // Normalization & clamping
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = Mathf.Clamp(samples[i], -1.0f, 1.0f);
            }

            playableFrames = RainAudioMath.ApplyLoopCrossfade(samples, 2, sampleRate / 10);
            System.Array.Resize(ref samples, playableFrames * 2);
            return samples;
        }

        private void OnDestroy()
        {
            if (hissClip != null) Destroy(hissClip);
            if (patterClip != null) Destroy(patterClip);
        }
    }
}
