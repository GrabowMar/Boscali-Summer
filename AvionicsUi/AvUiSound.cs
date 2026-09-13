using UnityEngine;
using UnityEngine.EventSystems;

namespace NOAvionics.Ui
{
    /// <summary>
    /// The cockpit's own click: a short, procedurally synthesized bezel tick played through
    /// one 2D source.
    ///
    /// <para>No audio asset is shipped or referenced — the waveform is generated once per
    /// scene, so a panel keypress has an audible acknowledgment without the mod owning a
    /// clip, a mixer channel, or a download. Deliberately quiet and unspatialized: it is a
    /// control confirming contact, not an effect in the world.</para>
    /// </summary>
    public static class AvUiSound
    {
        private const int SampleRate = 22050;
        private const string HostName = "NOAvionics.UiClick";

        private static AudioSource source;
        private static AudioClip tick;

        /// <summary>A single short key click. Safe to call when audio is unavailable.</summary>
        public static void Tick(float volume = 0.4f)
        {
            if (!Ensure()) return;
            source.pitch = Random.Range(0.97f, 1.04f);
            source.PlayOneShot(tick, Mathf.Clamp01(volume));
        }

        /// <summary>Drop the scene-owned source and clip at mission end.</summary>
        public static void Reset()
        {
            if (source != null) Object.Destroy(source);
            if (tick != null) Object.Destroy(tick);
            source = null;
            tick = null;
        }

        private static bool Ensure()
        {
            if (source != null && tick != null) return true;

            // The source belonged to the previous scene; the clip is owned by this static
            // and is not scene-bound, so both are recreated together and the old clip released.
            if (source != null) Object.Destroy(source);
            if (tick != null) Object.Destroy(tick);
            source = null;
            tick = null;

            var host = new GameObject(HostName, typeof(AudioSource));
            source = host.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;

            tick = BuildTick();
            return tick != null;
        }

        /// <summary>
        /// A 30 ms damped click: a high tone for the contact and a quieter octave below it
        /// for body, gone before the next key can be pressed.
        /// </summary>
        private static AudioClip BuildTick()
        {
            const int length = SampleRate / 33;
            var clip = AudioClip.Create("NOAvionicsClick", length, 1, SampleRate, false);
            if (clip == null) return null;

            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t * 260f) - Mathf.Exp(-t * 4500f);
                float tone = Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.75f
                           + Mathf.Sin(2f * Mathf.PI * 1200f * t) * 0.25f;
                data[i] = tone * envelope * 0.6f;
            }

            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>
    /// The same key click for a control that is not an <see cref="AvButton"/> — a native
    /// bezel key the rail has adopted, for instance. Attach and destroy with the skin that
    /// borrowed the control, so vanilla buttons are never left wired to mod audio.
    /// </summary>
    public sealed class AvClickSound : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            AvUiSound.Tick(0.3f);
        }
    }
}
