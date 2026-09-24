using System.Threading;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Features.UrbanCombat.Audio
{
    /// <summary>
    /// One procedural air-raid siren voice (civil-defense wailer), baked once on a worker
    /// thread, no shipped assets. The voice is a 3D source the service parks over the
    /// nearest city so distance and direction follow the camera like every other
    /// positional sound. Ceilings: 1 source, 1 clip, 1 bake.
    /// </summary>
    internal sealed class UrbanAmbienceAudio : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private const float SirenSeconds = 12.0f;

        private GameObject voiceRoot;
        private AudioSource sirenSource;
        private AudioClip sirenClip;
        private float sirenLevel;

        private float[] sirenBake;
        private int sirenFrames;
        private volatile int bakeState;
        private bool clipsReady;

        public void Initialize()
        {
            voiceRoot = new GameObject("BoscaliUrbanSiren");
            voiceRoot.transform.SetParent(transform, false);
            sirenSource = voiceRoot.AddComponent<AudioSource>();
            sirenSource.loop = true;
            sirenSource.playOnAwake = false;
            sirenSource.spatialBlend = 1f;
            sirenSource.minDistance = UrbanAmbienceMath.SirenInnerMeters;
            sirenSource.maxDistance = UrbanAmbienceMath.SirenOuterMeters;
            sirenSource.rolloffMode = AudioRolloffMode.Logarithmic;
            sirenSource.dopplerLevel = 0f;
            bakeState = 0;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    sirenBake = BakeSirenWail(SirenSeconds, SampleRate, out sirenFrames);
                    bakeState = 1;
                }
                catch (System.Exception)
                {
                    bakeState = 2;
                }
            });
        }

        public void SetOutputGroup(AudioMixerGroup group)
        {
            if (sirenSource != null) sirenSource.outputAudioMixerGroup = group;
        }

        public bool IsRouted => sirenSource != null && sirenSource.outputAudioMixerGroup != null;
        public bool ClipsReady => clipsReady;

        public bool IsSilent => sirenSource == null || !sirenSource.isPlaying;

        public void UpdateAudio(float sirenTarget, Vector3 anchorPos, bool hasAnchor)
        {
            if (sirenSource == null || voiceRoot == null) return;
            if (!clipsReady && !TryCreateClips()) return;
            if (hasAnchor) voiceRoot.transform.position = anchorPos;
            float target = hasAnchor ? sirenTarget : 0f;
            sirenLevel = UrbanAmbienceMath.FadeToward(sirenLevel, target, Time.deltaTime);
            DriveLayer(sirenSource, ref sirenLevel, target);
        }

        public void Silence()
        {
            sirenLevel = 0f;
            if (sirenSource != null && sirenSource.isPlaying) sirenSource.Stop();
        }

        private bool TryCreateClips()
        {
            if (bakeState != 1) return false;
            sirenClip = AudioClip.Create("BoscaliUrbanSiren", sirenFrames, 1, SampleRate, false);
            sirenClip.SetData(sirenBake, 0);
            sirenBake = null;
            sirenSource.clip = sirenClip;
            clipsReady = true;
            return true;
        }

        private static void DriveLayer(AudioSource source, ref float level, float target)
        {
            source.volume = level;
            if (target > UrbanAmbienceMath.SilenceEpsilon && !source.isPlaying) source.Play();
            if (target <= UrbanAmbienceMath.SilenceEpsilon &&
                level <= UrbanAmbienceMath.SilenceEpsilon && source.isPlaying)
            {
                source.Stop();
                level = 0f;
            }
        }

        private static float[] BakeSirenWail(float duration, int rate, out int playableFrames)
        {
            int total = (int)(duration * rate);
            float[] samples = new float[total];
            double phase = 0.0;
            for (int i = 0; i < total; i++)
            {
                float t = (float)i / rate;
                double lfo = System.Math.Sin(2.0 * System.Math.PI * t / duration);
                double freq = 600.0 + 200.0 * lfo;
                phase += 2.0 * System.Math.PI * freq / rate;
                double s = System.Math.Sin(phase)
                    + 0.3 * System.Math.Sin(2.0 * phase)
                    + 0.1 * System.Math.Sin(3.0 * phase);
                samples[i] = (float)(s * 0.35);
            }
            for (int i = 0; i < samples.Length; i++)
                samples[i] = samples[i] < -1f ? -1f : (samples[i] > 1f ? 1f : samples[i]);
            playableFrames = UrbanAmbienceMath.ApplyLoopCrossfade(samples, 1, rate / 10);
            System.Array.Resize(ref samples, playableFrames);
            return samples;
        }

        private void OnDestroy()
        {
            if (sirenClip != null) Destroy(sirenClip);
            // The voice lives on a child of the shared runtime root; a torn-down module
            // must not leave it.
            if (voiceRoot != null) Destroy(voiceRoot);
            voiceRoot = null;
            sirenSource = null;
        }
    }
}
