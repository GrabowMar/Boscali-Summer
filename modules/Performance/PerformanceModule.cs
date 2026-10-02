using System;
using BoscaliSummer.Modules.Performance.Runtime;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Performance
{
    internal sealed class PerformanceModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("performance", "Adaptive cosmetic performance");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => Type.EmptyTypes;

        public void Install(ModuleContext context)
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
