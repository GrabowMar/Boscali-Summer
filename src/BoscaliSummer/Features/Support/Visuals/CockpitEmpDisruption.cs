using System;
using System.Collections;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Delivers the full cinematic cockpit electronic meltdown for aircraft inside or near
    /// the EMP shockwave radius:
    /// 1. Electromagnetic screen pulse & cyan static wash via CameraStateManager blackout overlay.
    /// 2. HUD scramble, target marker corruption, and attitude ladder jitter.
    /// 3. Cockpit MFD screen flicker and reboot glitch.
    /// 4. Dashboard electrical spark flashes and camera electromagnetic jolt.
    /// 5. Procedurally synthesized static, spark pops, and 400Hz avionics failure squeal.
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

        public static void CheckLocalDisruption(Vector3 burstPoint, float radius)
        {
            if (GameManager.IsHeadless) return;
            if (GameManager.GetLocalPlayer<Player>(out Player localPlayer) && localPlayer != null && localPlayer.Aircraft != null)
            {
                float dist = Vector3.Distance(localPlayer.Aircraft.transform.position, burstPoint);
                if (dist <= radius)
                {
                    float severity = Mathf.Lerp(1.5f, 0.7f, dist / radius);
                    TriggerForPlayer(localPlayer.Aircraft, severity);
                }
                else if (dist <= radius * 1.5f)
                {
                    // Mild peripheral disruption
                    float severity = Mathf.Lerp(0.6f, 0.2f, (dist - radius) / (radius * 0.5f));
                    TriggerForPlayer(localPlayer.Aircraft, severity);
                }
            }
        }

        private float endTime;
        private float disruptionDuration;
        private float currentSeverity;
        private AudioSource staticSource;
        private Light cockpitSparkLight;
        private VirtualMFD virtualMfd;

        private void Disrupt(float severity)
        {
            currentSeverity = severity;
            disruptionDuration = Mathf.Clamp(severity * 3.8f, 1.8f, 6.5f);
            endTime = Time.time + disruptionDuration;
            virtualMfd = GetComponentInChildren<VirtualMFD>();

            EnsureAudio();

            // Cockpit dashboard spark light
            if (cockpitSparkLight == null)
            {
                var sparkObj = new GameObject("CockpitSparkLight");
                sparkObj.transform.SetParent(transform, false);
                sparkObj.transform.localPosition = new Vector3(0f, 0.45f, 0.7f); // In front of pilot
                cockpitSparkLight = sparkObj.AddComponent<Light>();
                cockpitSparkLight.type = LightType.Point;
                cockpitSparkLight.color = new Color(0.6f, 0.85f, 1.0f);
                cockpitSparkLight.range = 3.5f;
                cockpitSparkLight.intensity = 0f;
                cockpitSparkLight.shadows = LightShadows.None;
            }

            // Headset static & avionics failure audio
            if (staticSource == null)
            {
                staticSource = gameObject.AddComponent<AudioSource>();
                staticSource.clip = staticAudioClip;
                staticSource.spatialBlend = 0f; // Direct 2D in-headset audio
                staticSource.volume = 0.85f;
                staticSource.loop = true;
            }

            if (staticSource != null && !staticSource.isPlaying)
            {
                staticSource.Play();
            }

            // Initial camera jolt
            var csm = SceneSingleton<CameraStateManager>.i;
            if (csm != null)
            {
                csm.ShakeCamera(0.9f * severity, 1.8f * severity);
            }

            StartCoroutine(DisruptionRoutine());
        }

        private IEnumerator DisruptionRoutine()
        {
            float mfdGlitchTimer = 0.1f;
            var csm = SceneSingleton<CameraStateManager>.i;
            Image blackout = csm != null ? csm.GetBlackoutImage() : null;
            Color originalBlackout = blackout != null ? blackout.color : Color.clear;

            try
            {
                while (Time.time < endTime)
                {
                    float remaining = endTime - Time.time;
                    float progress = 1f - (remaining / disruptionDuration);
                    float decay = Mathf.Clamp01(remaining / disruptionDuration);

                    // 1. Screen flash & static pulse on native blackout overlay
                    if (blackout != null)
                    {
                        float flicker = 0.5f + Mathf.Sin(Time.time * 50f) * 0.5f;
                        float pulse = Mathf.Pow(decay, 1.8f) * 0.38f * flicker;
                        blackout.color = new Color(0.25f, 0.65f, 1f, pulse);
                    }

                    // 2. Scramble CombatHUD markers & radar
                    var combatHud = SceneSingleton<CombatHUD>.i;
                    if (combatHud != null)
                    {
                        combatHud.jamAccumulation = Mathf.Max(combatHud.jamAccumulation, 2.8f * currentSeverity * decay);
                    }

                    // 3. Glitch VirtualMFD screens during peak disruption
                    if (progress < 0.65f && virtualMfd != null)
                    {
                        mfdGlitchTimer -= Time.deltaTime;
                        if (mfdGlitchTimer <= 0f)
                        {
                            mfdGlitchTimer = UnityEngine.Random.Range(0.12f, 0.35f);
                            if (UnityEngine.Random.value > 0.4f)
                            {
                                virtualMfd.HideAllLeftScreens();
                                virtualMfd.HideAllRightScreens();
                            }
                        }
                    }

                    // 4. Cockpit spark light flickering
                    if (cockpitSparkLight != null)
                    {
                        if (progress < 0.7f && UnityEngine.Random.value > 0.65f)
                        {
                            cockpitSparkLight.enabled = true;
                            cockpitSparkLight.intensity = UnityEngine.Random.Range(1.8f, 5.0f) * decay;
                        }
                        else
                        {
                            cockpitSparkLight.enabled = false;
                        }
                    }

                    // 5. Subtle camera electromagnetic jitter
                    if (csm != null && progress < 0.5f)
                    {
                        csm.ShakeCamera(0.35f * decay, 0.75f * decay);
                    }

                    yield return null;
                }
            }
            finally
            {
                if (blackout != null)
                {
                    blackout.color = originalBlackout;
                }
            }

            // Cleanup
            if (staticSource != null && staticSource.isPlaying)
            {
                staticSource.Stop();
            }

            if (cockpitSparkLight != null)
            {
                cockpitSparkLight.enabled = false;
            }
        }

        private static void EnsureAudio()
        {
            if (staticAudioClip != null) return;

            int length = (int)(SampleRate * 3.5f);
            float[] samples = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;

                // Cockpit radio static noise
                float whiteNoise = (UnityEngine.Random.value * 2f - 1f) * 0.42f;

                // 400Hz aircraft avionics bus failure squeal (collapsing frequency)
                float squealFreq = Mathf.Lerp(420f, 60f, Mathf.Clamp01(t / 2.8f));
                float squeal = Mathf.Sin(2f * Mathf.PI * squealFreq * t) * 0.25f;

                // 60Hz power transformer hum
                float hum = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.2f;

                // Electrical circuit breaker spark pops
                float pop = UnityEngine.Random.value > 0.982f
                    ? (UnityEngine.Random.value * 2f - 1f) * 0.85f
                    : 0f;

                samples[i] = Mathf.Clamp(whiteNoise + squeal + hum + pop, -1f, 1f);
            }

            staticAudioClip = AudioClip.Create("EmpCockpitStatic", length, 1, SampleRate, false);
            staticAudioClip.SetData(samples, 0);
        }

        private void OnDestroy()
        {
            if (cockpitSparkLight != null) Destroy(cockpitSparkLight.gameObject);
        }
    }
}
