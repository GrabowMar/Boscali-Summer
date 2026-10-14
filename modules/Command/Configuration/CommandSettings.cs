using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Configuration
{
    /// <summary>What the corner map is: a small terrain map hologram that the held map key grows (HOLO), or the game's own corner map (CLASSIC; the held map key still raises the hologram).</summary>
    internal enum TacMapMode { Holo, Classic }

    internal sealed class CommandSettings : Core.Contracts.IVisorTerrainSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ExpandedMapUi { get; }
        public ConfigEntry<bool> FrontlinesOverlay { get; }
        public ConfigEntry<bool> FrontlineTrace { get; }
        public ConfigEntry<bool> StrOverlayFronts { get; }
        public ConfigEntry<bool> StrOverlayOperation { get; }
        public ConfigEntry<bool> StrOverlaySorties { get; }
        public ConfigEntry<bool> ThreatHeat { get; }
        public ConfigEntry<bool> MapRoads { get; }
        public ConfigEntry<float> OverlayOpacity { get; }
        public ConfigEntry<int> GridCellSizeMetres { get; }
        public ConfigEntry<float> GridRefreshInterval { get; }

        public ConfigEntry<float> DeckOpacity { get; }
        public ConfigEntry<bool> DeckGrid { get; }
        public ConfigEntry<bool> CheckerboardOverlay { get; }

        public ConfigEntry<bool> BackgroundImage { get; }
        public ConfigEntry<int> BackgroundImagePreset { get; }
        public ConfigEntry<string> CustomWallpaperFile { get; }
        public ConfigEntry<int> WallpaperFitMode { get; }
        public ConfigEntry<float> BackgroundImageOpacity { get; }

        public ConfigEntry<bool> MapTerrainImage { get; }
        public ConfigEntry<bool> MapIndustry { get; }
        public ConfigEntry<bool> MapSupplyRoutes { get; }
        public ConfigEntry<bool> MapRelief3D { get; }
        public ConfigEntry<bool> MapSymbology { get; }
        public ConfigEntry<bool> MapMinis3D { get; }
        public ConfigEntry<bool> MapLeftDragBox { get; }
        public ConfigEntry<bool> MapClickAdds { get; }
        public ConfigEntry<MapBoxSide> MapBoxSide { get; }
        public ConfigEntry<bool> MapRightDragTurns { get; }
        public ConfigEntry<bool> MapDoubleClickZoom { get; }
        public ConfigEntry<float> MapTurnSensitivity { get; }
        public ConfigEntry<float> MapZoomStep { get; }
        public ConfigEntry<float> MapTerrainOpacity { get; }
        public ConfigEntry<float> MapTrayOpacity { get; }

        public ConfigEntry<bool> NewsTickerEnabled { get; }
        public ConfigEntry<float> NewsTickerSpeed { get; }
        public ConfigEntry<float> UiSoundVolume { get; }

        public ConfigEntry<bool> TargetPresetWheel { get; }
        public ConfigEntry<string> TargetPresets { get; }
        public ConfigEntry<string> TargetPresetSlots { get; }
        public ConfigEntry<KeyCode> TargetPresetKey1 { get; }
        public ConfigEntry<KeyCode> TargetPresetKey2 { get; }
        public ConfigEntry<KeyCode> TargetPresetKey3 { get; }
        public ConfigEntry<bool> TargetWeaponOnly { get; }
        public ConfigEntry<int> TargetRangeKm { get; }

        public ConfigEntry<bool> DisplayEffects { get; }
        public ConfigEntry<float> DisplayGlass { get; }
        public ConfigEntry<bool> DisplayAutoLight { get; }
        public ConfigEntry<float> DisplayScanlines { get; }
        public ConfigEntry<float> DisplayVignette { get; }
        public ConfigEntry<int> DisplayTint { get; }
        public ConfigEntry<float> DisplayTintStrength { get; }
        public ConfigEntry<AvThemeId> AvionicsTheme { get; }
        public ConfigEntry<AvFxTier> AvionicsFxTier { get; }
        public ConfigEntry<bool> AvionicsBlurBehind { get; }
        public ConfigEntry<bool> AvionicsReducedMotion { get; }
        public ConfigEntry<float> DisplayLife { get; }
        public ConfigEntry<float> TerrainGhost { get; }
        public ConfigEntry<TacMapMode> TacMap { get; }
        public ConfigEntry<int> TacMapRangeKm { get; }
        public ConfigEntry<bool> HoloMap { get; }
        public ConfigEntry<int> HoloRangeKm { get; }
        /// <summary>Internal marker: 1 once the saved ranges have been moved from the old disc's options to the tile's.</summary>
        public ConfigEntry<int> HoloTileRanges { get; }
        public ConfigEntry<bool> HoloNorthUp { get; }
        public ConfigEntry<float> AvionicsCustomKeyHue { get; }
        public ConfigEntry<float> AvionicsCustomGlassHue { get; }
        public ConfigEntry<float> AvionicsCustomGlassTint { get; }

        bool Core.Contracts.IVisorTerrainSettings.HoloMap { get => HoloMap.Value; set => HoloMap.Value = value; }
        float Core.Contracts.IVisorTerrainSettings.TerrainGhost { get => TerrainGhost.Value; set => TerrainGhost.Value = value; }

        /// <summary>
        /// A whole number saved in the config file for an entry that has not been bound yet (0 when there is none): BepInEx keeps what it read in its (internal) orphan table until the entry is bound, so it is
        /// read by reflection, and a BepInEx without that table simply gives 0 (no migration).
        /// </summary>
        private static int SavedInt(ConfigFile config, string key)
        {
            var orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(config, null) as Dictionary<ConfigDefinition, string>;
            return orphans != null && orphans.TryGetValue(new ConfigDefinition("Command", key), out string text) && int.TryParse(text, out int value) ? value : 0;
        }

        public CommandSettings(ConfigFile config)
        {
            DisplayLife = config.Bind("Command", "DisplayLife", 0.6f,
                new ConfigDescription("Live signal on the maximized MFD: the power-on raster, the slow refresh sweep and harder jam and hit break-up. 0 keeps the display still.", new AcceptableValueRange<float>(0f, 1f)));
            TerrainGhost = config.Bind("Command", "TerrainGhost", 0f,
                new ConfigDescription("Visor terrain ghost: a very faint wire relief of the real ground around and below your aircraft, drawn over the cockpit view in the HUD's colours. Ground that can hit you glows amber. 0 turns it off, 1 is clearly visible. Needs a map with baked terrain. Applies now; no restart.", new AcceptableValueRange<float>(0f, 1f)));
            TacMap = config.Bind("Command", "TacMapMode", TacMapMode.Classic,
                new ConfigDescription("TAC MAP, the corner map at the bottom left of the HUD. Classic (default): the game's own corner map. Holo: an optional terrain map with your view wedge, visible contacts and scale bar. Needs HOLO MAP on and a map with baked terrain, else the game's corner map stays. Applies now; no restart."));
            // A range saved before the tile (2026-10-13) is the old disc's option: read it before the binds take it from the orphan table (and clamp it to the new list).
            int savedMini = SavedInt(config, "TacMapRangeKm"), savedHolo = SavedInt(config, "HoloRangeKm");
            TacMapRangeKm = config.Bind("Command", "TacMapRangeKm", 20,
                new ConfigDescription("TAC MAP: how far the small terrain map reaches from your aircraft to its side, in kilometres: 10, 20 or 40. Applies now; no restart.", new AcceptableValueList<int>(10, 20, 40)));
            HoloMap = config.Bind("Command", "HoloMap", false,
                new ConfigDescription("Enable holographic maps (off by default). Hold the map key for the big terrain map; tap for the full map. Off restores the classic corner map and opens the full map on key press, regardless of TacMapMode. Needs baked terrain. Applies now; no restart."));
            HoloRangeKm = config.Bind("Command", "HoloRangeKm", 40,
                new ConfigDescription("Holo map: how far the big terrain map reaches from your aircraft to its side, in kilometres: 20, 40 or 80. Applies now; no restart.", new AcceptableValueList<int>(20, 40, 80)));
            HoloNorthUp = config.Bind("Command", "HoloNorthUp", false,
                new ConfigDescription("Holo map: keep north at the top of the hologram. Off turns it so your heading is up. Applies now; no restart."));
            HoloTileRanges = config.Bind("Command", "HoloTileRanges", 0,
                new ConfigDescription("Internal marker, leave as it is: 1 once the saved TacMapRangeKm and HoloRangeKm have been doubled from the old round hologram's options (5, 10, 20 and 10, 20, 40 km) to the terrain tile's (10, 20, 40 and 20, 40, 80 km).", new AcceptableValueRange<int>(0, 1)));
            if (HoloTileRanges.Value < 1)
            {
                if (savedMini > 0) TacMapRangeKm.Value = HoloPresentation.MigrateMiniRange(savedMini);
                if (savedHolo > 0) HoloRangeKm.Value = HoloPresentation.MigrateHoloRange(savedHolo);
                HoloTileRanges.Value = 1;
            }
            // 0.2.4 turned the terrain ghost and holo maps off by default; a cfg saved by 0.2.3 still holds the old on values.
            ConfigEntry<int> quietDefaults = config.Bind("Command", "QuietVisorDefaults", 0,
                new ConfigDescription("Internal marker, leave as it is: 1 once a config saved before 0.2.5 has had TerrainGhost, HoloMap and TacMapMode set back to their off defaults.", new AcceptableValueRange<int>(0, 1)));
            if (quietDefaults.Value < 1)
            {
                TerrainGhost.Value = 0f;
                HoloMap.Value = false;
                TacMap.Value = TacMapMode.Classic;
                quietDefaults.Value = 1;
            }
            AvionicsCustomKeyHue = config.Bind("Avionics", "CustomKeyHue", 190f,
                new ConfigDescription("Custom scheme: hue of keys and selection, in degrees.", new AcceptableValueRange<float>(0f, 360f)));
            AvionicsCustomGlassHue = config.Bind("Avionics", "CustomGlassHue", 200f,
                new ConfigDescription("Custom scheme: hue of the panel glass, in degrees.", new AcceptableValueRange<float>(0f, 360f)));
            AvionicsCustomGlassTint = config.Bind("Avionics", "CustomGlassTint", 0.6f,
                new ConfigDescription("Custom scheme: how much of its hue the panel glass carries (0 = neutral grey).", new AcceptableValueRange<float>(0f, 1f)));
            DisplayEffects = config.Bind("Command", "DisplayEffects", true,
                "Enable client-local MFD glass and display overlays. OFF gives a clean display.");
            DisplayAutoLight = config.Bind("Command", "DisplayAutoLight", true,
                "Adjust glass reflection strength to ambient lighting.");
            DisplayGlass = config.Bind("Command", "DisplayGlass", 0.35f,
                new ConfigDescription("MFD glass reflection strength.", new AcceptableValueRange<float>(0f, 1f)));
            DisplayScanlines = config.Bind("Command", "DisplayScanlines", 0f,
                new ConfigDescription("Static CRT scanline strength over the maximized MFD; no flicker.", new AcceptableValueRange<float>(0f, 1f)));
            DisplayVignette = config.Bind("Command", "DisplayVignette", 0f,
                new ConfigDescription("Soft edge shading over the maximized MFD.", new AcceptableValueRange<float>(0f, 1f)));
            DisplayTint = config.Bind("Command", "DisplayTint", 0,
                new ConfigDescription("MFD color wash: 0 Neutral, 1 Green, 2 Amber, 3 Ice, 4 Rose.", new AcceptableValueRange<int>(0, 4)));
            DisplayTintStrength = config.Bind("Command", "DisplayTintStrength", 0.25f,
                new ConfigDescription("Color wash strength; limited to preserve symbols and warning colors.", new AcceptableValueRange<float>(0f, 1f)));
            AvionicsTheme = config.Bind("Avionics", "Theme", AvThemeId.Portal,
                "Panel color theme: Portal, Steel, Ace, Phosphor, FieldOps (FIELD OPS), Amber (AMBER CRT), Glass (GLASS MFD), NightOps (NIGHT OPS), Custom (built from the three Custom* keys). Unknown saved names fall back to Portal.");
            AvionicsFxTier = config.Bind("Avionics", "FxTier", AvFxTier.Full,
                "Panel effects: Off (none), Lite (glass and static glow), Full (shine, scan-in, alerts, EMP glitch).");
            AvionicsBlurBehind = config.Bind("Avionics", "BlurBehind", false,
                "Blur the world behind floating windows (up to ~0.4 ms GPU; frosted glass when off).");
            AvionicsReducedMotion = config.Bind("Avionics", "ReducedMotion", false,
                "Snap every panel animation to its end state.");

            ExpandedMapUi = config.Bind("Command", "ExpandedMapUi", true,
                "Use Boscali's full tactical display: left panel and log, central map, right button rail, and spawn footer.");
            Enabled = config.Bind("Command", "Enabled", true,
                "Enable the Tactical STR Panel and map tactical overlays.");

            FrontlinesOverlay = config.Bind("Command", "FrontlinesOverlay", true,
                "Render the sector control tint over the tactical map. The MAP bezel's CONTROL FIELD layer switches it in game.");

            FrontlineTrace = config.Bind("Command", "FrontlineTrace", true,
                "Draw the front line trace above the control tint. Only drawn while the control field is on; the MAP bezel's FRONT LINE layer switches it in game.");

            StrOverlayFronts = config.Bind("Command", "StrOverlayFronts", true,
                "STR layer on the expanded map: platform badges with lane bars and the proposal tags. The layer plate's FRONTS switch changes it in game.");

            StrOverlayOperation = config.Bind("Command", "StrOverlayOperation", true,
                "STR layer on the expanded map: the main effort's ring, axis and ground groups, and the reinforcement route once a wave desk reports a depot (none does in this build). The layer plate's OPERATION switch changes it in game.");

            StrOverlaySorties = config.Bind("Command", "StrOverlaySorties", true,
                "STR layer on the expanded map: the sorties tied to the operation, and enemy command posts (this build ties no sortie and reports no post yet). The layer plate's SORTIES switch changes it in game.");

            ThreatHeat = config.Bind("Command", "ThreatHeat", true,
                "On the MAP relief, the hovered unit's sensor (solid) and weapon (dashed) ranges drawn on the terrain in place of the stock flat circles. The MAP bezel's HOVER RANGES layer switches it in game.");

            MapRoads = config.Bind("Command", "MapRoads", false,
                "On the MAP relief, the game's road network drawn as amber lines on the terrain. The MAP bezel's ROADS layer switches it in game.");

            OverlayOpacity = config.Bind("Command", "OverlayOpacity", 0.35f,
                new ConfigDescription(
                    "Alpha opacity of the rasterized tactical map modes (0.1 = faint, 1.0 = solid).",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            GridCellSizeMetres = config.Bind("Command", "GridCellSizeMetres", 1000,
                new ConfigDescription(
                    "Advanced: tactical sector cell size in metres. 1000 matches the map's own base grid squares (the 10 km major squares are 10000). Applied when the module initializes; very large theaters coarsen it in powers of two.",
                    new AcceptableValueRange<int>(250, 10000)));

            GridRefreshInterval = config.Bind("Command", "GridRefreshInterval", 0.75f,
                new ConfigDescription(
                    "Seconds between influence grid texture updates while the map is open (0.5s = 2 Hz).",
                    new AcceptableValueRange<float>(0.2f, 2.0f)));

            DeckOpacity = config.Bind("Command", "DeckOpacity", 0.95f,
                new ConfigDescription(
                    "Opacity of the console backdrop (0.1 = see-through, 1.0 = solid). Map darkening is separate.",
                    new AcceptableValueRange<float>(0.10f, 1.0f)));

            DeckGrid = config.Bind("Command", "DeckGrid", false,
                "Retired backdrop grid flag, retained for config compatibility. Renders as matte.");

            CheckerboardOverlay = config.Bind("Command", "CheckerboardOverlay", false,
                "Retired checkerboard flag, retained for config compatibility. Renders as matte.");

            BackgroundImage = config.Bind("Command", "BackgroundImage", false,
                "Display a local custom wallpaper when BackgroundImagePreset is 3.");

            BackgroundImagePreset = config.Bind("Command", "BackgroundImagePreset", 0,
                new ConfigDescription(
                    "Wallpaper source (3: Custom file; retired presets 0-2 render the matte instrument surface).",
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

            MapIndustry = config.Bind("Command", "MapIndustry", true,
                "On the expanded map, mark the factories (square), depots (diamond) and ammo dumps (triangle) your side knows of, in side colours. The MAP bezel's INDUSTRY layer switches it in game.");

            MapSupplyRoutes = config.Bind("Command", "MapSupplyRoutes", false,
                "On the expanded map, a dashed line from each friendly factory, depot and ammo dump to the nearest point of the front, and a chevron on every known supply truck. The MAP bezel's SUPPLY ROUTES layer switches it in game.");

            MapRelief3D = config.Bind("Command", "MapRelief3D", true,
                "Show a tilted tactical model from baked game terrain when the expanded map is open. The native map remains available when terrain data is missing or this is off.");

            MapSymbology = config.Bind("Command", "MapSymbology", true,
                "Draw expanded-map contacts as frameless solid symbols, one silhouette per platform class, tinted by side. Off keeps the game's bare icons.");

            MapMinis3D = config.Bind("Command", "MapMinis3D", true,
                "On the 3D relief map, stand a small low-poly model on the terrain for each contact instead of its flat symbol. All of them are one mesh and one draw call. Needs MapSymbology and MapRelief3D; the flat map always uses symbols.");

            MapLeftDragBox = config.Bind("Command", "MapLeftDragBox", false,
                "Expanded map: a plain left drag draws a selection box and Shift or Ctrl + drag pans. Off (default) is the other way round. The DRAG tool on the map corner switches it.");
            MapClickAdds = config.Bind("Command", "MapClickAdds", true,
                "Expanded map: a click on a contact adds it to the selection (or drops it if already in). Off makes a click replace the selection; Shift or Ctrl + click still adds. The CLICK tool on the map corner switches it.");
            MapBoxSide = config.Bind("Command", "MapBoxSide", Domain.MapBoxSide.All,
                "Expanded map: which contacts a selection box picks up - All, Hostile or Friendly. The BOX tool on the map corner cycles it.");
            MapRightDragTurns = config.Bind("Command", "MapRightDragTurns", true,
                "3D relief map: holding the right mouse button and dragging turns and tilts the view. A right click without movement opens the map menu either way.");
            MapDoubleClickZoom = config.Bind("Command", "MapDoubleClickZoom", true,
                "3D relief map: a double click on the ground centres and zooms in there.");
            MapTurnSensitivity = config.Bind("Command", "MapTurnSensitivity", 1f,
                new ConfigDescription("3D relief map: how fast a held-button drag turns and tilts the view.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            MapZoomStep = config.Bind("Command", "MapZoomStep", 1f,
                new ConfigDescription("3D relief map: how far one notch of the wheel zooms.",
                    new AcceptableValueRange<float>(0.25f, 3f)));

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

            UiSoundVolume = config.Bind("Command", "UiSoundVolume", .7f,
                new ConfigDescription("Volume of Boscali avionics control feedback; zero mutes it.",
                    new AcceptableValueRange<float>(0f, 1f)));

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

            TargetWeaponOnly = config.Bind("Command", "TargetWeaponOnly", false,
                "Show only targets suitable for the selected weapon in the TGT acquisition browser.");
            TargetRangeKm = config.Bind("Command", "TargetRangeKm", 0,
                "Maximum acquisition distance in kilometres: 0 (unlimited), 10, 25, 50, or 100.");
        }
    }
}
