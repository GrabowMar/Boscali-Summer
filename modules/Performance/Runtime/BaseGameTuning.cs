using System;
using HarmonyLib;
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
    /// scene-transition cleanup, log trace cost, mip streaming and upload buffer size.
    /// Unlike the adaptive cosmetic budget, these touch the
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
        private float shadowCap = ClientTuningMath.ShadowDistanceCapMeters;
        // The game renders with URP, which ignores QualitySettings.shadowDistance.
        private UniversalRenderPipelineAsset shadowAsset;
        private bool scaleApplied;
        private float scaleOriginal = 1f;
        private UniversalRenderPipelineAsset scaleAsset;
        private float scaleCap = ClientTuningMath.RenderScaleCap;
        private UniversalRenderPipelineAsset msaaAsset;
        private int msaaOriginal = 1, msaaCap;
        private float lastCleanup = -CleanupSpacingSeconds;
        private const float CleanupSpacingSeconds = 30f;
        private bool tracesApplied;
        private StackTraceLogType logTraceOriginal, warningTraceOriginal;
        private bool streamingApplied;
        private bool streamingOriginal, streamingCamerasOriginal;
        private float streamingBudgetOriginal, streamingBudgetApplied;
        private bool uploadApplied;
        private int uploadOriginal, uploadSizeApplied;

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
            settings != null && isActiveAndEnabled && !Application.isBatchMode && !GameManager.IsHeadless;

        private void ApplyWanted()
        {
            if (settings == null) return;
            bool usable = Usable;
            SyncLod(usable && settings.LodBiasFloor.Value);
            // NO SHADOWS is the shadow cap at 0 m; a cap change hands the game value back first, then caps again
            float wantCap = settings.NoShadows.Value ? 0f : ClientTuningMath.ShadowDistanceCapMeters;
            if (shadowApplied && wantCap != shadowCap) SyncShadow(false);
            shadowCap = wantCap;
            SyncShadow(usable && (settings.ShadowDistanceCap.Value || settings.NoShadows.Value));
            HoldShadowCap();
            SyncScale(usable && settings.RenderScaleCap.Value);
            HoldScaleCap();
            SyncMsaa(usable ? (int)settings.AntiAliasingCap.Value : 0);
            SyncDetail(usable && settings.DetailCap.Value);
            SyncLogTraces(usable && settings.QuietUnityLogs.Value);
            SyncStreaming(usable && settings.TextureStreaming.Value);
            SyncUploadBuffer(usable && settings.PreallocateUploadBuffer.Value);
            BaseGameTuningReport.LodFloorActive = lodApplied;
            BaseGameTuningReport.ShadowCapActive = shadowApplied;
        }

        private void SyncLod(bool want)
        {
            if (!want && !lodApplied) return;
            try
            {
                float current = QualitySettings.lodBias;
                if (want)
                {
                    float capped = ClientTuningMath.PlanLodBias(current, true);
                    if (!(capped < current)) return;
                    QualitySettings.lodBias = capped;
                    lodOriginal = current;
                    if (!lodApplied)
                        Plugin.Logger?.LogInfo("Performance: LOD bias cap on (game " +
                            current.ToString("F2") + " -> " + capped.ToString("F2") + ").");
                    lodApplied = true;
                }
                else
                {
                    float restored = ClientTuningMath.RestoreLodBias(lodOriginal, current);
                    if (restored != current) QualitySettings.lodBias = restored;
                    lodApplied = false;
                    Plugin.Logger?.LogInfo("Performance: LOD bias restored to " + restored.ToString("F2") + ".");
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
                    asset.shadowDistance = ClientTuningMath.PlanShadowDistance(shadowOriginal, true, shadowCap);
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
                    shadowAsset.shadowDistance = ClientTuningMath.RestoreShadowDistance(shadowOriginal, shadowAsset.shadowDistance, shadowCap);
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
            if (current <= shadowCap) return;
            shadowOriginal = current;
            shadowAsset.shadowDistance = shadowCap;
        }

        private void SyncScale(bool want)
        {
            float cap = want ? ClientTuningMath.ClampRenderScale(settings.RenderScale.Value) : scaleCap;
            // A new share from the config: hand the game value back, then cap again.
            if (want && scaleApplied && cap != scaleCap) SyncScale(false);
            if (want == scaleApplied) return;
            try
            {
                if (want)
                {
                    var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    if (asset == null) return;
                    scaleAsset = asset;
                    scaleOriginal = asset.renderScale;
                    scaleCap = cap;
                    asset.renderScale = ClientTuningMath.PlanRenderScale(scaleOriginal, true, cap);
                    scaleApplied = true;
                    Plugin.Logger?.LogInfo("Performance: render scale cap on (game " +
                        scaleOriginal.ToString("F2") + " -> " + asset.renderScale.ToString("F2") + ").");
                }
                else
                {
                    scaleApplied = false;
                    if (scaleAsset == null) return;
                    scaleAsset.renderScale = ClientTuningMath.RestoreRenderScale(scaleOriginal, scaleAsset.renderScale, scaleCap);
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
            if (current <= scaleCap) return;
            scaleOriginal = current;
            scaleAsset.renderScale = scaleCap;
        }

        /// <summary>
        /// MSAA cap: <paramref name="cap"/> samples at most (0 = no cap). The game's sample count is captured when the cap
        /// takes over, re-captured when the game writes a higher value (its graphics menu, a mission load), and handed back
        /// only while the asset still holds our cap. Checked every frame like the other caps: one int compare.
        /// </summary>
        private void SyncMsaa(int cap)
        {
            var asset = cap != 0 ? GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset : null;
            // A new cap, or the game swapped its pipeline asset: hand the game's value back first.
            if (msaaAsset != null && (cap != msaaCap || !ReferenceEquals(asset, msaaAsset)))
            {
                // Only while the asset still holds our cap: a lower value the player picked meanwhile stays.
                if (msaaAsset.msaaSampleCount == msaaCap) msaaAsset.msaaSampleCount = msaaOriginal;
                Plugin.Logger?.LogInfo("Performance: MSAA restored to " + msaaAsset.msaaSampleCount + "x.");
                msaaAsset = null;
            }
            if (cap == 0 || asset == null) return;
            int current = asset.msaaSampleCount;
            if (msaaAsset != null)
            {
                if (current > cap) { msaaOriginal = current; asset.msaaSampleCount = cap; }   // the game wrote it back up
                return;
            }
            if (current <= cap) return;   // already at or under the cap: nothing to own
            msaaAsset = asset;
            msaaOriginal = current;
            msaaCap = cap;
            asset.msaaSampleCount = cap;
            Plugin.Logger?.LogInfo("Performance: MSAA cap on (game " + msaaOriginal + "x -> " + cap + "x).");
        }

        // The game's DetailSettings setters write PlayerPrefs; the backing fields keep the cap out of the saved settings.
        private static readonly AccessTools.FieldRef<NuclearOption.Effects.DetailSettings, bool> GrassField =
            Field<bool>("_grassEnabled");
        private static readonly AccessTools.FieldRef<NuclearOption.Effects.DetailSettings, float> TreeField =
            Field<float>("_treeRangeMultiplier");
        private const float DetailTreeCap = 0.5f;
        private NuclearOption.Effects.DetailSettings detailOwner;
        private bool grassOriginal;
        private float treeOriginal;

        /// <summary>DETAIL CAP: grass off and trees at half range, held each frame (the game's menu may write them),
        /// handed back on release. The game's DetailRenderer re-reads both every LateUpdate.</summary>
        private void SyncDetail(bool want)
        {
            if ((!want && detailOwner == null) || GrassField == null || TreeField == null) return;
            NuclearOption.Effects.DetailSettings detail = PlayerSettings.DetailSettings;
            if (detailOwner != null && (!want || !ReferenceEquals(detail, detailOwner)))
            {
                if (!GrassField(detailOwner)) GrassField(detailOwner) = grassOriginal;
                if (TreeField(detailOwner) == DetailTreeCap) TreeField(detailOwner) = treeOriginal;
                detailOwner = null;
                Plugin.Logger?.LogInfo("Performance: detail cap off (grass " + grassOriginal + ", trees x" + treeOriginal.ToString("F2") + ").");
            }
            if (!want || detail == null) return;
            if (detailOwner == null)
            {
                detailOwner = detail;
                grassOriginal = GrassField(detail);
                treeOriginal = TreeField(detail);
                Plugin.Logger?.LogInfo("Performance: detail cap on (grass off, trees x" + treeOriginal.ToString("F2") + " -> x" + DetailTreeCap.ToString("F2") + ").");
            }
            if (GrassField(detail)) { grassOriginal = true; GrassField(detail) = false; }
            float trees = TreeField(detail);
            if (trees > DetailTreeCap) { treeOriginal = trees; TreeField(detail) = DetailTreeCap; }
        }

        private static AccessTools.FieldRef<NuclearOption.Effects.DetailSettings, T> Field<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<NuclearOption.Effects.DetailSettings, T>(name); }
            catch (Exception) { return null; }
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

        // Independent implementations using Unity's public API. No REPOPerformanceKit code or binary.
        private void SyncLogTraces(bool want)
        {
            if (want == tracesApplied) return;
            if (want)
            {
                logTraceOriginal = Application.GetStackTraceLogType(LogType.Log);
                warningTraceOriginal = Application.GetStackTraceLogType(LogType.Warning);
                tracesApplied = true;
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            }
            else
            {
                tracesApplied = false;
                if (Application.GetStackTraceLogType(LogType.Log) == StackTraceLogType.None)
                    Application.SetStackTraceLogType(LogType.Log, logTraceOriginal);
                if (Application.GetStackTraceLogType(LogType.Warning) == StackTraceLogType.None)
                    Application.SetStackTraceLogType(LogType.Warning, warningTraceOriginal);
            }
        }

        private void SyncStreaming(bool want)
        {
            want = want && SystemInfo.supportsMipStreaming;
            float budget = want
                ? FiniteRange(settings.TextureStreamingBudgetMb.Value, 128f, 8192f, 1024f)
                : streamingBudgetApplied;
            if (want && streamingApplied && budget != streamingBudgetApplied) SyncStreaming(false);
            if (want == streamingApplied) return;
            if (want)
            {
                streamingOriginal = QualitySettings.streamingMipmapsActive;
                streamingBudgetOriginal = QualitySettings.streamingMipmapsMemoryBudget;
                streamingCamerasOriginal = QualitySettings.streamingMipmapsAddAllCameras;
                streamingBudgetApplied = budget;
                streamingApplied = true;
                QualitySettings.streamingMipmapsMemoryBudget = budget;
                // Include cockpit and sensor feeds as well as the main view.
                QualitySettings.streamingMipmapsAddAllCameras = true;
                QualitySettings.streamingMipmapsActive = true;
                Plugin.Logger?.LogInfo("Performance: texture streaming on; " + Texture.streamingTextureCount +
                    " streamable textures currently loaded. Only textures built for streaming can benefit.");
            }
            else
            {
                streamingApplied = false;
                if (QualitySettings.streamingMipmapsActive)
                    QualitySettings.streamingMipmapsActive = streamingOriginal;
                if (QualitySettings.streamingMipmapsMemoryBudget == streamingBudgetApplied)
                    QualitySettings.streamingMipmapsMemoryBudget = streamingBudgetOriginal;
                if (QualitySettings.streamingMipmapsAddAllCameras)
                    QualitySettings.streamingMipmapsAddAllCameras = streamingCamerasOriginal;
            }
        }

        private void SyncUploadBuffer(bool want)
        {
            int size = want
                ? Mathf.RoundToInt(FiniteRange(settings.UploadBufferMb.Value, 16f, 256f, 64f))
                : uploadSizeApplied;
            if (want && uploadApplied && Math.Max(uploadOriginal, size) != uploadSizeApplied) SyncUploadBuffer(false);
            if (want == uploadApplied) return;
            if (want)
            {
                uploadOriginal = QualitySettings.asyncUploadBufferSize;
                uploadSizeApplied = Math.Max(uploadOriginal, size);
                uploadApplied = true;
                QualitySettings.asyncUploadBufferSize = uploadSizeApplied;
            }
            else
            {
                uploadApplied = false;
                if (QualitySettings.asyncUploadBufferSize == uploadSizeApplied)
                    QualitySettings.asyncUploadBufferSize = uploadOriginal;
            }
        }

        private static float FiniteRange(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        private void RestoreAll()
        {
            SyncLod(false);
            SyncShadow(false);
            SyncScale(false);
            SyncMsaa(0);
            SyncDetail(false);
            SyncLogTraces(false);
            SyncStreaming(false);
            SyncUploadBuffer(false);
            BaseGameTuningReport.LodFloorActive =
                BaseGameTuningReport.ShadowCapActive = false;
        }
    }
}
