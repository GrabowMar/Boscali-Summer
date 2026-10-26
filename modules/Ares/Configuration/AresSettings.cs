using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Ares.Configuration
{
    internal sealed class AresSettings
    {
        public readonly ConfigEntry<bool> Enabled;

        public AresSettings(ConfigFile config)
        {
            Enabled = config.Bind("Ares", "Enabled", true,
                "ARES game-master console on the MISC screen, locked behind the cheating warning: spawn and manage units, god powers and cheats. " +
                "Host only. Applies at the next launch.");
        }
    }
}
