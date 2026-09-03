using System;
using System.Collections;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Delivers cockpit electronic disruption for aircraft inside the EMP shockwave radius.
    /// Causes avionics jitter, static warning audio, and HUD distortion.
    /// </summary>
    internal sealed class CockpitEmpDisruption : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private static AudioClip staticAudioClip;

        public static void TriggerForPlayer(Aircraft aircraft, float severity)
        {
            if (aircraft == null || GameManager.IsHeadless) return;

            var disruption = aircraft.gameObject.GetComponent<CockpitEmpDisruption>()
                          ?? aircraft.gameObject.AddComponent<CockpitEmpDisruption>();
            disruption.Disrupt(severity);
        }

        private float endTime;
        private AudioSource staticSource;

        private void Disrupt(float severity)
        {
            endTime = Time.time + Mathf.Clamp(severity * 3.5f, 1.5f, 5.0f);
            EnsureAudio();

            if (staticSource == null)
            {
                staticSource = gameObject.AddComponent<AudioSource>();
                staticSource.clip = staticAudioClip;
                staticSource.spatialBlend = 0f; // direct 2D in-headset audio
                staticSource.volume = 0.7f;
            }

            if (staticSource != null && !staticSource.isPlaying)
            {
                staticSource.Play();
            }

            StartCoroutine(DisruptionRoutine());
        }

        private IEnumerator DisruptionRoutine()
        {
            while (Time.time < endTime)
            {
                yield return null;
            }

            if (staticSource != null && staticSource.isPlaying)
            {
                staticSource.Stop();
            }
        }

        private static void EnsureAudio()
        {
            if (staticAudioClip != null) return;

            int length = (int)(SampleRate * 2.5f);
            float[] samples = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                // Cockpit radio static + intermittent 60Hz hum + spark pops
                float whiteNoise = (UnityEngine.Random.value * 2f - 1f) * 0.35f;
                float hum = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.25f;
                float pop = UnityEngine.Random.value > 0.985f ? (UnityEngine.Random.value * 2f - 1f) * 0.8f : 0f;
                samples[i] = Mathf.Clamp(whiteNoise + hum + pop, -1f, 1f);
            }

            staticAudioClip = AudioClip.Create("EmpCockpitStatic", length, 1, SampleRate, false);
            staticAudioClip.SetData(samples, 0);
        }
    }
}
