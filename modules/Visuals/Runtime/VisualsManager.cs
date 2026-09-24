using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Visuals.Configuration;
using BoscaliSummer.Features.Visuals.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>
    /// Coordinates native URP cinematic post-processing (ACES tonemapping, HDR bloom,
    /// color adjustments, film grain), G-force tunnel vision (blackout/redout),
    /// and transonic motion blur.
    /// </summary>
    internal sealed class VisualsManager : MonoBehaviour, ISceneService, IVisualEnhancements
    {
        public static VisualsManager Instance { get; private set; }

        private VisualsSettings settings;
        private ManualLogSource logger;

        private GameObject volumeHolder;
        private Volume customVolume;
        private VolumeProfile volumeProfile;

        // Cached overrides
        private Tonemapping tonemapping;
        private Bloom bloom;
        private ColorAdjustments colorAdjustments;
        private Vignette vignette;
        private FilmGrain filmGrain;
        private MotionBlur motionBlur;

        // Dynamic state tracking
        private float currentVignette = 0.18f;
        private float baselineVignette = 0.18f;
        private float baselineSaturation = 5.0f;

        public void Configure(VisualsSettings visualsSettings, ManualLogSource logSource)
        {
            settings = visualsSettings;
            logger = logSource;
            Instance = this;

            if (settings != null)
            {
                settings.OnSettingsChanged += ApplySettings;
            }
        }

        public bool IsEnabled => settings != null && settings.Enabled.Value;

        public bool CinematicPostFxEnabled
        {
            get => settings != null && settings.CinematicPostFxEnabled.Value;
            set
            {
                if (settings != null && settings.CinematicPostFxEnabled.Value != value)
                {
                    settings.CinematicPostFxEnabled.Value = value;
                    ApplySettings();
                }
            }
        }

        public bool GForceEffectsEnabled
        {
            get => settings != null && settings.GForceEffectsEnabled.Value;
            set
            {
                if (settings != null && settings.GForceEffectsEnabled.Value != value)
                {
                    settings.GForceEffectsEnabled.Value = value;
                    ApplySettings();
                }
            }
        }

        public bool MotionBlurEnabled
        {
            get => settings != null && settings.MotionBlurEnabled.Value;
            set
            {
                if (settings != null && settings.MotionBlurEnabled.Value != value)
                {
                    settings.MotionBlurEnabled.Value = value;
                    ApplySettings();
                }
            }
        }

        public bool FoliageDynamicsEnabled
        {
            get => settings != null && settings.FoliageDynamicsEnabled.Value;
            set
            {
                if (settings != null && settings.FoliageDynamicsEnabled.Value != value)
                {
                    settings.FoliageDynamicsEnabled.Value = value;
                    ApplySettings();
                    if (FoliageWindService.Instance != null) FoliageWindService.Instance.ApplySettings();
                }
            }
        }

        public float BloomIntensity
        {
            get => settings != null ? settings.BloomIntensity.Value : 0.4f;
            set
            {
                if (settings != null)
                {
                    settings.BloomIntensity.Value = Mathf.Clamp(value, 0.0f, 1.0f);
                    ApplySettings();
                }
            }
        }

        public float FoliageSwayStrength
        {
            get => settings != null ? settings.FoliageSwayStrength.Value : 1.0f;
            set
            {
                if (settings != null)
                {
                    settings.FoliageSwayStrength.Value = Mathf.Clamp(value, 0.1f, 2.5f);
                    ApplySettings();
                    if (FoliageWindService.Instance != null) FoliageWindService.Instance.ApplySettings();
                }
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (settings != null)
            {
                settings.OnSettingsChanged -= ApplySettings;
            }
            TeardownVolume();
            if (Instance == this) Instance = null;
        }

        public void ResetForScene()
        {
            TeardownVolume();
            currentVignette = baselineVignette;
            if (isActiveAndEnabled && settings != null && settings.Enabled.Value)
            {
                EnsureVolumeCreated();
                ApplySettings();
            }
        }

        private void Update()
        {
            if (Application.isBatchMode || settings == null || !settings.Enabled.Value)
            {
                if (customVolume != null && customVolume.weight > 0f) customVolume.weight = 0f;
                return;
            }

            EnsureVolumeCreated();

            bool postFxOn = settings.CinematicPostFxEnabled.Value;
            if (customVolume != null)
            {
                customVolume.weight = postFxOn ? 1f : 0f;
            }

            if (!postFxOn) return;

            UpdateFlightDynamics();
        }

        private void UpdateFlightDynamics()
        {
            CameraStateManager camState = SceneSingleton<CameraStateManager>.i;
            if (camState == null) return;

            float dt = Time.deltaTime;

            // 1. G-Force Physiological Feedback
            if (settings.GForceEffectsEnabled.Value)
            {
                float gForce = camState.gForce;

                // Positive G blackout tunnel vision
                currentVignette = VisualsMath.CalculateGForceVignette(gForce, currentVignette, dt, baselineVignette, 0.85f);
                if (vignette != null)
                {
                    vignette.intensity.value = currentVignette;
                }

                // Saturation loss under high G
                if (colorAdjustments != null)
                {
                    colorAdjustments.saturation.value = VisualsMath.CalculateGForceSaturation(gForce, baselineSaturation);

                    // Negative G redout
                    float redout = VisualsMath.CalculateGForceRedout(gForce);
                    if (redout > 0.005f)
                    {
                        colorAdjustments.colorFilter.value = Color.Lerp(Color.white, new Color(1.0f, 0.18f, 0.18f), redout);
                    }
                    else
                    {
                        colorAdjustments.colorFilter.value = Color.white;
                    }
                }
            }
            else
            {
                if (vignette != null) vignette.intensity.value = baselineVignette;
                if (colorAdjustments != null)
                {
                    colorAdjustments.saturation.value = baselineSaturation;
                    colorAdjustments.colorFilter.value = Color.white;
                }
            }

            // 2. Transonic / High-Speed Motion Blur
            if (settings.MotionBlurEnabled.Value && motionBlur != null)
            {
                float speed = 0f;
                float angularSpeed = 0f;

                if (camState.followingUnit is Aircraft aircraft && aircraft.rb != null)
                {
                    speed = aircraft.rb.velocity.magnitude;
                    angularSpeed = aircraft.rb.angularVelocity.magnitude * Mathf.Rad2Deg;
                }
                else if (camState.mainCamera != null)
                {
                    speed = camState.cameraVelocity.magnitude;
                }

                float mach = speed / 340f;
                float blurIntensity = VisualsMath.CalculateTransonicBlur(mach, angularSpeed);
                motionBlur.intensity.value = blurIntensity;
            }
            else if (motionBlur != null)
            {
                motionBlur.intensity.value = 0f;
            }
        }

        private void EnsureVolumeCreated()
        {
            if (customVolume != null && volumeProfile != null) return;

            try
            {
                volumeHolder = new GameObject("BoscaliSummer.VisualsVolume");
                volumeHolder.transform.SetParent(this.transform);

                customVolume = volumeHolder.AddComponent<Volume>();
                customVolume.isGlobal = true;
                customVolume.priority = 100f; // Global priority above default scene volume

                volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                volumeProfile.name = "BoscaliSummer.VisualsProfile";
                customVolume.profile = volumeProfile;

                // 1. Tonemapping (ACES gives filmic contrast curve)
                tonemapping = volumeProfile.Add<Tonemapping>();
                tonemapping.mode.Override(TonemappingMode.ACES);

                // 2. Controlled HDR Bloom (afterburners, tracers, solar specular glint)
                bloom = volumeProfile.Add<Bloom>();
                bloom.threshold.Override(1.15f);
                bloom.intensity.Override(settings.BloomIntensity.Value);
                bloom.scatter.Override(0.70f);

                // 3. Color Grading & Contrast
                colorAdjustments = volumeProfile.Add<ColorAdjustments>();
                colorAdjustments.postExposure.Override(0.10f);
                colorAdjustments.contrast.Override(10f);
                colorAdjustments.saturation.Override(baselineSaturation);
                colorAdjustments.colorFilter.Override(Color.white);

                // 4. Cockpit Vignette
                vignette = volumeProfile.Add<Vignette>();
                vignette.intensity.Override(baselineVignette);
                vignette.smoothness.Override(0.45f);

                // 5. Film Grain
                filmGrain = volumeProfile.Add<FilmGrain>();
                filmGrain.type.Override(FilmGrainLookup.Thin1);
                filmGrain.intensity.Override(0.12f);

                // 6. Motion Blur
                motionBlur = volumeProfile.Add<MotionBlur>();
                motionBlur.mode.Override(MotionBlurMode.CameraOnly);
                motionBlur.quality.Override(MotionBlurQuality.Medium);
                motionBlur.intensity.Override(0f);

                logger?.LogInfo("[Visuals] Injected native URP global post-processing Volume (ACES tonemapping, bloom, color grading).");
            }
            catch (Exception ex)
            {
                logger?.LogWarning($"[Visuals] Could not initialize URP post-processing volume: {ex.Message}");
            }
        }

        public void ApplySettings()
        {
            if (customVolume == null) return;

            bool enabled = settings.Enabled.Value && settings.CinematicPostFxEnabled.Value;
            customVolume.weight = enabled ? 1f : 0f;

            if (bloom != null)
            {
                bloom.intensity.value = settings.BloomIntensity.Value;
            }

            if (!settings.GForceEffectsEnabled.Value)
            {
                if (vignette != null) vignette.intensity.value = baselineVignette;
                if (colorAdjustments != null)
                {
                    colorAdjustments.saturation.value = baselineSaturation;
                    colorAdjustments.colorFilter.value = Color.white;
                }
            }

            if (!settings.MotionBlurEnabled.Value && motionBlur != null)
            {
                motionBlur.intensity.value = 0f;
            }
        }

        private void TeardownVolume()
        {
            if (volumeProfile != null)
            {
                Destroy(volumeProfile);
                volumeProfile = null;
            }
            if (volumeHolder != null)
            {
                Destroy(volumeHolder);
                volumeHolder = null;
            }
            customVolume = null;
            tonemapping = null;
            bloom = null;
            colorAdjustments = null;
            vignette = null;
            filmGrain = null;
            motionBlur = null;
        }
    }
}
