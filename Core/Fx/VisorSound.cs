using System;
using System.Collections.Generic;
using NOAvionics;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// The HMD-9 visor as one instrument (2026-11-16): every visor sound goes through here. Clips are decoded once (<see cref="SfxClips"/>) and played 2D from a small pool of sources (no allocation per play) on the game's
    /// interface mixer like the cockpit's own warnings. Level = the option <c>TacBoard.VisorSound</c> (0..1, 0 = silent) times the pilot's UI volume (<see cref="AvUiSound.Volume"/>; 0 mutes). Variations round-robin without
    /// repeating, each play at a pitch within 2 % of its own. <see cref="VisorSoundGate"/> rate-limits and DUCKS: chatter is skipped and cues lowered while a real alarm or voice is sounding, which is the centre WARNING banner
    /// being up (<see cref="Banner"/>, set by the HUD every frame; its alerts are exactly what the missile alarm, pull-up and stall sounds play for) or any registered voice probe (<see cref="AddVoiceProbe"/>:
    /// the cockpit's spoken warnings, the radio chatter voice).
    /// </summary>
    internal static class VisorSound
    {
        private const int Pool = 6;
        private static readonly VisorSoundGate gate = new VisorSoundGate();
        private static readonly AudioClip[][] clips = new AudioClip[Enum.GetValues(typeof(VisorClip)).Length][];
        private static readonly int[] lastVariant = new int[clips.Length];
        private static readonly List<Func<bool>> voiceProbes = new List<Func<bool>>();
        private static AudioSource[] sources;
        private static int next;
        private static uint serial = 12345u;

        /// <summary>The option TacBoard.VisorSound, 0..1; 0 is off.</summary>
        public static float Level { get; set; } = 0.6f;
        /// <summary>A warning banner is up on the centre HUD (a missile, pull up, fire, engine out, lock): the visor's chatter keeps quiet under the real alarm.</summary>
        public static bool Banner { get; set; }
        /// <summary>The visor sound is on: the option is above zero and the pilot's UI volume is not muted.</summary>
        public static bool Enabled => Level > 0.001f && AvUiSound.Volume > 0.001f;

        /// <summary>A source of "a voice or alarm is sounding right now" (an AudioSource.isPlaying); the duck holds while any returns true.</summary>
        public static void AddVoiceProbe(Func<bool> probe) { if (probe != null && !voiceProbes.Contains(probe)) voiceProbes.Add(probe); }
        public static void RemoveVoiceProbe(Func<bool> probe) => voiceProbes.Remove(probe);

        /// <summary>True while a real alarm or voice is sounding (the duck).</summary>
        public static bool Busy()
        {
            if (Banner) return true;
            for (int i = 0; i < voiceProbes.Count; i++)
            {
                try { if (voiceProbes[i]()) return true; }
                catch (Exception) { /* a dead source never ducks the visor */ }
            }
            return false;
        }

        /// <summary>Play <paramref name="clip"/> now, if it may sound. A missing device or clip is silence, never an error.</summary>
        public static void Play(VisorClip clip)
        {
            if (!Enabled) return;
            float now = Time.unscaledTime;
            bool busy = Busy();
            if (!gate.TryPass(clip, now, busy, out float duck))
            {
#if !PUBLIC_RELEASE
                Log(now, clip, -1, 0f, busy ? (Banner ? "skip (limit or duck: banner)" : "skip (limit or duck: voice)") : "skip (limit)");
#endif
                return;
            }
            serial = serial * 1664525u + 1013904223u;
            int count = VisorSoundRules.Variants(clip);
            int v = VisorSoundRules.NextVariant(lastVariant[(int)clip] - 1, count, serial);
            lastVariant[(int)clip] = v + 1;
            float gain = Mathf.Clamp01(Level) * Mathf.Clamp01(AvUiSound.Volume / 0.7f) * duck;
#if !PUBLIC_RELEASE
            Log(now, clip, v, gain, duck < 1f ? (Banner ? "play (ducked: banner)" : "play (ducked: voice)") : "play");
#endif
            try { Emit(clip, v, gain, VisorSoundRules.Pitch(serial >> 3)); }
            catch (Exception) { /* a missing audio device must never touch the HUD */ }
        }

        /// <summary>The radial menu's cues in the visor's voice (open, detent, select, back, deny); with the visor sound off the old console clips play as before.</summary>
        public static void PlayMenu(AvUiCue cue)
        {
            if (cue == AvUiCue.Hover) return;
            if (!Enabled) { AvUiSound.Play(cue); return; }
            switch (cue)
            {
                case AvUiCue.Engage: case AvUiCue.Open: Play(VisorClip.MenuOpen); break;
                case AvUiCue.Navigate: Play(VisorClip.MenuMove); break;
                case AvUiCue.Confirm: Play(VisorClip.MenuSelect); break;
                case AvUiCue.Release: Play(VisorClip.MenuBack); break;
                case AvUiCue.Caution: Play(VisorClip.MenuDeny); break;
                default: AvUiSound.Play(cue); break;
            }
        }

        private static void Emit(VisorClip clip, int variant, float gain, float pitch)
        {
            if (Application.isBatchMode) return;
            AudioClip c = Clip(clip, variant);
            if (c == null || !Ensure()) return;
            AudioSource s = null;      // a free source first (a long power-up is never cut by a tick), else the oldest
            for (int i = 0; i < Pool && s == null; i++) { AudioSource q = sources[(next + i) % Pool]; if (!q.isPlaying) s = q; }
            if (s == null) s = sources[next];
            next = (next + 1) % Pool;
            if (s.outputAudioMixerGroup == null && SoundManager.i != null) s.outputAudioMixerGroup = SoundManager.i.InterfaceMixer;
            s.Stop();
            s.pitch = pitch;
            s.volume = gain;
            s.clip = c;
            s.Play();
        }

        private static AudioClip Clip(VisorClip clip, int variant)
        {
            AudioClip[] set = clips[(int)clip];
            if (set == null)
            {
                int n = VisorSoundRules.Variants(clip);
                set = new AudioClip[n];
                string name = VisorSoundRules.Name(clip);
                for (int i = 0; i < n; i++) set[i] = SfxClips.Get(name + "-" + i);
                clips[(int)clip] = set;
            }
            return set[variant];
        }

        private static bool Ensure()
        {
            if (sources != null && sources[0] != null) return true;
            var go = new GameObject("BoscaliVisorSound") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            sources = new AudioSource[Pool];
            for (int i = 0; i < Pool; i++)
            {
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.bypassEffects = true;      // heard in the helmet, never in the cockpit's reverb
                s.priority = 40;
                sources[i] = s;
            }
            return true;
        }

        /// <summary>Forget every limit (a scene change, the sim's reset).</summary>
        public static void Reset() => gate.Reset();

#if !PUBLIC_RELEASE
        /// <summary>The dev-only record of what the visor was asked to play and what happened to it (the sim reads it; the same lines go to the BepInEx log).</summary>
        internal static readonly List<string> Journal = new List<string>();

        private static void Log(float now, VisorClip clip, int variant, float gain, string what)
        {
            string line = "[VisorSound] t=" + now.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " " + VisorSoundRules.Name(clip) +
                (variant >= 0 ? "-" + variant : "") + " g=" + gain.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + what;
            if (Journal.Count < 400) Journal.Add(line);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) Plugin.Logger?.LogInfo(line);
        }
#endif
    }
}
