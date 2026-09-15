using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Configuration
{
    internal sealed class CommandSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ExpandedMapUi { get; }
        public ConfigEntry<bool> FrontlinesOverlay { get; }
        public ConfigEntry<bool> FrontlineTrace { get; }
        public ConfigEntry<float> OverlayOpacity { get; }
        public ConfigEntry<int> GridCellSizeMetres { get; }
        public ConfigEntry<float> GridRefreshInterval { get; }

        public ConfigEntry<float> DeckOpacity { get; }
        public ConfigEntry<bool> DeckGrid { get; }
        public ConfigEntry<bool> CheckerboardOverlay { get; }
        public ConfigEntry<float> CheckerboardOpacity { get; }

        public ConfigEntry<bool> BackgroundImage { get; }
        public ConfigEntry<int> BackgroundImagePreset { get; }
        public ConfigEntry<string> CustomWallpaperFile { get; }
        public ConfigEntry<int> WallpaperFitMode { get; }
        public ConfigEntry<float> BackgroundImageOpacity { get; }

        public ConfigEntry<bool> MapTerrainImage { get; }
        public ConfigEntry<float> MapTerrainOpacity { get; }
        public ConfigEntry<float> MapTrayOpacity { get; }

        public ConfigEntry<bool> NewsTickerEnabled { get; }
        public ConfigEntry<float> NewsTickerSpeed { get; }

        public ConfigEntry<bool> TargetPresetWheel { get; }
        public ConfigEntry<string> TargetPresets { get; }
        public ConfigEntry<string> TargetPresetSlots { get; }
        public ConfigEntry<KeyCode> TargetPresetKey1 { get; }
        public ConfigEntry<KeyCode> TargetPresetKey2 { get; }
        public ConfigEntry<KeyCode> TargetPresetKey3 { get; }

        public CommandSettings(ConfigFile config)
        {
            ExpandedMapUi = config.Bind("Command", "ExpandedMapUi", true,
                "Use Boscali's full tactical display: left panel and log, central map, right button rail, and spawn footer.");
            Enabled = config.Bind("Command", "Enabled", true,
                "Enable the Tactical STR Panel and map tactical overlays.");

            FrontlinesOverlay = config.Bind("Command", "FrontlinesOverlay", true,
                "Render the sector control tint over the tactical map. The MAP bezel's CONTROL FIELD layer switches it in game.");

            FrontlineTrace = config.Bind("Command", "FrontlineTrace", true,
                "Draw the front line trace above the control tint. Only drawn while the control field is on; the MAP bezel's FRONT LINE layer switches it in game.");

            OverlayOpacity = config.Bind("Command", "OverlayOpacity", 0.35f,
                new ConfigDescription(
                    "Alpha opacity of the rasterized tactical map modes (0.1 = faint, 1.0 = solid).",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            GridCellSizeMetres = config.Bind("Command", "GridCellSizeMetres", 1000,
                new ConfigDescription(
                    "Advanced: tactical sector cell size in metres. 1000 matches the map's own base grid squares (the 10 km major squares are 10000). Applied when the module initializes; very large theaters coarsen it in powers of two.",
                    new AcceptableValueRange<int>(250, 10000)));

            GridRefreshInterval = config.Bind("Command", "GridRefreshInterval", 0.5f,
                new ConfigDescription(
                    "Seconds between influence grid texture updates while the map is open (0.5s = 2 Hz).",
                    new AcceptableValueRange<float>(0.2f, 2.0f)));

            DeckOpacity = config.Bind("Command", "DeckOpacity", 0.95f,
                new ConfigDescription(
                    "Opacity of the console backdrop (0.1 = see-through, 1.0 = solid). Map darkening is separate.",
                    new AcceptableValueRange<float>(0.10f, 1.0f)));

            DeckGrid = config.Bind("Command", "DeckGrid", false,
                "Legacy decoration: backdrop grid. The SET background selector chooses one decoration at a time.");

            CheckerboardOverlay = config.Bind("Command", "CheckerboardOverlay", false,
                "Render subtle tactical checkerboard overlay grid on the backdrop surface.");

            CheckerboardOpacity = config.Bind("Command", "CheckerboardOpacity", 0.08f,
                new ConfigDescription(
                    "Alpha opacity of the tactical checkerboard overlay pattern.",
                    new AcceptableValueRange<float>(0.02f, 0.40f)));

            BackgroundImage = config.Bind("Command", "BackgroundImage", false,
                "Display custom or procedural tactical wallpaper on the backdrop deck.");

            BackgroundImagePreset = config.Bind("Command", "BackgroundImagePreset", 0,
                new ConfigDescription(
                    "Wallpaper pattern preset (0: Hexagon, 1: Carbon, 2: Radar, 3: Custom file).",
                    new AcceptableValueRange<int>(0, 3)));

            CustomWallpaperFile = config.Bind("Command", "CustomWallpaperFile", "images.jpg",
                "Filename of custom background wallpaper loaded from wallpapers directory.");

            WallpaperFitMode = config.Bind("Command", "WallpaperFitMode", 0,
                new ConfigDescription(
                    "Aspect ratio fit mode for custom wallpaper (0: Cover/AspectFill, 1: Fit/Letterbox, 2: Stretch).",
                    new AcceptableValueRange<int>(0, 2)));

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

            NewsTickerEnabled = config.Bind("Command", "NewsTicker", true,
                "Display the scrolling frontline news and theater dispatch marquee above the tactical map.");

            NewsTickerSpeed = config.Bind("Command", "NewsTickerSpeed", 45f,
                new ConfigDescription(
                    "Scrolling speed of the tactical news ticker marquee in pixels per second.",
                    new AcceptableValueRange<float>(15f, 150f)));

            MapTrayOpacity = config.Bind("Command", "MapTrayOpacity", 0.15f,
                new ConfigDescription(
                    "Map darkening beneath terrain and symbols, independent of background choice (0.0 = clear, 1.0 = dark solid).",
                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            TargetPresetWheel = config.Bind("Command", "TargetPresetWheel", true,
                "Offer the assigned TGT quick-slot presets as a page in the native cockpit radial menu.");

            TargetPresets = config.Bind("Command", "TargetPresets", "",
                "Saved TGT target-filter presets, stored by the PRESETS page. Internal format; do not hand-edit.");

            TargetPresetSlots = config.Bind("Command", "TargetPresetSlots", "||",
                "The three TGT quick-slot preset names, in order. Managed by the PRESETS page.");

            TargetPresetKey1 = config.Bind("Command", "TargetPresetKey1", KeyCode.F6,
                "Apply TGT quick-slot 1 while flying. None disables the shortcut.");

            TargetPresetKey2 = config.Bind("Command", "TargetPresetKey2", KeyCode.F9,
                "Apply TGT quick-slot 2 while flying. None disables the shortcut.");

            TargetPresetKey3 = config.Bind("Command", "TargetPresetKey3", KeyCode.F10,
                "Apply TGT quick-slot 3 while flying. None disables the shortcut.");
        }
    }
}
