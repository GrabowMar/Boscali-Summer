using System.Collections.Generic;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// Recorded one-shot sounds embedded from Core/Fx/Sfx (mono 16-bit 22050 Hz WAV, cut by tools/Build-SoundClips.py).
    /// A clip is decoded the first time it is asked for and kept for the session; a missing clip is null and
    /// the caller stays silent. Sources and licences: Core/Fx/Sfx/LICENSES.txt.
    /// </summary>
    internal static class SfxClips
    {
        private const int SampleRate = 22050;
        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        public static AudioClip Get(string name)
        {
            // A destroyed clip compares equal to null without being a null reference: decode it again.
            if (clips.TryGetValue(name, out AudioClip clip) && (clip != null || ReferenceEquals(clip, null))) return clip;
            clip = null;
            float[] samples = PcmWav.Decode(EmbeddedResources.ReadAll(typeof(SfxClips).Assembly,
                "BoscaliSummer.Sfx." + name + ".wav", 1024 * 1024), SampleRate);
            if (samples != null && samples.Length > 0)
            {
                clip = AudioClip.Create("BoscaliSfx." + name, samples.Length, 1, SampleRate, false);
                clip.SetData(samples, 0);
            }
            clips[name] = clip;
            return clip;
        }
    }
}
