using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
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

        private void BuildDisplayPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Typography, "DISPLAY & AUDIO", "GLASS · SURFACE · TINT · SOUND");

            AvCellGrid switches = flow.Grid(3);
            ToggleCell(flow, switches, page, "DISPLAY EFFECTS", "OFF removes glass and overlays. Your tuning is retained.",
                () => settings.DisplayEffects.Value, v => settings.DisplayEffects.Value = v);
            ToggleCell(flow, switches, page, "ADAPT TO LIGHT", "Let ambient light vary the glass reflection. OFF keeps it steady.",
                () => settings.DisplayAutoLight.Value, v => settings.DisplayAutoLight.Value = v,
                EffectsEnabled, EffectsDisabled);
            ToggleCell(flow, switches, page, "REDUCED MOTION", "Snap every panel animation to its end state.",
                () => settings.AvionicsReducedMotion.Value,
                v => settings.AvionicsReducedMotion.Value = v);

            var finish = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, finish, page, "GLASS REFLECTION", settings.DisplayGlass, 0f, 1f, .1f,
                "How strongly the glass sheen reflects across the maximized MFD. 0% is a matte screen.",
                EffectsEnabled, EffectsDisabled);
            PercentRing(flow, finish, page, "SCAN TEXTURE", settings.DisplayScanlines, 0f, 1f, .1f,
                "Fine static screen texture over the display. 0% is off.", EffectsEnabled, EffectsDisabled);
            PercentRing(flow, finish, page, "EDGE SHADING", settings.DisplayVignette, 0f, 1f, .1f,
                "Shades the display perimeter without a circular mask. 0% is off.", EffectsEnabled, EffectsDisabled);

            string[] tints = { "NEUTRAL", "GREEN", "AMBER", "ICE", "ROSE" };
            string[] tintBriefs =
            {
                "NEUTRAL: no colour wash.", "GREEN: a phosphor-green wash.", "AMBER: a warm amber wash.",
                "ICE: a cool blue wash.", "ROSE: a soft rose wash.",
            };
            var tintStrip = flow.Add(new AvSegmented(flow.Content, "COLOR TINT", tints,
                () => Mathf.Clamp(settings.DisplayTint.Value, 0, tints.Length - 1),
                i =>
                {
                    settings.DisplayTint.Value = i;
                    Echo("COLOR TINT — " + tints[i]);
                    Changed();
                }));
            void RefreshTint()
            {
                bool on = EffectsEnabled();
                tintStrip.Refresh();
                for (int i = 0; i < tintStrip.Options.Length; i++)
                {
                    tintStrip.Options[i].Interactable = on;
                    tintStrip.Options[i].Help = on ? tintBriefs[i] + " Warning colours stay distinct." : EffectsDisabled();
                }
            }
            RefreshTint();
            flow.Ticker.Add(page, AvTickRate.Slow, RefreshTint);

            var tone = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, tone, page, "TINT STRENGTH", settings.DisplayTintStrength, 0f, 1f, .1f,
                "How strong the colour wash is.", () => EffectsEnabled() && settings.DisplayTint.Value != 0,
                () => "Enable effects and choose a color tint first.");
            PercentRing(flow, tone, page, "UI VOLUME", settings.UiSoundVolume, 0f, 1f, .1f,
                "Volume of interface clicks and hover sounds. 0% is silent.", () => true, () => "");

            AvControl resetDisplay = flow.Buttons(new AvControl.Spec("RESET DISPLAY FILTER", () =>
            {
                settings.DisplayEffects.Value = true;
                settings.DisplayGlass.Value = .35f;
                settings.DisplayAutoLight.Value = true;
                settings.DisplayScanlines.Value = 0f;
                settings.DisplayVignette.Value = 0f;
                settings.DisplayTint.Value = 0;
                settings.DisplayTintStrength.Value = .25f;
                Echo("Display filter reset.");
                Changed();
            }, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            resetDisplay.Help = "Restore the default glass finish and remove CRT, edge shading and tint.";

            var preview = flow.Add(new SetFinishPreview(flow.Content), 1f);
            preview.Refresh(settings);
            flow.Ticker.Add(page, AvTickRate.Slow, () => preview.Refresh(settings));
        }

        private void SetBackground(int mode)
        {
            settings.DeckGrid.Value = false;
            settings.CheckerboardOverlay.Value = false;
            settings.BackgroundImage.Value = mode == 1;
            if (mode == 1) settings.BackgroundImagePreset.Value = 3;
        }

        private bool ImageEnabled() => settings.ExpandedMapUi.Value && settings.BackgroundImage.Value && settings.BackgroundImagePreset.Value == 3;
        private bool CustomEnabled() => ImageEnabled();

        private int BackgroundIndex() =>
            Array.IndexOf(BackgroundNames, SettingsChoices.BackgroundName(settings.DeckGrid.Value,
                settings.CheckerboardOverlay.Value, settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value));

        private void BuildMapPage(AvFlow flow, int page)
        {
            Func<bool> expanded = () => settings.ExpandedMapUi.Value;
            Func<string> needExpanded = () => "Turn on expanded layout first.";

            flow.Section(AvIcon.Map2, "MAP CONSOLE", "LAYOUT · TERRAIN · TICKER");
            AvCellGrid switches = flow.Grid(2);
            ToggleCell(flow, switches, page, "EXPANDED LAYOUT", "Use the full map console. OFF restores the native layout.",
                () => settings.ExpandedMapUi.Value, v => settings.ExpandedMapUi.Value = v);
            ToggleCell(flow, switches, page, "3D RELIEF",
                "Render baked game terrain as a tilted tactical model. Symbols, front line and clicks follow the same surface.",
                () => settings.MapRelief3D.Value, v => settings.MapRelief3D.Value = v,
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                () => "Turn on expanded layout and terrain image first.");
            ToggleCell(flow, switches, page, "TERRAIN IMAGE", "Show the satellite terrain beneath map symbols.",
                () => settings.MapTerrainImage.Value, v => settings.MapTerrainImage.Value = v, expanded, needExpanded);
            ToggleCell(flow, switches, page, "NEWS TICKER", "Show theater dispatches above the map.",
                () => settings.NewsTickerEnabled.Value, v => settings.NewsTickerEnabled.Value = v, expanded, needExpanded);

            var levels = flow.Add(new SetRingRow(flow.Content));
            Ring(flow, levels, page, "SECTOR REFRESH",
                () => AvNum.Seconds(settings.GridRefreshInterval.Value, 1),
                () => Mathf.Clamp01(settings.GridRefreshInterval.Value / 2f),
                d => settings.GridRefreshInterval.Value = Mathf.Clamp(
                    Mathf.Round((settings.GridRefreshInterval.Value + d * .1f) * 10f) / 10f, .2f, 2f),
                () => settings.GridRefreshInterval.Value > .201f,
                () => settings.GridRefreshInterval.Value < 1.999f,
                "How often the sector grid updates. Longer intervals reduce CPU work. Recommended: 0.5 s.");
            PercentRing(flow, levels, page, "TERRAIN STRENGTH", settings.MapTerrainOpacity, .1f, 1f, .1f,
                "Opacity of the satellite terrain under the map symbols.",
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                () => "Enable expanded layout and terrain image first.");
            PercentRing(flow, levels, page, "CONSOLE OPACITY", settings.DeckOpacity, .1f, 1f, .05f,
                "Opacity of the map console panels. Lower lets the map show through.", expanded, needExpanded);

            // MAP DARKENING and TICKER SPEED are always here; the strength of whichever backdrop is chosen joins them.
            var more = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, more, page, "MAP DARKENING", settings.MapTrayOpacity, 0f, 1f, .05f,
                "How far the map behind the console is darkened so symbols read clearly.", expanded, needExpanded);
            Ring(flow, more, page, "TICKER SPEED",
                () => AvNum.Fixed(settings.NewsTickerSpeed.Value, 0) + " px/s",
                () => Mathf.Clamp01(settings.NewsTickerSpeed.Value / 150f),
                d => settings.NewsTickerSpeed.Value = Mathf.Clamp(settings.NewsTickerSpeed.Value + d * 15f, 15f, 150f),
                () => settings.NewsTickerSpeed.Value > 15f, () => settings.NewsTickerSpeed.Value < 150f,
                "Lower speeds are easier to read. Disable NEWS TICKER to stop motion.",
                () => settings.ExpandedMapUi.Value && settings.NewsTickerEnabled.Value,
                () => "Enable expanded layout and news ticker first.",
                () => AvNum.Fixed(settings.NewsTickerSpeed.Value, 0));

            // The backdrop controls that only matter for the chosen background appear with it, so a page that
            // is mostly disabled reasons never builds up.
            flow.Section(AvIcon.Stack2, "BACKDROP", "BEHIND THE CONSOLE");
            var background = flow.Add(AvSegmented.Strip(flow.Content, BackgroundNames, BackgroundIndex, i =>
            {
                SetBackground(i);
                Echo("BACKGROUND — " + BackgroundNames[i]);
                Changed();
            }));
            SetRingCell imageStrength = PercentRing(flow, more, page, "IMAGE STRENGTH", settings.BackgroundImageOpacity, .05f, 1f, .05f,
                "How visible the background image is.", ImageEnabled, () => "Choose an image background first.");
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

            int shownKey = -1;
            void RefreshBackdrop()
            {
                bool image = settings.BackgroundImage.Value && settings.BackgroundImagePreset.Value == 3;
                bool custom = image && settings.BackgroundImagePreset.Value == 3;
                bool isExpanded = settings.ExpandedMapUi.Value;
                background.Refresh();
                for (int i = 0; i < background.Options.Length; i++)
                {
                    background.Options[i].Interactable = isExpanded;
                    background.Options[i].Help = isExpanded ? BackgroundBriefs[i] : needExpanded();
                }
                int key = custom ? 1 : 0;
                imageStrength.SetShown(image);
                imageFile.SetShown(custom);
                imageFit.SetShown(custom);
                wallNote.SetShown(custom);
                scanRow.SetShown(custom);
                if (key != shownKey) { shownKey = key; more.Reflow(); }
                if (custom) wallNote.Set(MfdMapDeck.WallpaperStatus);
                bool ready = CustomEnabled();
                scan.Interactable = ready;
                scan.Help = ready
                    ? "Scan up to 512 entries. PNG/JPEG: 16 MB and 4096 pixels per side."
                    : "Turn on expanded layout and choose CUSTOM first.";
                layout.Refresh(settings);
            }
            RefreshBackdrop();
            flow.Ticker.Add(page, AvTickRate.Slow, RefreshBackdrop);
        }

        private void BuildCockpitPage(AvFlow flow, int page)
        {
            ModuleServices.TryGet(out IHudBoard board);
            flow.Section(AvIcon.Camera, "COCKPIT", "TARGETING · HUD");
            AvCellGrid switches = flow.Grid(2);
            ToggleCell(flow, switches, page, "TARGET CAMERA",
                "Show the native target camera feed inset on the status panel while a target is selected.",
                () => board != null && board.CameraFeedEnabled, v => { if (board != null) board.CameraFeedEnabled = v; },
                () => board != null, () => "HUD service unavailable in this scene.");
            ToggleCell(flow, switches, page, "RADIAL PRESETS",
                "Offer the TGT quick slots as a page in the native cockpit radial menu.",
                () => settings.TargetPresetWheel.Value, v => settings.TargetPresetWheel.Value = v);
            BuildHudRows(flow, page, board, switches);
        }
    }
}
