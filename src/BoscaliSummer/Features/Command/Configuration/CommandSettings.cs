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
                "Render tactical sector control grid and dynamic contested frontline boundaries on the tactical map.");

            RadarCoverageOverlay = config.Bind("Command", "RadarCoverageOverlay", false,
                "Render hostile SAM threat envelopes and friendly radar coverage contours.");

            VisibilityOverlay = config.Bind("Command", "VisibilityOverlay", false,
                "Render airbase runways, approach cones, and strategic logistics links.");

            AiOrdersOverlay = config.Bind("Command", "AiOrdersOverlay", false,
                "Draw friendly and detected AI operational flight vectors (CAP, Strike, CAS, RTB) on the main map.");

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
