using System;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// The sound of a transmission while there is no recorded voice: the game's own radio static as the key-up click, and a recorded squelch tail when the words are over
    /// (a new line re-times the tail, so a line that was cut gets none). Both go through the game's interface mixer, so the volume sliders hold. Fails closed: one
    /// exception and the radio is silent until the next mission.
    /// </summary>
    internal static class ChatterAudio
    {
        private const float MinimumGap = 0.35f;
        private static readonly string[] Tails = { "radio-tail-0", "radio-tail-1" };
        private static AudioSource tailSource;
        private static AudioClip chime;
        private static float lastClick = float.NegativeInfinity;
        private static bool unavailable;

        public static void Reset()
        {
            unavailable = false;
            lastClick = float.NegativeInfinity;
            if (tailSource != null) tailSource.Stop();
        }

        /// <summary>A line goes on air and holds the channel for <paramref name="seconds"/>.</summary>
        public static void Transmit(float seconds)
        {
            if (unavailable || Application.isBatchMode) return;
            try
            {
                SoundManager sound = SoundManager.i;
                if (sound == null) return;
                GameAssets assets = GameAssets.i;
                if (assets != null && assets.radioStatic != null && Time.unscaledTime - lastClick >= MinimumGap)
                {
                    SoundManager.PlayInterfaceOneShot(assets.radioStatic);
                    lastClick = Time.unscaledTime;
                }
                AudioClip tail = SfxClips.Get(Tails[UnityEngine.Random.Range(0, Tails.Length)]);
                if (tail == null) return;
                if (tailSource == null)
                {
                    var go = new GameObject("BoscaliChatterTail") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    tailSource = go.AddComponent<AudioSource>();
                    tailSource.spatialBlend = 0f;
                    tailSource.playOnAwake = false;
                    tailSource.volume = 0.45f;
                }
                tailSource.outputAudioMixerGroup = sound.InterfaceMixer;
                tailSource.Stop();
                tailSource.clip = tail;
                tailSource.PlayDelayed(Mathf.Clamp(seconds, 0.3f, 9f));
            }
            catch (Exception)
            {
                unavailable = true;
            }
        }

        /// <summary>A short rising two-tone: an order went out to the whole wing.</summary>
        public static void Chime()
        {
            if (unavailable || Application.isBatchMode) return;
            try
            {
                if (SoundManager.i == null) return;
                if (chime == null)
                {
                    const int rate = 22050;
                    var samples = new float[(int)(rate * 0.14f)];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        float t = i / (float)rate, k = i / (float)samples.Length;
                        samples[i] = Mathf.Sin(2f * Mathf.PI * (k < 0.5f ? 880f : 1100f) * t) * 0.25f * Mathf.Sin(Mathf.PI * k);
                    }
                    chime = AudioClip.Create("BoscaliChatterChime", samples.Length, 1, rate, false);
                    chime.SetData(samples, 0);
                }
                SoundManager.PlayInterfaceOneShot(chime);
            }
            catch (Exception)
            {
                unavailable = true;
            }
        }
    }
}
