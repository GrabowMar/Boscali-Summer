using BepInEx.Configuration;

namespace BoscaliSummer.Features.Campaign.Configuration
{
    /// <summary>
    /// The campaign mission ships with the mod; this gate decides whether it is written into
    /// the game's user mission list at startup.
    /// </summary>
    internal sealed class CampaignSettings
    {
        public ConfigEntry<bool> Enabled { get; }

        public CampaignSettings(ConfigFile config)
        {
            const string section = "Campaign";
            Enabled = config.Bind(section, "Enabled", true,
                "Install the authored Boscali Summer campaign mission into the game's mission " +
                "list on startup. Written once per shipped revision; a mission of the same " +
                "name that Boscali did not write is never overwritten. Restart to apply.");
        }
    }
}
