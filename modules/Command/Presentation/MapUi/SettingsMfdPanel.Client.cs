using NOAvionics;
using System;
using BepInEx.Configuration;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>THIS PILOT pages: settings stored on this machine only.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        private static readonly string[] BackgroundNames = { "MATTE", "CUSTOM" };
        private static readonly string[] BackgroundBriefs =
        {
            "MATTE: shaded instrument surface. Legacy decorative patterns are retired.",
            "CUSTOM: your own PNG or JPEG from the wallpapers folder; pick it with IMAGE FILE.",
        };

        private static readonly string[] FinishNames = { "CLEAR", "GLASS", "CRT", "CUSTOM" };
        private static readonly string[] FinishBriefs =
        {
            "CLEAR: no reflection, texture, edge shading or signal motion. Maximum readability.",
            "GLASS: subtle light-adaptive reflection and the default live signal. The default finish.",
            "CRT: restrained scan texture, edge shading and live signal, with low reflection.",
            "CUSTOM: tune reflection, texture, edge shading and signal yourself on the dials that open below.",
        };

        // The CLEAR / GLASS / CRT presets, one entry each. They write the existing saved controls; manual tuning leaves no preset selected.
        private static readonly float[] FinishGlass = { 0f, .35f, .15f }, FinishScan = { 0f, 0f, .25f },
            FinishEdge = { 0f, 0f, .15f }, FinishLife = { 0f, .6f, .35f };

        /// <summary>CUSTOM was picked this session, so the dials stay open even while their values match a preset.</summary>
        private bool fineTune;

        /// <summary>PANELS: everything about how the MFD consoles look and sound, in the order a pilot sets it up.</summary>
        private void BuildPanelsPage(AvFlow flow, int page)
        {
            BuildSchemeSection(flow, page);

            flow.Section(AvIcon.Typography, "SCREEN FINISH", "PICK ONE · OR CUSTOM");
            var finishStrip = flow.Add(AvSegmented.Strip(flow.Content, FinishNames, () =>
            {
                int i = DisplayFinishIndex();
                return fineTune || i < 0 ? 3 : i;
            }, i =>
            {
                fineTune = i == 3;
                if (i < 3) SetDisplayFinish(i);
                Echo("SCREEN FINISH — " + FinishNames[i]);
                Changed();
            }));
            for (int i = 0; i < finishStrip.Options.Length; i++) finishStrip.Options[i].Help = FinishBriefs[i];
            var tune = flow.Add(new SetRingRow(flow.Content));
            FinishRing(flow, tune, page, "GLASS REFLECTION", settings.DisplayGlass,
                "How strongly the glass sheen reflects across the maximized MFD. 0% is a matte screen.");
            FinishRing(flow, tune, page, "SCAN TEXTURE", settings.DisplayScanlines, "Fine static screen texture over the display. 0% is off.");
            FinishRing(flow, tune, page, "EDGE SHADING", settings.DisplayVignette, "Shades the display perimeter without a circular mask. 0% is off.");
            FinishRing(flow, tune, page, "LIVE SIGNAL", settings.DisplayLife,
                "The display powers on from a line, a slow refresh band rolls down it, and jamming breaks the picture up harder. 0% is a still display.");
            void RefreshFinish()
            {
                finishStrip.Refresh();
                tune.SetShown(fineTune || DisplayFinishIndex() < 0);
            }
            RefreshFinish();
            flow.Ticker.Add(page, AvTickRate.Slow, RefreshFinish);

            flow.Section(AvIcon.Bolt, "PANEL MOTION & SOUND", "EVERY CONSOLE");
            var fx = flow.Add(new AvSegmented(flow.Content, "PANEL FX", new[] { "OFF", "LITE", "FULL" },
                () => (int)settings.AvionicsFxTier.Value, i =>
                {
                    settings.AvionicsFxTier.Value = (AvFxTier)i;
                    Echo("PANEL FX — " + settings.AvionicsFxTier.Value.ToString().ToUpperInvariant());
                    Changed();
                }));
            fx.Options[0].Help = "OFF: no panel animation or glass effects at all. Cheapest.";
            fx.Options[1].Help = "LITE: keep the scan and shine effects, drop the heavier passes.";
            fx.Options[2].Help = "FULL: every panel effect the scheme defines.";
            flow.Ticker.Add(page, AvTickRate.Slow, fx.Refresh);
            AvCellGrid motion = flow.Grid(3);
            ToggleCell(flow, motion, page, "REDUCED MOTION", "Snap every panel animation to its end state, WMC included.",
                () => settings.AvionicsReducedMotion.Value, v => settings.AvionicsReducedMotion.Value = v);
            ToggleCell(flow, motion, page, "BLUR BEHIND", "Blur the world behind floating windows (up to ~0.4 ms GPU; frosted glass when off).",
                () => settings.AvionicsBlurBehind.Value, v => settings.AvionicsBlurBehind.Value = v);
            ToggleCell(flow, motion, page, "ADAPT TO LIGHT", "Let ambient light vary the glass reflection. OFF keeps it steady.",
                () => settings.DisplayAutoLight.Value, v => { settings.DisplayEffects.Value = true; settings.DisplayAutoLight.Value = v; });
            var tone = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, tone, page, "UI VOLUME", settings.UiSoundVolume, 0f, 1f, .1f,
                "Volume of interface clicks and hover sounds. 0% is silent.");

            BuildBoardRows(flow, page, CPanels);

            var preview = flow.Add(new SetFinishPreview(flow.Content), 1f);
            preview.Refresh(settings);
            flow.Ticker.Add(page, AvTickRate.Slow, () => preview.Refresh(settings));
        }

        /// <summary>A finish dial. Turning one switches the display filter back on (an old config may have it off).</summary>
        private SetRingCell FinishRing(AvFlow flow, SetRingRow row, int page, string title, ConfigEntry<float> entry, string help) =>
            Ring(flow, row, page, title, () => AvNum.Percent(entry.Value), () => Mathf.Clamp01(entry.Value), d =>
                {
                    settings.DisplayEffects.Value = true;
                    entry.Value = Mathf.Clamp01(Mathf.Round((entry.Value + d * .1f) * 100f) / 100f);
                },
                () => entry.Value > .001f, () => entry.Value < .999f, help);

        private void SetDisplayFinish(int index)
        {
            settings.DisplayEffects.Value = true;
            settings.DisplayAutoLight.Value = true;
            settings.DisplayGlass.Value = FinishGlass[index];
            settings.DisplayScanlines.Value = FinishScan[index];
            settings.DisplayVignette.Value = FinishEdge[index];
            settings.DisplayLife.Value = FinishLife[index];
        }

        private int DisplayFinishIndex()
        {
            if (!settings.DisplayEffects.Value) return -1;
            for (int i = 0; i < 3; i++)
                if ((i == 0 || settings.DisplayAutoLight.Value) &&
                    Mathf.Approximately(settings.DisplayGlass.Value, FinishGlass[i]) &&
                    Mathf.Approximately(settings.DisplayScanlines.Value, FinishScan[i]) &&
                    Mathf.Approximately(settings.DisplayVignette.Value, FinishEdge[i]) &&
                    Mathf.Approximately(settings.DisplayLife.Value, FinishLife[i])) return i;
            return -1;
        }

        private void SetBackground(int mode)
        {
            settings.DeckGrid.Value = false;
            settings.CheckerboardOverlay.Value = false;
            settings.BackgroundImage.Value = mode == 1;
            if (mode == 1) settings.BackgroundImagePreset.Value = 3;
        }

        private bool ImageEnabled() => settings.ExpandedMapUi.Value && settings.BackgroundImage.Value && settings.BackgroundImagePreset.Value == 3;

        private int BackgroundIndex() =>
            Array.IndexOf(BackgroundNames, SettingsChoices.BackgroundName(
                settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value));

        private void BuildMapPage(AvFlow flow, int page)
        {
            Func<bool> expanded = () => settings.ExpandedMapUi.Value;
            Func<string> needExpanded = () => "Turn on expanded layout first.";

            flow.Section(AvIcon.Map2, "MAP CONSOLE", "LAYOUT · TERRAIN · TICKER");
            AvCellGrid switches = flow.Grid(2);
            ToggleCell(flow, switches, page, "EXPANDED LAYOUT", "Use the full map console. OFF restores the native layout.",
                () => settings.ExpandedMapUi.Value, v => settings.ExpandedMapUi.Value = v);
            ToggleCell(flow, switches, page, "TERRAIN IMAGE", "Show the satellite terrain beneath map symbols.",
                () => settings.MapTerrainImage.Value, v => settings.MapTerrainImage.Value = v, expanded, needExpanded);
            ToggleCell(flow, switches, page, "3D RELIEF",
                "Render baked game terrain as a tilted tactical model. Symbols, front line and clicks follow the same surface.",
                () => settings.MapRelief3D.Value, v => settings.MapRelief3D.Value = v,
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                () => "Turn on expanded layout and terrain image first.");
            ToggleCell(flow, switches, page, "NEWS TICKER", "Show theater dispatches above the map.",
                () => settings.NewsTickerEnabled.Value, v => settings.NewsTickerEnabled.Value = v, expanded, needExpanded);

            var levels = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, levels, page, "TERRAIN STRENGTH", settings.MapTerrainOpacity, .1f, 1f, .1f,
                "Opacity of the satellite terrain under the map symbols.",
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                () => "Enable expanded layout and terrain image first.");
            PercentRing(flow, levels, page, "CONSOLE OPACITY", settings.DeckOpacity, .1f, 1f, .05f,
                "Opacity of the map console panels. Lower lets the map show through.", expanded, needExpanded);
            PercentRing(flow, levels, page, "MAP DARKENING", settings.MapTrayOpacity, 0f, 1f, .05f,
                "How far the map behind the console is darkened so symbols read clearly.", expanded, needExpanded);
            EntryRing(flow, levels, page, "TICKER SPEED", settings.NewsTickerSpeed, 15f, 150f, 15f, v => AvNum.Fixed(v, 0) + " px/s",
                "Lower speeds are easier to read. Disable NEWS TICKER to stop motion.",
                () => settings.ExpandedMapUi.Value && settings.NewsTickerEnabled.Value,
                () => "Enable expanded layout and news ticker first.");

            // Ported from the config file: how contacts stand on the relief and how the mouse drives it.
            Func<bool> relief = () => expanded() && settings.MapRelief3D.Value && settings.MapTerrainImage.Value;
            Func<string> needRelief = () => "Turn on 3D RELIEF first.";
            flow.Section(AvIcon.Target, "CONTACTS & MOUSE", "SYMBOLS · 3D RELIEF CONTROLS");
            AvCellGrid contacts = flow.Grid(2);
            ToggleCell(flow, contacts, page, "MAP SYMBOLS", "Contacts as solid symbols, one silhouette per platform class, tinted by side. OFF: the game's icons.",
                () => settings.MapSymbology.Value, v => settings.MapSymbology.Value = v, expanded, needExpanded);
            ToggleCell(flow, contacts, page, "3D MINIATURES", "A small low-poly model stands on the relief for each contact instead of its flat symbol.",
                () => settings.MapMinis3D.Value, v => settings.MapMinis3D.Value = v,
                () => relief() && settings.MapSymbology.Value, () => relief() ? "Turn on MAP SYMBOLS first." : needRelief());
            ToggleCell(flow, contacts, page, "RIGHT-DRAG TURNS", "Hold the right button and drag to turn and tilt the relief. A plain right click still opens the menu.",
                () => settings.MapRightDragTurns.Value, v => settings.MapRightDragTurns.Value = v, relief, needRelief);
            ToggleCell(flow, contacts, page, "DOUBLE-CLICK ZOOM", "A double click on the ground centres and zooms in there.",
                () => settings.MapDoubleClickZoom.Value, v => settings.MapDoubleClickZoom.Value = v, relief, needRelief);
            var mouse = flow.Add(new SetRingRow(flow.Content));
            EntryRing(flow, mouse, page, "TURN SPEED", settings.MapTurnSensitivity, .25f, 4f, .25f, v => AvNum.Fixed(v, 2) + "x",
                "How fast a held-button drag turns and tilts the relief.", relief, needRelief);
            EntryRing(flow, mouse, page, "ZOOM STEP", settings.MapZoomStep, .25f, 3f, .25f, v => AvNum.Fixed(v, 2) + "x",
                "How far one notch of the mouse wheel zooms the relief.", relief, needRelief);

            // The backdrop controls that only matter for the chosen background appear with it, so a page that
            // is mostly disabled reasons never builds up.
            flow.Section(AvIcon.Stack2, "BACKDROP", "BEHIND THE CONSOLE");
            var background = flow.Add(AvSegmented.Strip(flow.Content, BackgroundNames, BackgroundIndex, i =>
            {
                SetBackground(i);
                Echo("BACKGROUND — " + BackgroundNames[i]);
                Changed();
            }));
            var image = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, image, page, "IMAGE STRENGTH", settings.BackgroundImageOpacity, .05f, 1f, .05f,
                "How visible the background image is.", ImageEnabled, () => "Choose an image background first.");
            AvRow imageFile = Stepper(flow, page, "IMAGE FILE", MfdMapDeck.GetCurrentWallpaperFileName,
                MfdMapDeck.CycleCustomWallpaper,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                "Local PNG/JPEG files. Use RESCAN after adding or replacing files.", ImageEnabled,
                () => "Choose CUSTOM as the background. Add files to BepInEx/config/BoscaliSummer/wallpapers.");
            string[] fits = { "COVER", "FIT", "STRETCH" };
            AvRow imageFit = Stepper(flow, page, "IMAGE FIT",
                () => fits[Mathf.Clamp(settings.WallpaperFitMode.Value, 0, 2)],
                d => settings.WallpaperFitMode.Value = (settings.WallpaperFitMode.Value + d + 3) % 3,
                () => true, () => true, "COVER crops; FIT keeps the full image; STRETCH fills the screen.",
                ImageEnabled, () => "Choose CUSTOM as the background first.");
            AvNote wallNote = new AvNote(flow.Content) { MinHeight = 18f };
            AvButtons scanRow = new AvButtons(flow.Content, new[]
            {
                new AvControl.Spec("RESCAN LOCAL FILES", () =>
                {
                    MfdMapDeck.RescanWallpapers();
                    Echo("RESCAN — " + MfdMapDeck.WallpaperStatus);
                    Changed();
                }, AvButtonStyle.Primary, AvIcon.Refresh),
            });
            flow.Row(wallNote, scanRow);
            AvControl scan = scanRow.Controls[0];

            var layout = flow.Add(new SetLayoutPreview(flow.Content), 1f);

            void RefreshBackdrop()
            {
                bool custom = settings.BackgroundImage.Value && settings.BackgroundImagePreset.Value == 3;
                bool isExpanded = settings.ExpandedMapUi.Value;
                background.Refresh();
                for (int i = 0; i < background.Options.Length; i++)
                {
                    background.Options[i].Interactable = isExpanded;
                    background.Options[i].Help = isExpanded ? BackgroundBriefs[i] : needExpanded();
                }
                image.SetShown(custom);
                imageFile.SetShown(custom);
                imageFit.SetShown(custom);
                wallNote.SetShown(custom);
                scanRow.SetShown(custom);
                if (custom) wallNote.Set(MfdMapDeck.WallpaperStatus);
                bool ready = ImageEnabled();
                scan.Interactable = ready;
                scan.Help = ready
                    ? "Scan up to 512 entries. PNG/JPEG: 16 MB and 4096 pixels per side."
                    : "Turn on expanded layout and choose CUSTOM first.";
                layout.Refresh(settings);
            }
            RefreshBackdrop();
            flow.Ticker.Add(page, AvTickRate.Slow, RefreshBackdrop);
        }

        /// <summary>HUD: the TAC BOARD and the visor first (what a pilot tunes most), then flight aids, comms notices, targeting and every key.</summary>
        private void BuildHudPage(AvFlow flow, int page)
        {
            BuildTacBoardRows(flow, page);
            BuildVisorRows(flow, page);
            BuildBoardRows(flow, page, CHud);
            flow.Section(AvIcon.Target, "TARGETING", "TGT QUICK SLOTS");
            ToggleCell(flow, flow.Grid(1), page, "RADIAL PRESETS",
                "Offer the TGT quick slots as a page in the native cockpit radial menu.",
                () => settings.TargetPresetWheel.Value, v => settings.TargetPresetWheel.Value = v);
            BuildKeyRows(flow);
        }

        /// <summary>COCKPIT and WORLD: nothing but the rows the owning features publish, and a reset for the page.</summary>
        private void BuildBoardPage(AvFlow flow, int page, string what)
        {
            bool any = false;
            if (clientSettings != null)
                foreach (ClientSetting row in clientSettings.Rows) any |= ClientTabOf(row.Section) == page;
            if (!any)
            {
                flow.Section(page == CCockpit ? AvIcon.WaveSine : AvIcon.CloudRain, what, "UNAVAILABLE");
                flow.Add(new AvNote(flow.Content) { MinHeight = 18f }).Set("NOTHING INSTALLED FOR THIS PAGE");
                return;
            }
            BuildBoardRows(flow, page, page);
            flow.Section(AvIcon.Refresh, what + " DEFAULTS", "THIS PAGE ONLY");
            BoardResetButton(flow, page, what);
        }

        /// <summary>Keys the installed features read live, rebound in place.</summary>
        private void BuildKeyRows(AvFlow flow)
        {
            var keys = clientSettings?.Keys;
            if (keys == null || keys.Count == 0) return;
            flow.Section(AvIcon.AdjustmentsHorizontal, "KEYS", "CLICK, THEN PRESS A KEY");
            foreach (ClientKeySetting key in keys) KeyBindControl.Add(flow, key);
        }
    }
}
