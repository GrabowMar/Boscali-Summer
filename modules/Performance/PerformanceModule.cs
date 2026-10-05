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
            context.AddSceneService<BaseGameTuning>(91)
                .Configure(context.Settings.Performance, context.Logger);
            context.AddClientSetting("FRAME BUDGET", "ADAPTIVE FX",
                "After sustained slow flight frames, halve scalable Boscali cosmetic budgets. " +
                "Takes effect after sampling; no mission or game restart.",
                context.Settings.Performance.Enabled);
            context.AddClientSetting("BASE GAME", "LOD FLOOR",
                "Raise the LOD bias to at least 1.5 so distant models simplify sooner. " +
                "Client-local; the game's own value is restored when switched off.",
                context.Settings.Performance.LodBiasFloor);
            context.AddClientSetting("BASE GAME", "SHADOW CAP",
                "Cap shadow distance at 2000 m to drop far shadow-cascade work. " +
                "Client-local; the game's own value is restored when switched off.",
                context.Settings.Performance.ShadowDistanceCap);
            context.AddClientSetting("BASE GAME", "FPS CAP 60",
                "Cap the frame rate at 60 fps for steadier frametimes and lower thermals. " +
                "Client-local; the game's own value is restored when switched off.",
                context.Settings.Performance.FrameRateCap);
            context.AddClientSetting("BASE GAME", "SCENE CLEANUP",
                "Unload unused assets and collect garbage on scene transitions " +
                "(menu/mission loads), never during flight. Client-local.",
                context.Settings.Performance.CleanupOnSceneChange);
            context.Logger.LogInfo("Performance=client-local Boscali cosmetic budget; game simulation unchanged");
        }
    }
}
