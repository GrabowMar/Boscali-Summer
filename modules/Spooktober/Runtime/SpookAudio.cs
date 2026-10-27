using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// Spooktober's sounds (Assets/Sfx, ElevenLabs): one 2D source on the game's effects mixer that survives scene loads, played
    /// through <see cref="FxVoiceBus"/> like the mod's other one-shots. Local only: every peer hears its own menu and stings.
    /// A clip that is missing, or a game without its sound manager yet, stays silent.
    /// </summary>
    internal static class SpookAudio
    {
        public const string Stamp = "spook-stamp", File = "spook-file", Whisper = "spook-whisper",
            Wake = "spook-wake", Ace = "spook-ace", Gate = "spook-gate";

        private static AudioSource source;
        private static bool routed;

        /// <summary>Sounds started this session (automation readout).</summary>
        public static int Played { get; private set; }

        /// <summary>A sound of the menu itself: only while the MFD map is open and <paramref name="page"/> is on it. A console
        /// keeps ticking on other screens (and in a sim), and its file and whisper must not play over a dogfight.</summary>
        public static void Menu(string name, RectTransform page, float volume = .8f)
        {
            if (DynamicMap.mapMaximized && page != null && page.gameObject.activeInHierarchy) Play(name, volume);
        }

        public static void Play(string name, float volume = .8f)
        {
            try
            {
                if (source == null)
                {
                    var go = new GameObject("BoscaliSummer.SpookAudio");
                    Object.DontDestroyOnLoad(go);
                    source = go.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                    routed = false;
                }
                if (!routed)
                {
                    AudioMixerGroup group = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
                    if (group == null) return;
                    source.outputAudioMixerGroup = group;
                    routed = true;
                }
                AudioClip clip = SfxClips.Get(name);
                if (clip == null || !FxVoiceBus.TryOneShot(clip.length)) return;
                source.PlayOneShot(clip, volume);
                Played++;
            }
            catch (System.Exception e) { Plugin.Logger.LogWarning("[Spooktober] sound " + name + ": " + e.Message); }
        }
    }
}
