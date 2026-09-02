using BepInEx.Configuration;

namespace BoscaliSummer.Features.Progression.Configuration
{
    internal sealed class ProgressionSettings
    {
        public ConfigEntry<bool> Enabled { get; }

        public ProgressionSettings(ConfigFile config)
        {
            Enabled = config.Bind("Progression", "Enabled", true,
                "Use vanilla player ranks as skill points for the Boscali skill tree.");
        }
    }
}
