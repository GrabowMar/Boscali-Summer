using UnityEngine;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    internal sealed class CockpitAudioFilter
    {
        private AudioListener boundListener;
        private Camera boundCamera;
        private AudioLowPassFilter filter;
        private bool created;
        private bool wrote;
        private bool priorEnabled;
        private float priorCutoff;
        private float writtenCutoff;
        private float nextSearch;
        private float currentCutoff = 22000f;
        public float CutoffFrequency => currentCutoff;
        public bool IsFilteringActive => wrote && filter != null && filter.enabled;

        public void Tick(Camera camera, float positive, float negative, bool cockpit, bool enabled, float dt)
        {
            if (camera != boundCamera || (filter != null && boundListener == null) ||
                (boundListener != null && !boundListener.isActiveAndEnabled)) Release();
            if (!enabled || !cockpit || camera == null || (positive <= 0.001f && negative <= 0.001f))
            {
                Restore();
                currentCutoff = 22000f;
                return;
            }
            float target = ImmersionMath.ExposureAudioCutoff(positive, negative);
            currentCutoff = Mathf.MoveTowards(currentCutoff, target, 12000f * dt);
            if (currentCutoff >= 21000f) { Restore(); return; }
            EnsureFilter(camera);
            if (filter == null) return;
            if (!wrote)
            {
                priorEnabled = filter.enabled;
                priorCutoff = filter.cutoffFrequency;
            }
            else
            {
                // Rebase each borrowed property separately. A foreign cutoff write must
                // not adopt our enabled=true write as the native enabled baseline.
                if (!filter.enabled) priorEnabled = false;
                if (Mathf.Abs(filter.cutoffFrequency - writtenCutoff) > 0.5f)
                    priorCutoff = filter.cutoffFrequency;
            }
            writtenCutoff = priorEnabled ? Mathf.Min(priorCutoff, currentCutoff) : currentCutoff;
            filter.cutoffFrequency = writtenCutoff;
            filter.enabled = true;
            wrote = true;
        }

        private void Restore()
        {
            if (wrote && filter != null)
            {
                if (Mathf.Abs(filter.cutoffFrequency - writtenCutoff) <= 0.5f)
                    filter.cutoffFrequency = priorCutoff;
                if (filter.enabled) filter.enabled = priorEnabled;
            }
            wrote = false;
        }

        public void Release()
        {
            Restore();
            if (created && filter != null) Object.Destroy(filter);
            filter = null;
            boundListener = null;
            boundCamera = null;
            created = false;
            currentCutoff = 22000f;
            nextSearch = 0f;
        }

        private void EnsureFilter(Camera camera)
        {
            if (filter != null && boundListener != null) return;
            if (Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + 1f;
            boundCamera = camera;
            boundListener = camera.GetComponent<AudioListener>() ?? camera.GetComponentInChildren<AudioListener>()
                ?? camera.GetComponentInParent<AudioListener>();
            if (boundListener == null) return;
            filter = boundListener.GetComponent<AudioLowPassFilter>();
            created = filter == null;
            if (created)
            {
                filter = boundListener.gameObject.AddComponent<AudioLowPassFilter>();
                filter.enabled = false;
                filter.cutoffFrequency = 22000f;
            }
        }
    }
}
