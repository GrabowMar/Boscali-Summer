using System;
using BoscaliSummer.Modules.Performance.Runtime;
using BoscaliSummer.Core.Contracts;
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
            typeof(RemoteTurretSearchPatch), typeof(SleepingTurretPatch), typeof(DistantTracerPatch),
            typeof(ReliefAmbientOcclusionPatch)
        };

        public void Install(ModuleContext context)
        {
            if (Application.isBatchMode) return;
            context.AddSceneService<PerformanceMonitor>(90)
                .Configure(context.Settings.Performance);
            context.AddSceneService<BaseGameTuning>(91)
                .Configure(context.Settings.Performance);
            var renderTuning = context.AddSceneService<RenderTuning>(93);
            renderTuning.Configure(context.Settings.Performance);
            context.AddService<IRenderTuning>(renderTuning);
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
            ClientSettingToggle scale = context.AddClientSetting("BASE GAME", "RENDER SCALE",
                "Render the 3D world at the RESOLUTION below and upscale it. " +
                "Helps a GPU-limited machine only; the world looks softer. " +
                "Client-local; the game's own value is restored when switched off.",
                context.Settings.Performance.RenderScaleCap, ClientPreset.LastResort);
            context.AddClientLevel("BASE GAME", "RESOLUTION", "3D world resolution under RENDER SCALE. Lower is faster and softer.",
                context.Settings.Performance.RenderScale, .5f, 1f, .05f, requires: scale);
            context.AddClientSetting("BASE GAME", "MAP AO SKIP",
                "Skip unused ambient occlusion on the relief map's fallback camera. " +
                "The normal map path already avoids this work. World and sensor shading stay unchanged.",
                context.Settings.Performance.SkipReliefAmbientOcclusion);
            context.AddClientSetting("BASE GAME", "CHEAPER LIGHT STREAKS",
                "Reduce rendering work for CINEMATIC light glow when supported. " +
                "Only helps while cinematic light streaks are enabled; keeps the original path as fallback.",
                context.Settings.Performance.OptimizeCinematicStreaks);
            ClientSettingToggle sharpen = context.AddClientSetting("BASE GAME", "INDEPENDENT SHARPEN",
                "Sharpen the picture with native FidelityFX RCAS without enabling COLOUR GRADE. " +
                "Can help a reduced render scale look clearer; adds GPU work. " +
                "While on, the strength below takes priority over grade sharpening.",
                context.Settings.Performance.IndependentSharpening);
            context.AddClientLevel("BASE GAME", "SHARPEN STRENGTH",
                "How strongly independent sharpening restores detail. High values can emphasize noise and halos.",
                context.Settings.Performance.IndependentSharpenStrength, 0f, 1f, .1f, requires: sharpen);
            context.AddClientSetting("BASE GAME", "SCENE CLEANUP",
                "Unload unused assets and collect garbage on scene transitions " +
                "(menu/mission loads), never during flight. Frees memory; loads take slightly longer. Client-local.",
                context.Settings.Performance.CleanupOnSceneChange);
            context.AddClientSetting("BASE GAME", "LIGHTER LOGS",
                "Skip stack traces for ordinary Unity logs and warnings. Messages still appear; " +
                "errors keep their diagnostics. Helps only when logs are frequent. Restores on disable.",
                context.Settings.Performance.QuietUnityLogs);
            ClientSettingToggle streaming = context.AddClientSetting("BASE GAME", "TEXTURE STREAMING",
                "Stream textures that support it to reduce memory pressure. Can cause texture pop-in " +
                "and extra CPU work. No benefit without streamable textures. Restores on disable.",
                context.Settings.Performance.TextureStreaming);
            context.AddClientLevel("BASE GAME", "TEXTURE BUDGET", "Texture memory budget while streaming. " +
                "Other textures can exceed it; a small budget can blur textures.",
                context.Settings.Performance.TextureStreamingBudgetMb, 128f, 8192f, 128f,
                value => value.ToString("F0") + " MB", requires: streaming);
            context.AddClientSetting("BASE GAME", "UPLOAD BUFFER",
                "Reserve at least 64 MB for texture and mesh uploads (configurable in the config file). " +
                "Uses more RAM to avoid buffer resize work during loads. Restores on disable.",
                context.Settings.Performance.PreallocateUploadBuffer);
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
                context.Settings.Performance.SleepDistantUnits, ClientPreset.LastResort);
            context.Logger.LogInfo("Performance=client-local Boscali cosmetic budget and opt-in engine tuning; game simulation unchanged");
        }
    }
}
