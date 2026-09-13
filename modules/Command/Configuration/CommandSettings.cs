using BepInEx.Configuration;

namespace BoscaliSummer.Features.Command.Configuration
{
    internal sealed class CommandSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ExpandedMapUi { get; }
        public ConfigEntry<bool> FrontlinesOverlay { get; }
        public ConfigEntry<float> OverlayOpacity { get; }
        public ConfigEntry<int> GridResolution { get; }
        public ConfigEntry<float> GridRefreshInterval { get; }

        public ConfigEntry<float> DeckOpacity { get; }
        public ConfigEntry<bool> DeckGrid { get; }
        public ConfigEntry<bool> CheckerboardOverlay { get; }
        public ConfigEntry<float> CheckerboardOpacity { get; }

        public ConfigEntry<bool> BackgroundImage { get; }
        public ConfigEntry<int> BackgroundImagePreset { get; }
        public ConfigEntry<float> BackgroundImageOpacity { get; }

        public ConfigEntry<bool> MapTerrainImage { get; }
        public ConfigEntry<float> MapTerrainOpacity { get; }

        public CommandSettings(ConfigFile config)
        {
            ExpandedMapUi = config.Bind("Command", "ExpandedMapUi", true,
                "Use Boscali's full tactical display: left panel and log, central map, right button rail, and spawn footer.");
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

            DeckOpacity = config.Bind("Command", "DeckOpacity", 0.95f,
                new ConfigDescription(
                    "Alpha opacity of the tactical map console backdrop and map tray (0.1 = see-through, 1.0 = solid).",
                    new AcceptableValueRange<float>(0.10f, 1.0f)));

            DeckGrid = config.Bind("Command", "DeckGrid", true,
                "Render datum grid lines and coordinate ticks across the tactical backdrop.");

            CheckerboardOverlay = config.Bind("Command", "CheckerboardOverlay", false,
                "Render subtle tactical checkerboard overlay grid on the backdrop surface.");

            CheckerboardOpacity = config.Bind("Command", "CheckerboardOpacity", 0.08f,
                new ConfigDescription(
                    "Alpha opacity of the tactical checkerboard overlay pattern.",
                    new AcceptableValueRange<float>(0.02f, 0.40f)));

            BackgroundImage = config.Bind("Command", "BackgroundImage", true,
                "Display custom or procedural tactical wallpaper on the backdrop deck.");

            BackgroundImagePreset = config.Bind("Command", "BackgroundImagePreset", 0,
                new ConfigDescription(
                    "Wallpaper pattern preset (0: Hexagon, 1: Carbon, 2: Radar, 3: Custom file).",
                    new AcceptableValueRange<int>(0, 3)));

            BackgroundImageOpacity = config.Bind("Command", "BackgroundImageOpacity", 0.25f,
                new ConfigDescription(
                    "Alpha opacity of the backdrop background image or wallpaper.",
                    new AcceptableValueRange<float>(0.05f, 1.0f)));

            MapTerrainImage = config.Bind("Command", "MapTerrainImage", true,
                "Display satellite terrain map image under tactical overlays.");

            MapTerrainOpacity = config.Bind("Command", "MapTerrainOpacity", 1.0f,
                new ConfigDescription(
                    "Alpha opacity of the satellite terrain map image.",
                    new AcceptableValueRange<float>(0.10f, 1.0f)));
        }
    }
}
