using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>THIS PILOT pages: settings stored on this machine only.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildDisplayPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Typography, "DISPLAY FILTER", "GLASS · CRT · TINT");
            Toggle(flow, page, "DISPLAY EFFECTS", "OFF removes glass and overlays. Your tuning is retained.",
                () => settings.DisplayEffects.Value, v => settings.DisplayEffects.Value = v);
            Percent(flow, page, "GLASS REFLECTION", settings.DisplayGlass, 0f, 1f, .1f, EffectsEnabled, EffectsDisabled);
            Toggle(flow, page, "ADAPT TO LIGHT", "Let ambient light vary the glass reflection. OFF keeps it steady.",
                () => settings.DisplayAutoLight.Value, v => settings.DisplayAutoLight.Value = v,
                EffectsEnabled, EffectsDisabled);
            Percent(flow, page, "CRT SCANLINES", settings.DisplayScanlines, 0f, 1f, .1f, EffectsEnabled, EffectsDisabled);
            Percent(flow, page, "EDGE SHADING", settings.DisplayVignette, 0f, 1f, .1f, EffectsEnabled, EffectsDisabled);
            string[] tints = { "NEUTRAL", "GREEN", "AMBER", "ICE", "ROSE" };
            Stepper(flow, page, "COLOR TINT",
                () => tints[Mathf.Clamp(settings.DisplayTint.Value, 0, 4)],
                d => settings.DisplayTint.Value = (settings.DisplayTint.Value + d + tints.Length) % tints.Length,
                () => true, () => true, "A gentle color wash across the maximized MFD; warning colors remain distinct.",
                EffectsEnabled, EffectsDisabled);
            Percent(flow, page, "TINT STRENGTH", settings.DisplayTintStrength, 0f, 1f, .1f,
                () => EffectsEnabled() && settings.DisplayTint.Value != 0, () => "Enable effects and choose a color tint first.");
            AvControl resetDisplay = flow.Buttons(new AvControl.Spec("RESET DISPLAY FILTER", () =>
            {
                settings.DisplayEffects.Value = true;
                settings.DisplayGlass.Value = .6f;
                settings.DisplayAutoLight.Value = true;
                settings.DisplayScanlines.Value = 0f;
                settings.DisplayVignette.Value = 0f;
                settings.DisplayTint.Value = 0;
                settings.DisplayTintStrength.Value = .25f;
                Echo("Display filter reset.");
                Changed();
            })).Controls[0];
            resetDisplay.Help = "Restore the default glass finish and remove CRT, edge shading and tint.";

            flow.Section(AvIcon.Settings, "PANEL THEME", "AVIONICS");
            var themeSeg = flow.Add(new AvSegmented(flow.Content, "THEME", new[] { "STEEL", "ACE", "PHOSPHOR" },
                () => (int)settings.AvionicsTheme.Value,
                i => { settings.AvionicsTheme.Value = (AvThemeId)i; Echo("THEME — " + settings.AvionicsTheme.Value); }));
            var motionCell = flow.Add(AvCell.Toggle(flow.Content, "REDUCED MOTION",
                "Snap every panel animation to its end state.",
                () => settings.AvionicsReducedMotion.Value,
                v => { settings.AvionicsReducedMotion.Value = v; Echo("REDUCED MOTION — " + (v ? "ON" : "OFF")); }));
            flow.Ticker.Add(page, AvTickRate.Slow, () => { themeSeg.Refresh(); motionCell.Refresh(); });

            flow.Section(AvIcon.Volume, "INTERFACE AUDIO", "LOCAL");
            Percent(flow, page, "UI VOLUME", settings.UiSoundVolume, 0f, 1f, .1f, () => true, () => "");
        }

        private void SetBackground(int mode)
        {
            settings.DeckGrid.Value = mode == 1;
            settings.CheckerboardOverlay.Value = mode == 2;
            settings.BackgroundImage.Value = mode >= 3;
            if (mode >= 3) settings.BackgroundImagePreset.Value = mode - 3;
        }

        private bool ImageEnabled() => settings.ExpandedMapUi.Value && settings.BackgroundImage.Value;
        private bool CustomEnabled() => ImageEnabled() && settings.BackgroundImagePreset.Value == 3;

        private void BuildMapPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Map2, "MAP CONSOLE", "LAYOUT");
            Toggle(flow, page, "EXPANDED LAYOUT", "Use the full map console. OFF restores the native layout.",
                () => settings.ExpandedMapUi.Value, v => settings.ExpandedMapUi.Value = v);
            Stepper(flow, page, "SECTOR REFRESH",
                () => AvNum.Seconds(settings.GridRefreshInterval.Value, 1),
                d => settings.GridRefreshInterval.Value = Mathf.Clamp(
                    Mathf.Round((settings.GridRefreshInterval.Value + d * .1f) * 10f) / 10f, .2f, 2f),
                () => settings.GridRefreshInterval.Value > .201f,
                () => settings.GridRefreshInterval.Value < 1.999f,
                "How often the sector grid updates. Longer intervals reduce CPU work. Recommended: 0.5 s.");

            flow.Section(AvIcon.Satellite, "TERRAIN", "RELIEF · IMAGE");
            Toggle(flow, page, "3D RELIEF",
                "Render baked game terrain as a tilted tactical model. Symbols, front line and clicks follow the same surface.",
                () => settings.MapRelief3D.Value, v => settings.MapRelief3D.Value = v,
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                () => "Turn on expanded layout and terrain image first.");
            Toggle(flow, page, "TERRAIN IMAGE", "Show the satellite terrain beneath map symbols.",
                () => settings.MapTerrainImage.Value, v => settings.MapTerrainImage.Value = v,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Percent(flow, page, "TERRAIN STRENGTH", settings.MapTerrainOpacity, .1f, 1f, .1f,
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                () => "Enable expanded layout and terrain image first.");

            // The backdrop rows that only matter for the chosen background appear with it, so a page that
            // is mostly disabled reasons never builds up.
            flow.Section(AvIcon.Stack2, "BACKDROP", "CONSOLE SURFACE");
            Percent(flow, page, "CONSOLE OPACITY", settings.DeckOpacity, .1f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Percent(flow, page, "MAP DARKENING", settings.MapTrayOpacity, 0f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Stepper(flow, page, "BACKGROUND",
                () => SettingsChoices.BackgroundName(settings.DeckGrid.Value, settings.CheckerboardOverlay.Value,
                    settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value),
                d => SetBackground(SettingsChoices.CycleBackground(settings.DeckGrid.Value,
                    settings.CheckerboardOverlay.Value, settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value, d)),
                () => true, () => true,
                "Choose one decoration: plain, grid, checker, hexagon, carbon, radar or custom image. MIXED preserves your old combination.",
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            AvRow checker = Percent(flow, page, "CHECKER STRENGTH", settings.CheckerboardOpacity, .02f, .4f, .02f,
                () => settings.ExpandedMapUi.Value && settings.CheckerboardOverlay.Value,
                () => "Choose CHECKER as the background first.");
            AvRow imageStrength = Percent(flow, page, "IMAGE STRENGTH", settings.BackgroundImageOpacity, .05f, 1f, .05f,
                ImageEnabled, () => "Choose an image background first.");
            AvRow imageFile = Stepper(flow, page, "IMAGE FILE", MfdMapDeck.GetCurrentWallpaperFileName,
                MfdMapDeck.CycleCustomWallpaper,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                "Local PNG/JPEG files. Use RESCAN after adding or replacing files.", CustomEnabled,
                () => "Choose CUSTOM as the background. Add files to BepInEx/config/BoscaliSummer/wallpapers.");
            string[] fits = { "COVER", "FIT", "STRETCH" };
            AvRow imageFit = Stepper(flow, page, "IMAGE FIT",
                () => fits[Mathf.Clamp(settings.WallpaperFitMode.Value, 0, 2)],
                d => settings.WallpaperFitMode.Value = (settings.WallpaperFitMode.Value + d + 3) % 3,
                () => true, () => true, "COVER crops; FIT keeps the full image; STRETCH fills the screen.",
                CustomEnabled, () => "Choose CUSTOM as the background first.");
            NoteLine wallNote = flow.Add(new NoteLine(flow.Content));
            AvButtons scanRow = flow.Buttons(new AvControl.Spec("RESCAN LOCAL FILES", () =>
            {
                MfdMapDeck.RescanWallpapers();
                Echo("RESCAN — " + MfdMapDeck.WallpaperStatus);
                Changed();
            }, AvButtonStyle.Primary));
            AvControl scan = scanRow.Controls[0];
            void RefreshBackdrop()
            {
                bool image = settings.BackgroundImage.Value;
                bool custom = image && settings.BackgroundImagePreset.Value == 3;
                checker.SetShown(settings.CheckerboardOverlay.Value);
                imageStrength.SetShown(image);
                imageFile.SetShown(custom);
                imageFit.SetShown(custom);
                wallNote.SetShown(custom);
                scanRow.SetShown(custom);
                if (custom) wallNote.Set(MfdMapDeck.WallpaperStatus + " Add PNG or JPEG files, then rescan.");
                bool ready = CustomEnabled();
                scan.Interactable = ready;
                scan.Help = ready
                    ? "Scan up to 512 entries. PNG/JPEG: 16 MB and 4096 pixels per side."
                    : "Turn on expanded layout and choose CUSTOM first.";
            }
            RefreshBackdrop();
            flow.Ticker.Add(page, AvTickRate.Slow, RefreshBackdrop);

            flow.Section(AvIcon.Message2, "DISPATCHES", "NEWS TICKER");
            Toggle(flow, page, "NEWS TICKER", "Show theater dispatches above the map.",
                () => settings.NewsTickerEnabled.Value, v => settings.NewsTickerEnabled.Value = v,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Stepper(flow, page, "TICKER SPEED",
                () => AvNum.Fixed(settings.NewsTickerSpeed.Value, 0) + " px/s",
                d => settings.NewsTickerSpeed.Value = Mathf.Clamp(settings.NewsTickerSpeed.Value + d * 15f, 15f, 150f),
                () => settings.NewsTickerSpeed.Value > 15f, () => settings.NewsTickerSpeed.Value < 150f,
                "Lower speeds are easier to read. Disable NEWS TICKER to stop motion.",
                () => settings.ExpandedMapUi.Value && settings.NewsTickerEnabled.Value,
                () => "Enable expanded layout and news ticker first.");
        }

        private void BuildCockpitPage(AvFlow flow, int page)
        {
            ModServices.TryGet(out IHudBoard board);
            flow.Section(AvIcon.Camera, "TARGETING", "CAMERA · RADIAL");
            Toggle(flow, page, "TARGET CAMERA",
                "Show the native target camera feed inset on the status panel while a target is selected.",
                () => board != null && board.CameraFeedEnabled, v => { if (board != null) board.CameraFeedEnabled = v; },
                () => board != null, () => "HUD service unavailable in this scene.");
            Toggle(flow, page, "RADIAL PRESETS",
                "Offer the TGT quick slots as a page in the native cockpit radial menu.",
                () => settings.TargetPresetWheel.Value, v => settings.TargetPresetWheel.Value = v);
            BuildHudRows(flow, page, board);
        }
    }
}
