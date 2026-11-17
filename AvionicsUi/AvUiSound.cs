using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// What a control says when used. Navigate = a tab or selector step; Press = a plain key; Engage / Release = a switch latching on / off;
    /// Confirm / Caution = an accepted / refused action; Open = a console key bringing its screen up. Hover is silent.
    /// </summary>
    public enum AvUiCue { Hover, Navigate, Press, Engage, Release, Confirm, Caution, Open }

    /// <summary>
    /// Quiet, scene-owned, two-dimensional recorded feedback for cockpit controls. The host supplies and owns clips;
    /// a missing clip is silence. No runtime synthesizer or per-play clip allocation.
    /// </summary>
    public static class AvUiSound
    {
        private const int Cues = 8;
        /// <summary>A cue does not repeat inside this, and a generic click cue yields to any cue played inside it (a handler's own Confirm wins).</summary>
        private const float Gate = .06f;
        private static readonly AudioClip[] clips = new AudioClip[Cues];
        // Hover, Navigate, Press, Engage, Release, Confirm, Caution, Open. Each cue has its own clip at its own pitch: nothing retunes the
        // shared source, so a ringing chime is never bent by the next click.
        private static readonly string[] recorded = { null, "ui-tab", "ui-click-0", "ui-engage", "ui-release", "ui-confirm", "ui-caution", "ui-open" };
        // The clips share one peak level; tonal cues carry more energy than clicks, so they sit lower.
        private static readonly float[] gains = { 0f, .30f, .34f, .18f, .16f, .14f, .16f, .15f };
        private static readonly float[] last = new float[Cues];
        private static AudioSource source;
        private static float lastAny = -1f;
        private static System.Func<string, AudioClip> resolver;

        /// <summary>The host's recorded clips by stable name; null or a missing clip is silent. Replacing the provider drops borrowed references.</summary>
        public static System.Func<string, AudioClip> Recorded
        {
            get => resolver;
            set { if (!ReferenceEquals(resolver, value)) { Reset(); resolver = value; } }
        }

        /// <summary>Optional host routing (for example the game's interface mixer); retried on play if the mixer starts late.</summary>
        public static System.Action<AudioSource> Route { get; set; }

        /// <summary>Independent UI level; zero mutes every cue.</summary>
        public static float Volume { get; set; } = .7f;

        public static void Play(AvUiCue cue)
        {
            int i = (int)cue;
            if (Volume <= 0f || i <= 0 || i >= Cues) return;
            AudioClip clip = clips[i];
            if (clip == null) clips[i] = clip = Recorded?.Invoke(recorded[i]);
            if (clip == null || !Ensure()) return;
            float now = Time.unscaledTime;
            if (now - last[i] < Gate) return;
            bool generic = cue == AvUiCue.Press || cue == AvUiCue.Navigate || cue == AvUiCue.Engage || cue == AvUiCue.Release;
            if (generic && now - lastAny < Gate) return;
            last[i] = lastAny = now;
            Route?.Invoke(source);
            source.PlayOneShot(clip, gains[i] * Mathf.Clamp01(Volume));
        }

        public static void Reset()
        {
            if (source != null) UnityEngine.Object.Destroy(source.gameObject);
            source = null;
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = null;
                last[i] = -1f;
            }
            lastAny = -1f;
        }

        private static bool Ensure()
        {
            if (source != null) return true;
            var host = new GameObject("NOAvionics.UiSound", typeof(AudioSource));
            source = host.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.priority = 40;
            return source != null;
        }
    }
}
