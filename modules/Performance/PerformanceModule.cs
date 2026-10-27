using System;
using BoscaliSummer.Modules.Performance.Runtime;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Performance
{
    internal sealed class PerformanceModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("performance", "Adaptive cosmetic performance");

        public ModuleMetadata Metadata => Module;
        public Type[] PatchTypes => new[]
        {
            typeof(RemoteTurretSearchPatch), typeof(SleepingTurretPatch), typeof(DistantTracerPatch)
        };

        public void Install(ModuleContext context)
        {
            if (Application.isBatchMode) return;
            context.AddSceneService<PerformanceMonitor>(90)
                .Configure(context.Settings.Performance);
            context.AddSceneService<BaseGameTuning>(91)
                .Configure(context.Settings.Performance);
            context.AddClientSetting("FRAME BUDGET", "ADAPTIVE FX",
                "After sustained slow flight frames, halve scalable Boscali cosmetic budgets. " +
                "Takes effect after sampling; no mission or game restart.",
                context.Settings.Performance.Enabled, ClientPreset.Saver);
            context.AddClientSetting("BASE GAME", "LOD CAP",
                "Lower the LOD bias to at most 1.5 (game default 4) so distant models simplify sooner. " +
                "Helps a GPU-limited machine only; expect more model pop-in. " +
                "Client-local; the game's own value is restored when switched off.",
                context.Settings.Performance.LodBiasFloor, ClientPreset.LastResort);
            context.AddClientSetting("BASE GAME", "SHADOW CAP",
                "Cap shadow distance at 1000 m (game default 2000 m) to drop far shadow-cascade work. " +
                "Helps a GPU-limited machine only. " +
                "Client-local; the game's own value is restored when switched off.",
                context.Settings.Performance.ShadowDistanceCap, ClientPreset.LastResort);
            context.AddClientSetting("BASE GAME", "RENDER SCALE",
                "Render the 3D world at 80% resolution and upscale it, for about a third fewer pixels. " +
                "Helps a GPU-limited machine only; the world looks softer. " +
                "Client-local; the game's own value is restored when switched off.",
                context.Settings.Performance.RenderScaleCap, ClientPreset.LastResort);
            context.AddClientSetting("BASE GAME", "SCENE CLEANUP",
                "Unload unused assets and collect garbage on scene transitions " +
                "(menu/mission loads), never during flight. Frees memory; loads take slightly longer. Client-local.",
                context.Settings.Performance.CleanupOnSceneChange);
            context.AddSceneService<ClientSavings>(92)
                .Configure(context.Settings.Performance);
            context.AddClientSetting("FAR UNITS", "SKIP IDLE SEARCH",
                "Joined to another host: skip the turret target search your machine runs for every unit " +
                "and never uses. Nothing to see; no effect while you host or fly single player.",
                context.Settings.Performance.SkipRemoteTurretSearch, ClientPreset.Saver);
            context.AddClientSetting("FAR UNITS", "THIN FAR TRACERS",
                "Joined to another host: draw fewer of other units' bullets past 2.5 km and none past 15 km. " +
                "Visual only; hits are unchanged.",
                context.Settings.Performance.ThinDistantTracers, ClientPreset.Saver);
            context.AddClientSetting("FAR UNITS", "SLEEP FAR UNITS",
                "Stop drawing aircraft, vehicles and ships more than 15 km away. What you fly, follow or " +
                "have targeted always draws; a zoomed camera on an untargeted far unit shows nothing.",
                context.Settings.Performance.SleepDistantUnits, ClientPreset.LastResort);            context.Logger.LogInfo("Performance=client-local Boscali cosmetic budget; game simulation unchanged");
        }
    }
}
