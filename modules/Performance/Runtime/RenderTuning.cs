using System;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Performance.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Performance.Runtime
{
    /// <summary>Lifetime and explicit opt-ins for savings in the existing renderer.</summary>
    internal sealed class RenderTuning : MonoBehaviour, ISceneService, IRenderTuning
    {
        private static RenderTuning live;
        // Registration belongs to the relief's lifetime, independently of this service.
        private static Camera reliefCamera;
        private PerformanceSettings settings;

        private static bool Usable => live != null && live.isActiveAndEnabled && live.settings != null &&
            !Application.isBatchMode && !GameManager.IsHeadless;

        internal static bool IndependentSharpening => Usable && live.settings.IndependentSharpening.Value;
        internal static bool OptimizeCinematicStreaks => Usable && live.settings.OptimizeCinematicStreaks.Value;
        internal static float SharpenStrength
        {
            get
            {
                if (!Usable) return 0f;
                float value = live.settings.IndependentSharpenStrength.Value;
                return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
            }
        }

        bool IRenderTuning.IndependentSharpening => IndependentSharpening;
        bool IRenderTuning.OptimizeCinematicStreaks => OptimizeCinematicStreaks;
        float IRenderTuning.SharpenStrength => SharpenStrength;
        void IRenderTuning.RegisterReliefCamera(Camera camera) => RegisterReliefCamera(camera);
        void IRenderTuning.UnregisterReliefCamera(Camera camera) => UnregisterReliefCamera(camera);

        // Feature callbacks, not GPU pass timings. The normal direct-draw relief path never calls this feature.
        internal static int ReliefAoCalls { get; private set; }
        internal static int ReliefAoSkipped { get; private set; }
        internal static bool ReliefCameraRegistered => reliefCamera != null;

        public void Configure(PerformanceSettings config)
        {
            settings = config;
            if (isActiveAndEnabled) live = this;
            ResetForScene();
        }

        public void ResetForScene()
        {
            ReliefAoCalls = 0;
            ReliefAoSkipped = 0;
        }

        private void OnEnable() { if (settings != null) live = this; }
        private void OnDisable() { if (ReferenceEquals(live, this)) live = null; }
        private void OnDestroy() => OnDisable();

        internal static void RegisterReliefCamera(Camera camera) => reliefCamera = camera;
        internal static void UnregisterReliefCamera(Camera camera)
        {
            if (ReferenceEquals(reliefCamera, camera)) reliefCamera = null;
        }

        internal static bool SkipReliefAo(Camera camera, object feature)
        {
            if (!Usable || reliefCamera == null || !ReferenceEquals(camera, reliefCamera)) return false;
            ReliefAoCalls++;
            if (!live.settings.SkipReliefAmbientOcclusion.Value || !ReliefAmbientOcclusionPatch.IsLightingOnly(feature)) return false;
            ReliefAoSkipped++;
            return true;
        }
    }

    /// <summary>Omit only lighting AO on the registered unlit relief camera; never mutate a shared renderer feature.</summary>
    [HarmonyPatch]
    internal static class ReliefAmbientOcclusionPatch
    {
        private static readonly Type FeatureType = typeof(UniversalRenderer).Assembly.GetType(
            "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion", false);
        private static readonly MethodInfo AddPasses = FeatureType == null ? null : AccessTools.Method(FeatureType,
            "AddRenderPasses", new[] { typeof(ScriptableRenderer), typeof(RenderingData).MakeByRefType() });
        private static readonly FieldInfo Settings = FeatureType == null ? null : AccessTools.Field(FeatureType, "m_Settings");
        private static readonly FieldInfo AfterOpaque = Settings == null ? null : AccessTools.Field(Settings.FieldType, "AfterOpaque");

        private static bool Prepare()
        {
            bool available = AddPasses != null && AfterOpaque?.FieldType == typeof(bool);
            if (!available) Plugin.Logger?.LogWarning("Performance: relief AO tuning unavailable; native SSAO API does not match.");
            return available;
        }

        private static MethodBase TargetMethod() => AddPasses;

        internal static bool IsLightingOnly(object feature)
        {
            if (AfterOpaque?.FieldType != typeof(bool) || feature == null || !FeatureType.IsInstanceOfType(feature)) return false;
            object settings = Settings.GetValue(feature);
            // AfterOpaque multiplies the finished image, including unlit pixels, so preserve that mode.
            return settings != null && !(bool)AfterOpaque.GetValue(settings);
        }

        private static bool Prefix(object __instance, ref RenderingData renderingData) =>
            !RenderTuning.SkipReliefAo(renderingData.cameraData.camera, __instance);
    }
}
