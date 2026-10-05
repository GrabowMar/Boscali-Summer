using BepInEx.Logging;
using BoscaliSummer.Modules.Performance.Domain;
using BoscaliSummer.Modules.Performance.Configuration;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Performance.Runtime
{
    internal sealed class PerformanceMonitor : MonoBehaviour, ISceneService
    {
        private readonly FrameBudgetPolicy policy = new FrameBudgetPolicy();
        private ManualLogSource logger;
        private PerformanceSettings settings;
        private bool appliedReduction;
        private bool wasEnabled;

        public void Configure(PerformanceSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
            ResetForScene();
        }

        public void ResetForScene()
        {
            policy.Reset();
            appliedReduction = false;
            wasEnabled = false;
            FxBus.SetAdaptiveCap(null);
            AdaptiveBudgetReport.Reduced = false;
            AdaptiveBudgetReport.LastAverageMs = AdaptiveBudgetReport.LastFps = 0f;
        }

        private void OnDestroy()
        {
            FxBus.SetAdaptiveCap(null);
            AdaptiveBudgetReport.Enabled = AdaptiveBudgetReport.Reduced = false;
        }

        private void Update()
        {
            bool enabled = settings != null && settings.Enabled.Value;
            AdaptiveBudgetReport.Enabled = enabled;
            if (!enabled)
            {
                if (wasEnabled)
                {
                    wasEnabled = false;
                    appliedReduction = false;
                    FxBus.SetAdaptiveCap(null);
                    AdaptiveBudgetReport.Reduced = false;
                    policy.Reset();
                }
                return;
            }
            if (!wasEnabled)
            {
                wasEnabled = true;
                policy.Reset();
            }
            bool active = Time.timeScale > 0.5f && !GameplayUI.GameIsPaused &&
                !DynamicMap.mapMaximized &&
                GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null;
            policy.Observe(Time.unscaledDeltaTime, active);
            AdaptiveBudgetReport.LastAverageMs = policy.LastAverageMs;
            AdaptiveBudgetReport.LastFps =
                policy.LastAverageMs > 0.01f ? 1000f / policy.LastAverageMs : 0f;
            if (appliedReduction == policy.Reduced) return;
            appliedReduction = policy.Reduced;
            AdaptiveBudgetReport.Reduced = appliedReduction;
            FxBus.SetAdaptiveCap(appliedReduction ? FxQuality.Low : (FxQuality?)null);
            logger?.LogInfo("Performance: Boscali cosmetic budget " +
                (appliedReduction ? "reduced" : "restored") +
                " after " + policy.LastAverageMs.ToString("F1") + " ms average frames.");
        }
    }
}
