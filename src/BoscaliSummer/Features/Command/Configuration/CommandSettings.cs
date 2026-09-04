using BepInEx.Configuration;

namespace BoscaliSummer.Features.Command.Configuration
{
    internal sealed class CommandSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> FrontlinesOverlay { get; }
        public ConfigEntry<float> OverlayOpacity { get; }
        public ConfigEntry<int> GridResolution { get; }
        public ConfigEntry<float> GridRefreshInterval { get; }

        public CommandSettings(ConfigFile config)
        {
            Enabled = config.Bind("Command", "Enabled", true,
                "Enable the Tactical COM Panel, map tactical overlays, and AI Battle Director.");

            FrontlinesOverlay = config.Bind("Command", "FrontlinesOverlay", true,
                "Render tactical sector control grid and dynamic contested frontline boundaries on the tactical map.");

            OverlayOpacity = config.Bind("Command", "OverlayOpacity", 0.35f,
                new ConfigDescription(
                    "Alpha opacity of the rasterized tactical map modes (0.1 = faint, 1.0 = solid).",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            GridResolution = config.Bind("Command", "GridResolution", 32,
                new ConfigDescription(
                    "Tactical sector grid dimension (32 = 32x32 sectors). Recommended: 32.",
                    new AcceptableValueRange<int>(16, 64)));

            GridRefreshInterval = config.Bind("Command", "GridRefreshInterval", 0.5f,
                new ConfigDescription(
                    "Seconds between influence grid texture updates while the map is open (0.5s = 2 Hz).",
                    new AcceptableValueRange<float>(0.2f, 2.0f)));
        }
    }
}
