using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Pmc.Configuration
{
    internal sealed class PmcSettings
    {
        internal ConfigEntry<string> DisplayName { get; }
        internal ConfigEntry<string> Tag { get; }
        internal ConfigEntry<string> Color { get; }

        internal PmcSettings(ConfigFile config)
        {
            const string section = "Pmc";
            DisplayName = config.Bind(section, "DisplayName", "PMC", "The third side's long name, shown on the map and in menus. Missions always call it PMC.");
            Tag = config.Bind(section, "Tag", "PMC", "The third side's short tag on the MFD.");
            Color = config.Bind(section, "Color", "0.2,0.8,0.2,1", "The third side's colour as r,g,b or r,g,b,a, each 0 to 1, with '.' decimals.");
        }
    }
}
