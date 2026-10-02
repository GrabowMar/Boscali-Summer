using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Intel.Configuration
{
    /// <summary>
    /// Intel's two switches. The master switch is read once at startup: a module that was
    /// never installed cannot be switched on mid-mission. PreWarIntel is read when a mission's
    /// pre-war seeding runs, 30 s after it starts.
    /// </summary>
    internal sealed class IntelSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> PreWarIntel { get; }

        public IntelSettings(ConfigFile config)
        {
            const string section = "Intel";
            Enabled = config.Bind(section, "Enabled", true,
                "Build each faction's fog-of-war threat picture: known enemy air defence with vanilla's real " +
                "engagement cones, launch memory and scouted ground. It reads only each faction's own tracking, " +
                "patches nothing and sends nothing. The theater staff and the SA page read it and fall back to " +
                "their old behaviour without it. Read at startup.");
            PreWarIntel = config.Bind(section, "PreWarIntel", true,
                "Each faction starts the mission knowing the other factions' fixed, mission-placed air-defence " +
                "sites, labelled PRE-WAR until its own tracking confirms them. Mobile units, factory output and " +
                "garrisons are never pre-war. Off: knowledge comes strictly from live tracking. Read when a " +
                "mission starts.");
        }
    }
}
