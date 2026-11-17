using System;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// A generated key-up click and squelch tail frame recorded transmissions (a new line re-times the tail, so a cut line gets none). Both follow the military volume
    /// through the game's interface mixer. Fails closed: one
    /// exception and the radio is silent until the next mission.
    /// </summary>
    internal static class ChatterAudio
    {
        private const float MinimumGap = 0.35f;
        private static readonly string[] Keys = { "radio-keyup-0", "radio-keyup-1", "radio-keyup-2", "radio-keyup-3" };
        private static readonly string[] Tails = { "radio-tail-0", "radio-tail-1", "radio-tail-2", "radio-tail-3" };
        private static int keyTurn, tailTurn;
        private static AudioSource keySource, tailSource;
        private static float volume = 1f;
        private static float lastClick = float.NegativeInfinity;
        private static bool unavailable;
        internal static bool Sounding => keySource != null && keySource.isPlaying || tailSource != null && tailSource.isPlaying;

        public static void Reset()
        {
            unavailable = false;
            lastClick = float.NegativeInfinity;
            keyTurn = tailTurn = 0;
            if (tailSource != null) tailSource.Stop();
            if (keySource != null) keySource.Stop();
        }

        public static void Volume(float value)
        {
            volume = Mathf.Clamp01(value);
            if (keySource != null) keySource.volume = volume * 0.35f;
            if (tailSource != null) tailSource.volume = volume * 0.3f;
        }

        private static void Prepare(SoundManager sound)
        {
            if (tailSource == null)
            {
                var go = new GameObject("BoscaliMilitaryRadioFx") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                keySource = go.AddComponent<AudioSource>();
                tailSource = go.AddComponent<AudioSource>();
                keySource.spatialBlend = tailSource.spatialBlend = 0f;
                keySource.playOnAwake = tailSource.playOnAwake = false;
                Volume(volume);
            }
            keySource.outputAudioMixerGroup = tailSource.outputAudioMixerGroup = sound.InterfaceMixer;
        }

        /// <summary>A line goes on air and holds the channel for <paramref name="seconds"/>.</summary>
        public static void Transmit(float seconds)
        {
            if (unavailable || Application.isBatchMode || !ChatterHost.Receiving || volume <= 0f) return;
            try
            {
                SoundManager sound = SoundManager.i;
                if (sound == null) return;
                Prepare(sound);
                AudioClip key = SfxClips.Get(Keys[keyTurn++ % Keys.Length]) ?? GameAssets.i?.radioStatic;
                if (key != null && Time.unscaledTime - lastClick >= MinimumGap)
                {
                    keySource.PlayOneShot(key);
                    lastClick = Time.unscaledTime;
                }
                AudioClip tail = SfxClips.Get(Tails[tailTurn++ % Tails.Length]);
                if (tail == null) return;
                tailSource.Stop();
                tailSource.clip = tail;
                tailSource.PlayDelayed(Mathf.Clamp(seconds, 0.3f, 30f));
            }
            catch (Exception)
            {
                unavailable = true;
            }
        }

        /// <summary>A short rising blip (Core/Fx/Sfx ui-confirm): an order went out to the whole wing.</summary>
        public static void Chime()
        {
            if (unavailable || Application.isBatchMode || !ChatterHost.Receiving || volume <= 0f) return;
            try
            {
                if (SoundManager.i == null) return;
                AudioClip chime = SfxClips.Get("ui-confirm");
                if (chime == null) return;
                Prepare(SoundManager.i);
                keySource.PlayOneShot(chime);
            }
            catch (Exception)
            {
                unavailable = true;
            }
        }
    }
}
