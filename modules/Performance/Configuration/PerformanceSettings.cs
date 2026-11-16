using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Performance.Configuration
{
    internal sealed class PerformanceSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> LodBiasFloor { get; }
        public ConfigEntry<bool> ShadowDistanceCap { get; }
        public ConfigEntry<bool> RenderScaleCap { get; }
        public ConfigEntry<float> RenderScale { get; }
        public ConfigEntry<bool> CleanupOnSceneChange { get; }
        public ConfigEntry<bool> QuietUnityLogs { get; }
        public ConfigEntry<bool> TextureStreaming { get; }
        public ConfigEntry<float> TextureStreamingBudgetMb { get; }
        public ConfigEntry<bool> PreallocateUploadBuffer { get; }
        public ConfigEntry<float> UploadBufferMb { get; }
        public ConfigEntry<bool> SkipRemoteTurretSearch { get; }
        public ConfigEntry<bool> ThinDistantTracers { get; }
        public ConfigEntry<bool> SleepDistantUnits { get; }
        public ConfigEntry<bool> SkipReliefAmbientOcclusion { get; }
        public ConfigEntry<bool> OptimizeCinematicStreaks { get; }
        public ConfigEntry<bool> IndependentSharpening { get; }
        public ConfigEntry<float> IndependentSharpenStrength { get; }

        public PerformanceSettings(ConfigFile config)
        {
            Enabled = config.Bind("Performance", "Enabled", false,
                "Adapt only Boscali-owned cosmetic effects after sustained slow flight frames. " +
                "Never changes game physics, camera distance, or graphics quality. " +
                "Applies during the current mission; no restart required.");
            LodBiasFloor = config.Bind("Performance", "LodBiasFloor", false,
                "Client-local Unity tuning: lower the LOD bias to at most 1.5 (game default 4) so distant " +
                "models drop to cheaper levels sooner. Restored exactly when switched off. " +
                "Applies immediately; no restart required.");
            ShadowDistanceCap = config.Bind("Performance", "ShadowDistanceCap", false,
                "Client-local Unity tuning: cap shadow distance at 1000 m (game default 2000 m), dropping far " +
                "shadow-cascade work. Restored exactly when switched off. " +
                "Applies immediately; no restart required.");
            RenderScaleCap = config.Bind("Performance", "RenderScaleCap", false,
                "Client-local Unity tuning: render the 3D scene at most at RenderScale resolution and upscale it " +
                "(screen-overlay HUD stays sharp). Restored exactly when switched off. " +
                "Applies immediately; no restart required.");
            RenderScale = config.Bind("Performance", "RenderScale", 0.8f,
                new ConfigDescription("Share of the screen resolution the 3D scene renders at while RenderScaleCap " +
                    "is on (0.8 = 80%). Lower is faster and softer. Applies immediately.",
                    new AcceptableValueRange<float>(0.5f, 1f)));
            CleanupOnSceneChange = config.Bind("Performance", "CleanupOnSceneChange", false,
                "Client-local Unity tuning: unload unused assets and collect garbage on " +
                "scene transitions (menu/mission loads), never during flight. " +
                "Applies on the next scene change; no restart required.");
            QuietUnityLogs = config.Bind("Performance", "QuietUnityLogs", false,
                "Skip stack trace generation for ordinary Unity logs and warnings. Messages still appear; " +
                "error, exception and assertion diagnostics are unchanged. Restored when switched off.");
            TextureStreaming = config.Bind("Performance", "TextureStreaming", false,
                "Enable Unity mipmap streaming for textures built with streaming support, using all cameras. " +
                "Can reduce texture memory pressure at the cost of texture pop-in and CPU work; " +
                "has no benefit when the scene has no streamable textures. Restored when switched off.");
            TextureStreamingBudgetMb = config.Bind("Performance", "TextureStreamingBudgetMb", 1024f,
                new ConfigDescription("Texture memory budget in MB while TextureStreaming is on. " +
                    "Non-streaming textures can exceed this budget. Lower budgets can blur textures.",
                    new AcceptableValueRange<float>(128f, 8192f)));
            PreallocateUploadBuffer = config.Bind("Performance", "PreallocateUploadBuffer", false,
                "Reserve a larger Unity async texture/mesh upload buffer to avoid resize work while loading. " +
                "Uses additional system memory; does not change upload time per frame. Restored when switched off.");
            UploadBufferMb = config.Bind("Performance", "UploadBufferMb", 64f,
                new ConfigDescription("Minimum async upload buffer size in MB while PreallocateUploadBuffer is on. " +
                    "An already larger game buffer is preserved. This targets loading hitches, not steady FPS.",
                    new AcceptableValueRange<float>(16f, 256f)));
            SkipRemoteTurretSearch = config.Bind("Performance", "SkipRemoteTurretSearch", false,
                "When joined to someone else's game, skip the turret datalink target search the client " +
                "runs for every unit and never uses. No effect while hosting or in single player.");
            ThinDistantTracers = config.Bind("Performance", "ThinDistantTracers", false,
                "When joined to someone else's game, draw fewer of other units' bullets past 2.5 km " +
                "and none past 15 km. Visual only; hits are unchanged.");
            SleepDistantUnits = config.Bind("Performance", "SleepDistantUnits", false,
                "Stop drawing aircraft, vehicles and ships more than 15 km away (models, particles, lights). " +
                "The unit you fly, follow or have targeted always draws. A zoomed camera can show the gap.");
            SkipReliefAmbientOcclusion = config.Bind("Performance", "SkipReliefAmbientOcclusion", false,
                "Skip unused native ambient occlusion on Boscali's unlit relief-map camera fallback. " +
                "The normal direct-draw map already avoids it. World and sensor cameras keep their AO.");
            OptimizeCinematicStreaks = config.Bind("Performance", "OptimizeCinematicStreaks", false,
                "Use the cheaper cinematic light-streak path when supported. " +
                "Only affects CINEMATIC light glow; it does not enable that effect.");
            IndependentSharpening = config.Bind("Performance", "IndependentSharpening", false,
                "Use native FidelityFX RCAS sharpening without enabling COLOUR GRADE. " +
                "Uses IndependentSharpenStrength while on, including when grade sharpening is also on. " +
                "A quality option with a GPU cost, not a promised FPS improvement. Restores on disable.");
            IndependentSharpenStrength = config.Bind("Performance", "IndependentSharpenStrength", 0.4f,
                new ConfigDescription("Sharpening strength while IndependentSharpening is on. " +
                    "Higher values can emphasize noise and halos; 0.4 is a restrained starting point.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }
    }
}
