using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Performance.Configuration
{
    internal sealed class PerformanceSettings
    {
        public ConfigEntry<bool> Enabled { get; }

        public PerformanceSettings(ConfigFile config)
        {
            Enabled = config.Bind("Performance", "Enabled", false,
                "Adapt only Boscali-owned cosmetic effects after sustained slow flight frames. " +
                "Never changes game physics, camera distance, or graphics quality. " +
                "Applies during the current mission; no restart required.");
        }
    }
}
