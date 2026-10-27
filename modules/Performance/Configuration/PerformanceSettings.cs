using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Performance.Configuration
{
    internal sealed class PerformanceSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> LodBiasFloor { get; }
        public ConfigEntry<bool> ShadowDistanceCap { get; }
        public ConfigEntry<bool> RenderScaleCap { get; }
        public ConfigEntry<bool> CleanupOnSceneChange { get; }
        public ConfigEntry<bool> SkipRemoteTurretSearch { get; }
        public ConfigEntry<bool> ThinDistantTracers { get; }
        public ConfigEntry<bool> SleepDistantUnits { get; }

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
                "Client-local Unity tuning: render the 3D scene at most at 80% resolution and upscale it " +
                "(screen-overlay HUD stays sharp). Restored exactly when switched off. " +
                "Applies immediately; no restart required.");
            CleanupOnSceneChange = config.Bind("Performance", "CleanupOnSceneChange", false,
                "Client-local Unity tuning: unload unused assets and collect garbage on " +
                "scene transitions (menu/mission loads), never during flight. " +
                "Applies on the next scene change; no restart required.");
            SkipRemoteTurretSearch = config.Bind("Performance", "SkipRemoteTurretSearch", false,
                "When joined to someone else's game, skip the turret datalink target search the client " +
                "runs for every unit and never uses. No effect while hosting or in single player.");
            ThinDistantTracers = config.Bind("Performance", "ThinDistantTracers", false,
                "When joined to someone else's game, draw fewer of other units' bullets past 2.5 km " +
                "and none past 15 km. Visual only; hits are unchanged.");
            SleepDistantUnits = config.Bind("Performance", "SleepDistantUnits", false,
                "Stop drawing aircraft, vehicles and ships more than 15 km away (models, particles, lights). " +
                "The unit you fly, follow or have targeted always draws. A zoomed camera can show the gap.");
        }
    }
}
