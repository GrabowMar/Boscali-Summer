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
    /// Explicit opt-in Unity-level tunings: LOD floor, shadow cap, frame-rate cap, and a
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
        private float lastCleanup = -CleanupSpacingSeconds;
        private const float CleanupSpacingSeconds = 30f;
        private bool frameApplied;
        private int frameOriginal = -1;

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
            SyncFrame(usable && settings.FrameRateCap.Value);
            BaseGameTuningReport.LodFloorActive = lodApplied;
            BaseGameTuningReport.ShadowCapActive = shadowApplied;
            BaseGameTuningReport.FrameCapActive = frameApplied;
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

        private void SyncFrame(bool want)
        {
            if (want == frameApplied) return;
            try
            {
                if (want)
                {
                    frameOriginal = Application.targetFrameRate;
                    Application.targetFrameRate =
                        ClientTuningMath.PlanFrameRate(frameOriginal, true);
                    frameApplied = true;
                    Plugin.Logger?.LogInfo("Performance: frame rate cap on (game " +
                        frameOriginal + " -> " + Application.targetFrameRate + ").");
                }
                else
                {
                    Application.targetFrameRate = ClientTuningMath.RestoreFrameRate(frameOriginal, Application.targetFrameRate);
                    frameApplied = false;
                    Plugin.Logger?.LogInfo("Performance: frame rate restored to " + Application.targetFrameRate + ".");
                }
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogWarning("Performance: frame cap tuning failed: " + error.Message);
            }
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
            SyncFrame(false);
            BaseGameTuningReport.LodFloorActive =
                BaseGameTuningReport.ShadowCapActive =
                BaseGameTuningReport.FrameCapActive = false;
        }
    }
}
