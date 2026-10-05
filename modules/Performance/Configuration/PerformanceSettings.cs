using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Performance.Configuration
{
    internal sealed class PerformanceSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> LodBiasFloor { get; }
        public ConfigEntry<bool> ShadowDistanceCap { get; }
        public ConfigEntry<bool> FrameRateCap { get; }
        public ConfigEntry<bool> CleanupOnSceneChange { get; }

        public PerformanceSettings(ConfigFile config)
        {
            Enabled = config.Bind("Performance", "Enabled", false,
                "Adapt only Boscali-owned cosmetic effects after sustained slow flight frames. " +
                "Never changes game physics, camera distance, or graphics quality. " +
                "Applies during the current mission; no restart required.");
            LodBiasFloor = config.Bind("Performance", "LodBiasFloor", false,
                "Client-local Unity tuning: raise the LOD bias to at least 1.5 so distant " +
                "models drop to cheaper levels sooner. Restored exactly when switched off. " +
                "Applies immediately; no restart required.");
            ShadowDistanceCap = config.Bind("Performance", "ShadowDistanceCap", false,
                "Client-local Unity tuning: cap shadow distance at 2000 m, dropping far " +
                "shadow-cascade work. Restored exactly when switched off. " +
                "Applies immediately; no restart required.");
            FrameRateCap = config.Bind("Performance", "FrameRateCap", false,
                "Client-local Unity tuning: cap the frame rate at 60 fps for steadier " +
                "frametimes and lower thermals. Restored exactly when switched off. " +
                "Applies immediately; no restart required.");
            CleanupOnSceneChange = config.Bind("Performance", "CleanupOnSceneChange", false,
                "Client-local Unity tuning: unload unused assets and collect garbage on " +
                "scene transitions (menu/mission loads), never during flight. " +
                "Applies on the next scene change; no restart required.");
        }
    }
}
