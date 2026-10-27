using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Presentation
{
    /// <summary>
    /// A restrained dispatch chime (Core/Fx/Sfx master-caution), flat and client-local,
    /// rate-limited so rapid rotations cannot stack a loud alert.
    /// </summary>
    internal sealed class EventAlertTone
    {
        private const float Interval = 8f;

        private readonly AudioSource source;
        private float nextAllowed;

        public EventAlertTone(Transform parent)
        {
            var host = new GameObject("BoscaliEventAlertTone");
            host.transform.SetParent(parent, false);
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.3f;
        }

        public void Play()
        {
            AudioClip clip = SfxClips.Get("master-caution");
            if (source == null || clip == null || Time.unscaledTime < nextAllowed) return;
            nextAllowed = Time.unscaledTime + Interval;
            source.PlayOneShot(clip);
        }
    }
}
