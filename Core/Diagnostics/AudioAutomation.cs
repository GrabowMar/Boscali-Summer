#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>Explicit nomodkit acceptance of the shipped clip bank, cache and native mixer. Never runs by itself.</summary>
    public static class AudioAutomation
    {
        private const string LoopId = "audio-acceptance-loop";
        private static AudioSource source;
        private static string playing;
        private static bool loopActive;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "requires a nomodkit scenario" };
            string action = args != null && args.TryGetValue("action", out object raw) ? raw as string : "state";
            int before = SfxClips.DecodeCount, cachedBefore = SfxClips.CachedCount, ready = 0;
            var missing = new List<string>();
            bool accepted = false;
            if (action == "load")
                foreach (string id in SfxClips.KnownIds)
                {
                    if (SfxClips.Get(id) != null) ready++; else missing.Add(id);
                }
            else if (action == "unknown")
            {
                SfxClips.Get("../invalid"); SfxClips.Get("not-a-shipped-audio-key"); SfxClips.Get(null);
            }
            else if (action == "stop" || action == "mute")
            {
                Stop();
            }
            else if (action == "volume")
            {
                float gain = Volume(args);
                if (gain <= 0f) Stop();
                else if (source != null) source.volume = gain;
                accepted = source != null;
            }
            else if (action == "play" || action == "loop")
            {
                bool loop = action == "loop";
                string id = args != null && args.TryGetValue("id", out object requested) ? requested as string : loop ? "cockpit-wind" : "ui-confirm";
                float gain = Volume(args);
                Stop();
                AudioClip clip = SfxClips.Get(id);
                var mixer = SoundManager.i != null ? (loop ? SoundManager.i.EffectsMixer : SoundManager.i.InterfaceMixer) : null;
                if (clip != null && mixer != null && gain > 0f &&
                    (loop ? FxVoiceBus.TryStartLoop(LoopId) : FxVoiceBus.TryOneShot(clip.length)))
                {
                    if (source == null)
                    {
                        var host = new GameObject("BoscaliAudioAcceptance") { hideFlags = HideFlags.HideAndDontSave };
                        source = host.AddComponent<AudioSource>();
                        source.playOnAwake = false; source.spatialBlend = 0f; source.priority = 40;
                    }
                    source.outputAudioMixerGroup = mixer;
                    source.clip = clip; source.volume = gain; source.pitch = 1f; source.loop = loop;
                    loopActive = loop;
                    source.Play(); playing = id; accepted = true;
                }
            }
            var state = new Dictionary<string, object>
            {
                ["ok"] = true, ["action"] = action, ["ready"] = ready, ["missing"] = missing, ["missingCount"] = missing.Count,
                ["knownReady"] = action == "load" && missing.Count == 0 && ready == SfxClips.KnownCount,
                ["newDecodes"] = SfxClips.DecodeCount - before, ["newCacheEntries"] = SfxClips.CachedCount - cachedBefore,
                ["accepted"] = accepted, ["playing"] = source != null && source.isPlaying,
                ["id"] = playing ?? "", ["seconds"] = source != null && source.clip != null ? source.clip.length : 0f,
                ["gain"] = source != null ? source.volume : 0f, ["loop"] = loopActive,
                ["interfaceMixer"] = source != null && SoundManager.i != null && SoundManager.i.InterfaceMixer != null && source.outputAudioMixerGroup == SoundManager.i.InterfaceMixer,
                ["effectsMixer"] = source != null && SoundManager.i != null && SoundManager.i.EffectsMixer != null && source.outputAudioMixerGroup == SoundManager.i.EffectsMixer,
            };
            SfxClips.Describe(state);
            FxVoiceBus.Describe(state);
            return state;
        }

        private static float Volume(Dictionary<string, object> args)
        {
            if (args != null && args.TryGetValue("mute", out object muted) && muted is bool value && value) return 0f;
            if (args == null || !args.TryGetValue("volume", out object raw)) return .6f;
            try
            {
                float gain = Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture);
                return float.IsNaN(gain) || float.IsInfinity(gain) ? 0f : Mathf.Clamp01(gain);
            }
            catch (Exception) { return 0f; }
        }

        private static void Stop()
        {
            if (source != null) { source.Stop(); source.volume = 0f; }
            if (loopActive) FxVoiceBus.EndLoop(LoopId);
            loopActive = false;
        }
    }
}
#endif
