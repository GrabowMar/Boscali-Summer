using UnityEngine;

namespace NOAvionics
{
    public enum AvUiCue { Hover, Navigate, Press, Engage, Release, Confirm, Caution }

    /// <summary>
    /// Quiet, scene-owned, two-dimensional feedback for cockpit controls: recorded panel keys and blips by name from
    /// <see cref="Recorded"/> (the host's clip library), with synthesized tones as the fallback when a clip is missing.
    /// </summary>
    public static class AvUiSound
    {
        private const int SampleRate = 44100;
        private static readonly AudioClip[] clips = new AudioClip[7];
        // Hover, Navigate, Press, Engage, Release, Confirm, Caution
        private static readonly string[] recorded = { null, "ui-click-1", "ui-click-0", "ui-click-0", "ui-click-1", "ui-confirm", "ui-caution" };
        private static readonly float[] gains = { 0f, .35f, .35f, .4f, .3f, .14f, .16f };
        private static readonly float[] pitches = { 1f, 1.08f, 1f, .9f, .94f, 1f, 1f };
        private static AudioSource source;
        private static float lastAction = -1f;

        /// <summary>The host's recorded clips by name (ui-click-0/1, ui-confirm, ui-caution); null keeps the synthesized tones.</summary>
        public static System.Func<string, AudioClip> Recorded { get; set; }

        /// <summary>Independent UI level; zero mutes every cue.</summary>
        public static float Volume { get; set; } = .7f;

        public static void Play(AvUiCue cue)
        {
            if (Volume <= 0f || cue == AvUiCue.Hover) return;
            if (!Ensure()) return;
            float now = Time.unscaledTime;
            if (now - lastAction < .025f) return;
            lastAction = now;
            source.pitch = pitches[(int)cue];
            source.PlayOneShot(clips[(int)cue], gains[(int)cue] * Mathf.Clamp01(Volume));
        }

        public static void Reset()
        {
            if (source != null) Object.Destroy(source.gameObject);
            source = null;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name.StartsWith("NOAvionics.")) Object.Destroy(clips[i]); // the recorded ones are shared
                clips[i] = null;
            }
            lastAction = -1f;
        }

        private static bool Ensure()
        {
            if (source != null && clips[0] != null) return true;
            Reset();
            var host = new GameObject("NOAvionics.UiSound", typeof(AudioSource));
            source = host.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            for (int i = 0; i < clips.Length; i++) clips[i] = (recorded[i] != null ? Recorded?.Invoke(recorded[i]) : null) ?? Build((AvUiCue)i);
            return clips[0] != null;
        }

        private static AudioClip Build(AvUiCue cue)
        {
            // Soft attack, low-mid fundamental, sparse harmonics and a little filtered
            // contact texture. Every tail reaches zero to avoid a cut when repeated.
            float seconds, frequency, glide, decay, texture;
            switch (cue)
            {
                case AvUiCue.Hover:    seconds = .035f; frequency = 300f; glide = 0f; decay = 55f; texture = .02f; break;
                case AvUiCue.Navigate: seconds = .060f; frequency = 350f; glide = -25f; decay = 48f; texture = .035f; break;
                case AvUiCue.Engage:   seconds = .080f; frequency = 390f; glide = -30f; decay = 38f; texture = .035f; break;
                case AvUiCue.Release:  seconds = .065f; frequency = 285f; glide = -20f; decay = 44f; texture = .03f; break;
                case AvUiCue.Confirm:  seconds = .085f; frequency = 420f; glide = -20f; decay = 36f; texture = .03f; break;
                case AvUiCue.Caution:  seconds = .090f; frequency = 250f; glide = -15f; decay = 34f; texture = .04f; break;
                default:               seconds = .060f; frequency = 320f; glide = -25f; decay = 46f; texture = .04f; break;
            }

            int length = Mathf.CeilToInt(seconds * SampleRate);
            var clip = AudioClip.Create("NOAvionics." + cue, length, 1, SampleRate, false);
            if (clip == null) return null;
            var data = new float[length];
            uint seed = 0x9e3779b9u + (uint)cue * 2654435761u;
            float filtered = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float attack = Mathf.Min(1f, t / .005f);
                float tail = Mathf.Min(1f, (seconds - t) / .014f);
                float envelope = attack * Mathf.Exp(-decay * t) * Mathf.Max(0f, tail);
                float phase = 2f * Mathf.PI * (frequency * t + .5f * glide * t * t);
                float body = Mathf.Sin(phase) + .07f * Mathf.Sin(phase * 2f);
                seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                float noise = ((seed & 0xffffu) / 32767.5f) - 1f;
                filtered += (noise - filtered) * .16f;
                float contact = filtered * texture * Mathf.Exp(-t * 75f);
                data[i] = Mathf.Clamp((body * .52f + contact) * envelope, -.85f, .85f);
            }
            clip.SetData(data, 0);
            return clip;
        }
    }
}
