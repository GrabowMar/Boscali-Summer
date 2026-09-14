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
        private const float E1Window = 0.3f;
        private const float E2Window = 2.6f;
        private static AudioClip staticAudioClip;

        public static void TriggerForPlayer(Aircraft aircraft, float severity)
        {
            if (aircraft == null || GameManager.IsHeadless) return;

            if (!GameManager.GetLocalPlayer<Player>(out Player local) || local?.Aircraft != aircraft) return;

            var disruption = aircraft.gameObject.GetComponent<CockpitEmpDisruption>()
                          ?? aircraft.gameObject.AddComponent<CockpitEmpDisruption>();
            disruption.Disrupt(severity);
        }

        public static void CheckLocalDisruption(Vector3 burstPoint, float radius)
        {
            if (GameManager.IsHeadless) return;
            if (GameManager.GetLocalPlayer<Player>(out Player localPlayer) && localPlayer != null && localPlayer.Aircraft != null)
            {
                Vector3 delta = localPlayer.Aircraft.transform.position - burstPoint;
                float dist = new Vector2(delta.x, delta.z).magnitude;
                if (dist <= radius && localPlayer.Aircraft.transform.position.y <= Datum.LocalSeaY + 25000f)
                    TriggerForPlayer(localPlayer.Aircraft, Mathf.Lerp(1.5f, 0.7f, dist / radius));
            }
        }

        private float endTime;
        private float disruptionDuration;
        private float currentSeverity;
        private AudioSource staticSource;
        private AudioSource radioStaticSource;
        private Light cockpitSparkLight;
        private VirtualMFD virtualMfd;
        private Coroutine disruptionRoutine;

        private void Disrupt(float severity)
        {
            currentSeverity = severity;
            disruptionDuration = Mathf.Clamp(severity * 5.5f, 4f, 10f);
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

            // Radio static noise layer from game assets
            if (radioStaticSource == null && GameAssets.i?.radioStatic != null)
            {
                radioStaticSource = gameObject.AddComponent<AudioSource>();
                radioStaticSource.clip = GameAssets.i.radioStatic;
                radioStaticSource.spatialBlend = 0f;
                radioStaticSource.volume = 0.75f;
                radioStaticSource.loop = true;
            }

            if (radioStaticSource != null && !radioStaticSource.isPlaying)
            {
                radioStaticSource.Play();
            }

            // Initial camera jolt
            var csm = SceneSingleton<CameraStateManager>.i;
            if (csm != null)
            {
                csm.ShakeCamera(0.9f * severity, 1.8f * severity);
            }

            if (disruptionRoutine == null)
            {
                disruptionRoutine = StartCoroutine(DisruptionRoutine());
            }
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
                    float elapsed = disruptionDuration - remaining;
                    float decay = Mathf.Clamp01(remaining / disruptionDuration);
                    float prompt = Mathf.Clamp01(1f - elapsed / E1Window);
                    float intermediate = elapsed < E2Window
                        ? Mathf.Clamp01(1f - (elapsed - E1Window) / (E2Window - E1Window))
                        : 0f;
                    float late = Mathf.Clamp01(1f - (elapsed - E2Window) /
                        Mathf.Max(0.1f, disruptionDuration - E2Window));

                    // 1. Screen flash & static pulse on native blackout overlay
                    if (blackout != null)
                    {
                        float flicker = 0.5f + Mathf.Sin(Time.time * 50f) * 0.5f;
                        float pulse = prompt * 0.55f + intermediate * 0.3f * flicker + late * 0.08f;
                        blackout.color = new Color(0.25f, 0.65f, 1f, pulse);
                    }

                    // 2. Scramble CombatHUD markers & radar
                    var combatHud = SceneSingleton<CombatHUD>.i;
                    if (combatHud != null)
                    {
                        float strength = currentSeverity * (prompt * 5f + intermediate * 2.6f + late * 0.8f);
                        combatHud.jamAccumulation = Mathf.Max(combatHud.jamAccumulation, strength);
                    }

                    // 3. Glitch VirtualMFD screens during the prompt and intermediate pulses
                    if (elapsed < E2Window && virtualMfd != null)
                    {
                        mfdGlitchTimer -= Time.deltaTime;
                        if (mfdGlitchTimer <= 0f)
                        {
                            mfdGlitchTimer = UnityEngine.Random.Range(0.08f, 0.28f);
                            bool screenPowerOff = UnityEngine.Random.value > 0.4f;
                            if (virtualMfd.gameObject.activeSelf == screenPowerOff)
                            {
                                virtualMfd.gameObject.SetActive(!screenPowerOff);
                            }
                        }
                    }
                    else if (virtualMfd != null && !virtualMfd.gameObject.activeSelf)
                    {
                        virtualMfd.gameObject.SetActive(true);
                    }

                    // 4. Cockpit spark light flickering
                    if (cockpitSparkLight != null)
                    {
                        float sparkChance = prompt > 0f ? 0.2f : intermediate > 0.3f ? 0.55f : 0.8f;
                        if (elapsed < E2Window + 1.5f && UnityEngine.Random.value > sparkChance)
                        {
                            cockpitSparkLight.enabled = true;
                            cockpitSparkLight.intensity = UnityEngine.Random.Range(1.8f, 5.0f) *
                                (0.4f + 0.6f * decay);
                        }
                        else
                        {
                            cockpitSparkLight.enabled = false;
                        }
                    }

                    // 5. Camera jolt scaled by pulse phase
                    if (csm != null)
                    {
                        float jitter = prompt * 0.9f + intermediate * 0.35f + late * 0.08f;
                        if (jitter > 0.02f)
                        {
                            csm.ShakeCamera(jitter * currentSeverity, jitter * 1.4f * currentSeverity);
                        }
                    }

                    if (staticSource != null)
                    {
                        staticSource.volume = 0.85f * (0.35f + 0.65f * Mathf.Max(prompt, intermediate));
                    }

                    if (radioStaticSource != null)
                    {
                        radioStaticSource.volume = 0.75f *
                            (0.25f + 0.75f * Mathf.Max(intermediate, late * 0.35f));
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

                // Restore MFD screens to their active state after reboot
                if (virtualMfd != null && !virtualMfd.gameObject.activeSelf)
                {
                    virtualMfd.gameObject.SetActive(true);
                }
            }

            // Cleanup
            if (staticSource != null && staticSource.isPlaying)
            {
                staticSource.Stop();
            }

            if (radioStaticSource != null && radioStaticSource.isPlaying)
            {
                radioStaticSource.Stop();
            }

            if (cockpitSparkLight != null)
            {
                cockpitSparkLight.enabled = false;
            }
            disruptionRoutine = null;
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
