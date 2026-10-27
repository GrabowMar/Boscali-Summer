using System;
using BoscaliSummer.Modules.Performance.Configuration;
using BoscaliSummer.Modules.Performance.Domain;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Performance.Runtime
{
    /// <summary>
    /// Explicit opt-in Unity-level tunings: LOD floor, shadow cap, render scale cap, and a
    /// scene-transition cleanup. Unlike the adaptive cosmetic budget, these touch the
    /// game's own render settings — but only while the toggle is on, only in the cheaper
    /// direction, and only client-locally. Each knob captures the game's value on first
    /// apply and restores it exactly on disable, scene reset keeps enabled knobs applied
    /// (the game may reset quality on load), and teardown always restores everything.
    /// Headless servers and batch mode never apply anything.
    /// </summary>
    internal sealed class BaseGameTuning : MonoBehaviour, ISceneService
    {
        private PerformanceSettings settings;

        private bool lodApplied;
        private float lodOriginal = 1f;
        private bool shadowApplied;
        private float shadowOriginal;
        // The game renders with URP, which ignores QualitySettings.shadowDistance.
        private UniversalRenderPipelineAsset shadowAsset;
        private bool scaleApplied;
        private float scaleOriginal = 1f;
        private UniversalRenderPipelineAsset scaleAsset;
        private float lastCleanup = -CleanupSpacingSeconds;
        private const float CleanupSpacingSeconds = 30f;

        public void Configure(PerformanceSettings config)
        {
            settings = config;
            ResetForScene();
        }

        public void ResetForScene()
        {
            // Undo values we still own before re-capturing. Otherwise a scene that
            // preserves quality would replace the originals with our capped values.
            RestoreAll();
            ApplyWanted();
            if (settings != null && settings.CleanupOnSceneChange.Value && Usable)
                RunCleanup();
        }

        private void OnDisable() => RestoreAll();
        private void OnDestroy() => RestoreAll();

        private void Update() => ApplyWanted();

        private bool Usable =>
            settings != null && !Application.isBatchMode && !GameManager.IsHeadless;

        private void ApplyWanted()
        {
            if (settings == null) return;
            bool usable = Usable;
            SyncLod(usable && settings.LodBiasFloor.Value);
            SyncShadow(usable && settings.ShadowDistanceCap.Value);
            HoldShadowCap();
            SyncScale(usable && settings.RenderScaleCap.Value);
            HoldScaleCap();
            BaseGameTuningReport.LodFloorActive = lodApplied;
            BaseGameTuningReport.ShadowCapActive = shadowApplied;
        }

        private void SyncLod(bool want)
        {
            if (want == lodApplied) return;
            try
            {
                if (want)
                {
                    lodOriginal = QualitySettings.lodBias;
                    QualitySettings.lodBias = ClientTuningMath.PlanLodBias(lodOriginal, true);
                    lodApplied = true;
                    Plugin.Logger?.LogInfo("Performance: LOD bias cap on (game " +
                        lodOriginal.ToString("F2") + " -> " +
                        QualitySettings.lodBias.ToString("F2") + ").");
                }
                else
                {
                    QualitySettings.lodBias = ClientTuningMath.RestoreLodBias(lodOriginal, QualitySettings.lodBias);
                    lodApplied = false;
                    Plugin.Logger?.LogInfo("Performance: LOD bias restored to " +
                        QualitySettings.lodBias.ToString("F2") + ".");
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning("Performance: LOD bias tuning failed: " + error.Message);
            }
        }

        private void SyncShadow(bool want)
        {
            if (want == shadowApplied) return;
            try
            {
                if (want)
                {
                    var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (asset == null) return;
                    shadowAsset = asset;
                    shadowOriginal = asset.shadowDistance;
                    asset.shadowDistance = ClientTuningMath.PlanShadowDistance(shadowOriginal, true);
                    shadowApplied = true;
                    Plugin.Logger?.LogInfo("Performance: shadow distance cap on (game " +
                        shadowOriginal.ToString("F0") + " m -> " +
                        asset.shadowDistance.ToString("F0") + " m).");
                }
                else
                {
                    // Restore the asset we changed, even if a new quality preset swapped the active one.
                    shadowApplied = false;
                    if (shadowAsset == null) return;
                    shadowAsset.shadowDistance = ClientTuningMath.RestoreShadowDistance(shadowOriginal, shadowAsset.shadowDistance);
                    Plugin.Logger?.LogInfo("Performance: shadow distance restored to " +
                        shadowAsset.shadowDistance.ToString("F0") + " m.");
                    shadowAsset = null;
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning("Performance: shadow cap tuning failed: " + error.Message);
            }
        }

        // The game writes its own shadow distance back after a mission loads (seen live:
        // 1000 m became 2000 m again), so the cap is held, not set once.
        private void HoldShadowCap()
        {
            if (!shadowApplied || shadowAsset == null) return;
            if (!ReferenceEquals(GraphicsSettings.currentRenderPipeline, shadowAsset))
            {
                SyncShadow(false);
                return;
            }
            float current = shadowAsset.shadowDistance;
            if (current <= ClientTuningMath.ShadowDistanceCapMeters) return;
            shadowOriginal = current;
            shadowAsset.shadowDistance = ClientTuningMath.ShadowDistanceCapMeters;
        }

        private void SyncScale(bool want)
        {
            if (want == scaleApplied) return;
            try
            {
                if (want)
                {
                    var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (asset == null) return;
                    scaleAsset = asset;
                    scaleOriginal = asset.renderScale;
                    asset.renderScale = ClientTuningMath.PlanRenderScale(scaleOriginal, true);
                    scaleApplied = true;
                    Plugin.Logger?.LogInfo("Performance: render scale cap on (game " +
                        scaleOriginal.ToString("F2") + " -> " + asset.renderScale.ToString("F2") + ").");
                }
                else
                {
                    scaleApplied = false;
                    if (scaleAsset == null) return;
                    scaleAsset.renderScale = ClientTuningMath.RestoreRenderScale(scaleOriginal, scaleAsset.renderScale);
                    Plugin.Logger?.LogInfo("Performance: render scale restored to " +
                        scaleAsset.renderScale.ToString("F2") + ".");
                    scaleAsset = null;
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning("Performance: render scale tuning failed: " + error.Message);
            }
        }

        // Held like the shadow cap: a graphics change or mission load may write the asset back.
        private void HoldScaleCap()
        {
            if (!scaleApplied || scaleAsset == null) return;
            if (!ReferenceEquals(GraphicsSettings.currentRenderPipeline, scaleAsset))
            {
                SyncScale(false);
                return;
            }
            float current = scaleAsset.renderScale;
            if (current <= ClientTuningMath.RenderScaleCap) return;
            scaleOriginal = current;
            scaleAsset.renderScale = ClientTuningMath.RenderScaleCap;
        }

        private void RunCleanup()
        {
            // A mission load fires several scene resets in a row; one cleanup covers them.
            if (Time.realtimeSinceStartup - lastCleanup < CleanupSpacingSeconds) return;
            lastCleanup = Time.realtimeSinceStartup;
            try
            {
                Resources.UnloadUnusedAssets();
                GC.Collect();
                Plugin.Logger?.LogInfo("Performance: scene-transition cleanup ran.");
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning("Performance: scene cleanup failed: " + error.Message);
            }
        }

        private void RestoreAll()
        {
            SyncLod(false);
            SyncShadow(false);
            SyncScale(false);
            BaseGameTuningReport.LodFloorActive =
                BaseGameTuningReport.ShadowCapActive = false;
        }
    }
}
