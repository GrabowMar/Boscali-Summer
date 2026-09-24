using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Visuals.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.Effects;
using UnityEngine;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>
    /// Coordinates vegetation wind dynamics across terrain grass and foliage.
    /// Modulates native grass wind parameters and broadcasts global wind vector.
    /// </summary>
    internal sealed class FoliageWindService : MonoBehaviour, ISceneService
    {
        public static FoliageWindService Instance { get; private set; }

        private VisualsSettings settings;
        private ManualLogSource logger;

        private const float DefaultWindSpeed = 2.12f;
        private const float DefaultWindStrength = 0.25f;
        private const float DefaultWindDensity = 4.52f;

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
            RestoreBaseline();
            if (Instance == this) Instance = null;
        }

        public void ResetForScene()
        {
            if (isActiveAndEnabled && settings != null && settings.Enabled.Value)
            {
                ApplySettings();
            }
        }

        private void Update()
        {
            if (Application.isBatchMode || settings == null || !settings.Enabled.Value) return;
            if (!settings.FoliageDynamicsEnabled.Value) return;

            // Broadcast dynamic global wind vector with gentle turbulence
            float time = Time.time;
            float swayStrength = settings.FoliageSwayStrength.Value;
            float headingRad = 0.785f; // ~45 deg
            float gust = 1.0f + 0.35f * Mathf.Sin(time * 0.8f);

            Vector4 windVec = new Vector4(
                Mathf.Cos(headingRad) * gust * swayStrength,
                0f,
                Mathf.Sin(headingRad) * gust * swayStrength,
                swayStrength
            );

            Shader.SetGlobalVector("_GlobalWindVector", windVec);
        }

        public void ApplySettings()
        {
            if (Application.isBatchMode || settings == null) return;

            bool active = settings.Enabled.Value && settings.FoliageDynamicsEnabled.Value;
            float strengthScale = active ? settings.FoliageSwayStrength.Value : 1.0f;

            // Apply to active GrassRenderers in scene
            try
            {
                DetailRenderer detailRenderer = SceneSingleton<DetailRenderer>.i;
                if (detailRenderer != null)
                {
                    GrassRenderer[] grassList = detailRenderer.GetComponentsInChildren<GrassRenderer>(true);
                    float targetStrength = active ? (DefaultWindStrength * 1.6f * strengthScale) : DefaultWindStrength;
                    float targetSpeed = active ? (DefaultWindSpeed * 1.25f) : DefaultWindSpeed;

                    foreach (var grass in grassList)
                    {
                        if (grass == null || grass.grassMaterialProps == null) continue;
                        grass.grassMaterialProps.SetFloat("_WindStrength", targetStrength);
                        grass.grassMaterialProps.SetFloat("_WindSpeed", targetSpeed);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug($"[Visuals] Grass wind update skipped: {ex.Message}");
            }
        }

        private void RestoreBaseline()
        {
            try
            {
                DetailRenderer detailRenderer = SceneSingleton<DetailRenderer>.i;
                if (detailRenderer != null)
                {
                    GrassRenderer[] grassList = detailRenderer.GetComponentsInChildren<GrassRenderer>(true);
                    foreach (var grass in grassList)
                    {
                        if (grass == null || grass.grassMaterialProps == null) continue;
                        grass.grassMaterialProps.SetFloat("_WindStrength", DefaultWindStrength);
                        grass.grassMaterialProps.SetFloat("_WindSpeed", DefaultWindSpeed);
                    }
                }
                Shader.SetGlobalVector("_GlobalWindVector", Vector4.zero);
            }
            catch
            {
                // Ignored on teardown
            }
        }
    }
}
