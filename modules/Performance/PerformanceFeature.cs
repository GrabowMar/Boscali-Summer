using System;
using BoscaliSummer.Features.Performance.Runtime;
using BoscaliSummer.Framework.Features;
using UnityEngine;

namespace BoscaliSummer.Features.Performance
{
    internal sealed class PerformanceFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("performance", "Adaptive cosmetic performance");

        public FeatureMetadata Metadata => Feature;
        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(FeatureContext context)
        {
            if (Application.isBatchMode) return;
            context.AddSceneService<PerformanceMonitor>(90)
                .Configure(context.Settings.Performance, context.Logger);
            context.AddClientSetting("FRAME BUDGET", "ADAPTIVE FX",
                "After sustained slow flight frames, halve scalable Boscali cosmetic budgets. " +
                "Takes effect after sampling; no mission or game restart.",
                context.Settings.Performance.Enabled);
            context.Logger.LogInfo("Performance=client-local Boscali cosmetic budget; game simulation unchanged");
        }
    }
}
