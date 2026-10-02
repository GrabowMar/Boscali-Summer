using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Audio
{
    /// <summary>
    /// Dynamic cockpit audio filter simulating pilot auditory narrowing (tunnel hearing) under
    /// high G-forces and redout pressure.
    /// Attaches an AudioLowPassFilter to the active AudioListener.
    /// When outside the cockpit or during normal 1G flight, the filter is automatically disabled
    /// so the Unity audio engine incurs zero DSP processing overhead.
    /// </summary>
    internal sealed class CockpitAudioFilter
    {
        private const float MinCutoffHz = 600f;
        private const float MaxCutoffHz = 22000f;
        private const float ThresholdDisableHz = 21000f;
        private const float SlewRateHz = 16000f;

        private AudioListener boundListener;
        private AudioLowPassFilter filter;
        private float currentCutoff = MaxCutoffHz;

        /// <summary>Current filter cutoff frequency in Hz (diagnostics readout).</summary>
        public float CutoffFrequency => currentCutoff;

        /// <summary>Whether the DSP filter is currently actively processing audio.</summary>
        public bool IsFilteringActive => filter != null && filter.enabled;

        public void Tick(Camera mainCamera, Vector3 forceG, bool cockpitView, bool enabled, float dt)
        {
            if (!enabled || !cockpitView || mainCamera == null)
            {
                if (filter != null && filter.enabled)
                {
                    filter.enabled = false;
                    currentCutoff = MaxCutoffHz;
                }
                return;
            }

            EnsureFilter(mainCamera);
            if (filter == null) return;

            float targetCutoff = ImmersionMath.GAudioCutoffFrequency(forceG.y, cockpitView);
            targetCutoff = Mathf.Clamp(targetCutoff, MinCutoffHz, MaxCutoffHz);

            // Smooth transition to prevent audio popping
            currentCutoff = Mathf.MoveTowards(currentCutoff, targetCutoff, SlewRateHz * dt);

            if (currentCutoff >= ThresholdDisableHz)
            {
                if (filter.enabled)
                {
                    filter.enabled = false;
                }
            }
            else
            {
                if (!filter.enabled)
                {
                    filter.enabled = true;
                    filter.lowpassResonanceQ = 1.0f;
                }
                filter.cutoffFrequency = currentCutoff;
            }
        }

        public void Release()
        {
            if (filter != null)
            {
                filter.enabled = false;
                Object.Destroy(filter);
                filter = null;
            }
            boundListener = null;
            currentCutoff = MaxCutoffHz;
        }

        private void EnsureFilter(Camera camera)
        {
            if (filter != null && boundListener != null && boundListener.gameObject != null) return;

            AudioListener listener = camera.GetComponent<AudioListener>()
                ?? camera.GetComponentInChildren<AudioListener>()
                ?? Object.FindObjectOfType<AudioListener>();

            if (listener == null) return;

            boundListener = listener;
            filter = listener.GetComponent<AudioLowPassFilter>()
                ?? listener.gameObject.AddComponent<AudioLowPassFilter>();

            filter.enabled = false;
            filter.cutoffFrequency = MaxCutoffHz;
            filter.lowpassResonanceQ = 1.0f;
            currentCutoff = MaxCutoffHz;
        }
    }
}
