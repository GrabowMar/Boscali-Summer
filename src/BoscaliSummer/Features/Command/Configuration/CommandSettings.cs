using BepInEx.Configuration;

namespace BoscaliSummer.Features.Command.Configuration
{
    internal sealed class CommandSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> FrontlinesOverlay { get; }
        public ConfigEntry<bool> RadarCoverageOverlay { get; }
        public ConfigEntry<bool> VisibilityOverlay { get; }
        public ConfigEntry<bool> AiOrdersOverlay { get; }
        public ConfigEntry<float> OverlayOpacity { get; }
        public ConfigEntry<int> GridResolution { get; }
        public ConfigEntry<float> GridRefreshInterval { get; }
        public ConfigEntry<float> VectorRefreshInterval { get; }
        public ConfigEntry<int> MaxOrderVectors { get; }

        public CommandSettings(ConfigFile config)
        {
            Enabled = config.Bind("Command", "Enabled", true,
                "Enable the Tactical COM Panel, map tactical overlays, and AI Battle Director.");

            FrontlinesOverlay = config.Bind("Command", "FrontlinesOverlay", true,
                "Render dynamic area-of-control and contested frontline boundaries on the tactical map.");

            RadarCoverageOverlay = config.Bind("Command", "RadarCoverageOverlay", true,
                "Render friendly radar networks and detected hostile SAM/radar coverage bubbles.");

            VisibilityOverlay = config.Bind("Command", "VisibilityOverlay", true,
                "Render visual and sensor surveillance coverage and recon staleness.");

            AiOrdersOverlay = config.Bind("Command", "AiOrdersOverlay", true,
                "Draw friendly and detected AI operational order vectors (CAP, Strike, CAS, RTB) on the main map.");

            OverlayOpacity = config.Bind("Command", "OverlayOpacity", 0.45f,
                new ConfigDescription(
                    "Alpha opacity of the rasterized tactical map modes.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            GridResolution = config.Bind("Command", "GridResolution", 64,
                new ConfigDescription(
                    "Influence grid dimension (64 = 64x64 cells). Keep at 64 for optimal performance.",
                    new AcceptableValueRange<int>(32, 128)));

            GridRefreshInterval = config.Bind("Command", "GridRefreshInterval", 0.5f,
                new ConfigDescription(
                    "Seconds between influence grid texture updates while the map is open (0.5s = 2 Hz).",
                    new AcceptableValueRange<float>(0.2f, 2.0f)));

            VectorRefreshInterval = config.Bind("Command", "VectorRefreshInterval", 0.1f,
                new ConfigDescription(
                    "Seconds between AI flight order vector updates while the map is open (0.1s = 10 Hz).",
                    new AcceptableValueRange<float>(0.05f, 0.5f)));

            MaxOrderVectors = config.Bind("Command", "MaxOrderVectors", 48,
                new ConfigDescription(
                    "Maximum pooled vector visual objects active simultaneously.",
                    new AcceptableValueRange<int>(16, 96)));
        }
    }
}
