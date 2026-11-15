using BepInEx.Configuration;

namespace BoscaliSummer.Modules.SeaStructures.Configuration
{
    /// <summary>Sea structures: how many the host builds when a mission starts.</summary>
    internal sealed class SeaStructureSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<int> OilRigs { get; }
        public ConfigEntry<int> SeaForts { get; }

        public SeaStructureSettings(ConfigFile config)
        {
            const string S = "SeaStructures";
            Enabled = config.Bind(S, "Enabled", true,
                "Install sea structures at startup: armed oil platforms and sea fortresses off the coast, built by the host when a mission starts.");
            OilRigs = config.Bind(S, "OilRigs", 2, new ConfigDescription(
                "Oil platforms the host builds at mission start, in open water 8-16 km off held airbases. 0 builds none.", new AcceptableValueRange<int>(0, 6)));
            SeaForts = config.Bind(S, "SeaForts", 2, new ConfigDescription(
                "Sea fortresses the host builds at mission start, in open water 3.5-9 km off held airbases. 0 builds none.", new AcceptableValueRange<int>(0, 6)));
        }
    }
}
