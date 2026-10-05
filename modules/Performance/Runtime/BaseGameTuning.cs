using System;
using BepInEx.Logging;
using BoscaliSummer.Modules.Performance.Configuration;
using BoscaliSummer.Modules.Performance.Domain;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

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
        private ManualLogSource logger;

        private bool lodApplied;
        private float lodOriginal = 1f;
        private bool shadowApplied;
        private float shadowOriginal;
        private bool frameApplied;
        private int frameOriginal = -1;

        public void Configure(PerformanceSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
            ResetForScene();
        }

        public void ResetForScene()
        {
            // Scene loads may reset quality behind our back, so drop the applied flags
            // without restoring: ApplyWanted then re-captures the fresh game values as
            // the new originals and re-asserts enabled knobs. Cleanup runs on the
            // transition itself, never during flight.
            lodApplied = shadowApplied = frameApplied = false;
            ApplyWanted();
            if (settings != null && settings.CleanupOnSceneChange.Value && Usable)
                RunCleanup();
        }

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
                    logger?.LogInfo("Performance: LOD bias floor on (game " +
                        lodOriginal.ToString("F2") + " -> " +
                        QualitySettings.lodBias.ToString("F2") + ").");
                }
                else
                {
                    QualitySettings.lodBias = lodOriginal;
                    lodApplied = false;
                    logger?.LogInfo("Performance: LOD bias restored to " +
                        lodOriginal.ToString("F2") + ".");
                }
            }
            catch (Exception error)
            {
                logger?.LogWarning("Performance: LOD bias tuning failed: " + error.Message);
            }
        }

        private void SyncShadow(bool want)
        {
            if (want == shadowApplied) return;
            try
            {
                if (want)
                {
                    shadowOriginal = QualitySettings.shadowDistance;
                    QualitySettings.shadowDistance =
                        ClientTuningMath.PlanShadowDistance(shadowOriginal, true);
                    shadowApplied = true;
                    logger?.LogInfo("Performance: shadow distance cap on (game " +
                        shadowOriginal.ToString("F0") + " m -> " +
                        QualitySettings.shadowDistance.ToString("F0") + " m).");
                }
                else
                {
                    QualitySettings.shadowDistance = shadowOriginal;
                    shadowApplied = false;
                    logger?.LogInfo("Performance: shadow distance restored to " +
                        shadowOriginal.ToString("F0") + " m.");
                }
            }
            catch (Exception error)
            {
                logger?.LogWarning("Performance: shadow cap tuning failed: " + error.Message);
            }
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
                    logger?.LogInfo("Performance: frame rate cap on (game " +
                        frameOriginal + " -> " + Application.targetFrameRate + ").");
                }
                else
                {
                    Application.targetFrameRate = frameOriginal;
                    frameApplied = false;
                    logger?.LogInfo("Performance: frame rate restored to " + frameOriginal + ".");
                }
            }
            catch (Exception error)
            {
                logger?.LogWarning("Performance: frame cap tuning failed: " + error.Message);
            }
        }

        private void RunCleanup()
        {
            try
            {
                Resources.UnloadUnusedAssets();
                GC.Collect();
                logger?.LogInfo("Performance: scene-transition cleanup ran.");
            }
            catch (Exception error)
            {
                logger?.LogWarning("Performance: scene cleanup failed: " + error.Message);
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
