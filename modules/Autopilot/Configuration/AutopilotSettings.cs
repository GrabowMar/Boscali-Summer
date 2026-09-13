using BepInEx.Configuration;

namespace BoscaliSummer.Features.Autopilot.Configuration
{
    internal sealed class AutopilotSettings
    {
        public ConfigEntry<bool> Enabled { get; }

        public AutopilotSettings(ConfigFile config)
        {
            Enabled = config.Bind("Autopilot", "Enabled", true,
                "Local ownship autopilot landing from the native radial menu (Boscali Summer > " +
                "Autopilot). Client-local: flies only your aircraft with the game's own autopilot, " +
                "never commands other aircraft, and yields to manual stick input.");
        }
    }
}
