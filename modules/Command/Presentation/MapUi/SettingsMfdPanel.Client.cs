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
        private static readonly string[] FinishNames = { "CLEAR", "GLASS", "CRT", "CUSTOM" };
        private static readonly string[] FinishBriefs =
        {
            "CLEAR: a clean display with no surface finish. Panel feedback and interference remain controlled separately.",
            "GLASS: smoked cover glass, restrained reflections, square edge glints and a fine bezel shadow. The default.",
            "CRT: fine phosphor scanlines and a slow refresh band, with a little glass and square edge shading.",
            "CUSTOM: tune reflection, texture, edge shading and signal yourself on the dials that open below.",
        };

        // The CLEAR / GLASS / CRT presets, one entry each. They write the existing saved controls; manual tuning leaves no preset selected.
        private static readonly float[] FinishGlass = { 0f, .5f, .2f }, FinishScan = { 0f, 0f, .55f },
            FinishEdge = { 0f, .35f, .6f }, FinishLife = { 0f, .5f, .6f };

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
                "Sky light, canopy reflection and the glass rim across the maximized MFD. 0% is a matte screen.");
            FinishRing(flow, tune, page, "SCAN TEXTURE", settings.DisplayScanlines, "Phosphor scanlines (one dark row in three screen pixels) and the refresh band. 0% is off.");
            FinishRing(flow, tune, page, "EDGE SHADING", settings.DisplayVignette, "A narrow square bezel shadow around the display. 0% is off.");
            FinishRing(flow, tune, page, "LIVE SIGNAL", settings.DisplayLife,
                "A brief edge sweep marks display opening; a slow refresh band and interference add signal motion. Content stays readable throughout. 0% is still.");
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
            fx.Options[0].Help = "OFF: stop animated panel effects. SCREEN FINISH still controls the display surface.";
            fx.Options[1].Help = "LITE: keep the scan and shine effects, drop the heavier passes.";
            fx.Options[2].Help = "FULL: every panel effect the scheme defines.";
            flow.Ticker.Add(page, AvTickRate.Slow, fx.Refresh);
            ToggleCell(flow, page, "REDUCED MOTION", "Snap every panel animation to its end state, WMC included.",
                () => settings.AvionicsReducedMotion.Value, v => settings.AvionicsReducedMotion.Value = v);
            ToggleCell(flow, page, "BLUR BEHIND", "Soften the world behind floating windows. OFF keeps the static glass surface.",
                () => settings.AvionicsBlurBehind.Value, v => settings.AvionicsBlurBehind.Value = v);
            ToggleCell(flow, page, "ADAPT TO LIGHT", "Let ambient light vary the glass reflection. OFF keeps it steady.",
                () => settings.DisplayAutoLight.Value, v => { settings.DisplayEffects.Value = true; settings.DisplayAutoLight.Value = v; });
            ToggleCell(flow, page, "STANDING ORDERS", "Each page's usage notes (SOP) turn over in the footer under the status line.",
                () => settings.PanelOrders.Value, v => settings.PanelOrders.Value = v);
            ToggleCell(flow, page, "HOVER HELP", "A control's help (TIP) in the footer while the pointer is over it.",
                () => settings.PanelHoverHelp.Value, v => settings.PanelHoverHelp.Value = v);
            var tone = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, tone, page, "UI VOLUME", settings.UiSoundVolume, 0f, 1f, .1f,
                "Volume of interface clicks and hover sounds. 0% is silent.");

            BuildBoardRows(flow, page, CPanels);
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

        private void BuildMapPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Map2, "MAP CONSOLE", null);
            ToggleCell(flow, page, "TERRAIN IMAGE", "Show the satellite terrain beneath map symbols.",
                () => settings.MapTerrainImage.Value, v => settings.MapTerrainImage.Value = v);
            ToggleCell(flow, page, "3D RELIEF",
                "Render baked game terrain as a tilted tactical model. Symbols, front line and clicks follow the same surface.",
                () => settings.MapRelief3D.Value, v => settings.MapRelief3D.Value = v,
                () => settings.MapTerrainImage.Value,
                () => "Turn on terrain image first.");
            ToggleCell(flow, page, "NEWS TICKER", "Show theater dispatches above the map.",
                () => settings.NewsTickerEnabled.Value, v => settings.NewsTickerEnabled.Value = v);

            var levels = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, levels, page, "MFD OPACITY", settings.DeckOpacity, .1f, 1f, .05f,
                "Opacity of MFD panels and their backdrop. Lower values make the panels more transparent.");

            // Ported from the config file: how contacts stand on the relief and how the mouse drives it.
            Func<bool> relief = () => settings.MapRelief3D.Value && settings.MapTerrainImage.Value;
            Func<string> needRelief = () => "Turn on 3D RELIEF first.";
            flow.Section(AvIcon.Target, "CONTACTS & MOUSE", "SYMBOLS · 3D RELIEF CONTROLS");
            ToggleCell(flow, page, "MAP SYMBOLS", "Contacts as solid symbols, one silhouette per platform class, tinted by side. OFF: the game's icons.",
                () => settings.MapSymbology.Value, v => settings.MapSymbology.Value = v);
            ToggleCell(flow, page, "3D MINIATURES", "A small low-poly model stands on the relief for each contact instead of its flat symbol.",
                () => settings.MapMinis3D.Value, v => settings.MapMinis3D.Value = v,
                () => relief() && settings.MapSymbology.Value, () => relief() ? "Turn on MAP SYMBOLS first." : needRelief());
            ToggleCell(flow, page, "TACTICAL CURSOR", "A themed cursor; on the map it shows what a click would do: select, drop, box, pan, turn, place or move. OFF: the system arrow.",
                () => settings.MapCursor.Value, v => settings.MapCursor.Value = v);
            ToggleCell(flow, page, "RIGHT-DRAG TURNS", "Hold the right button and drag to turn and tilt the relief. A plain right click still opens the menu.",
                () => settings.MapRightDragTurns.Value, v => settings.MapRightDragTurns.Value = v, relief, needRelief);
            ToggleCell(flow, page, "DOUBLE-CLICK ZOOM", "A double click on the ground centres and zooms in there.",
                () => settings.MapDoubleClickZoom.Value, v => settings.MapDoubleClickZoom.Value = v, relief, needRelief);
            var mouse = flow.Add(new SetRingRow(flow.Content));
            EntryRing(flow, mouse, page, "TURN SPEED", settings.MapTurnSensitivity, .25f, 4f, .25f, v => AvNum.Fixed(v, 2) + "x",
                "How fast a held-button drag turns and tilts the relief.", relief, needRelief);
            EntryRing(flow, mouse, page, "ZOOM STEP", settings.MapZoomStep, .25f, 3f, .25f, v => AvNum.Fixed(v, 2) + "x",
                "How far one notch of the mouse wheel zooms the relief.", relief, needRelief);
        }

        /// <summary>HUD: the TAC BOARD first (what a pilot tunes most), then the visor, flight aids, comms notices, targeting and every key.</summary>
        private void BuildHudPage(AvFlow flow, int page)
        {
            BuildTacBoardRows(flow, page);
            BuildBoardRows(flow, page, CHud);
            flow.Section(AvIcon.Map2, "VISOR & TARGETING", null);
            var level = flow.Add(new SetRingRow(flow.Content));
            PercentRing(flow, level, page, "TERRAIN GHOST", settings.TerrainGhost, 0f, 1f, .05f,
                "A faint wire relief of the ground around and below you, over the view. 0% turns it off.");
            ToggleCell(flow, page, "RADIAL PRESETS",
                "Offer the TGT quick slots as a page in the native cockpit radial menu.",
                () => settings.TargetPresetWheel.Value, v => settings.TargetPresetWheel.Value = v);
            BuildKeyRows(flow);
        }

        /// <summary>VISUALS: the cockpit feel and the world look, as the owning features publish them, then a reset and the compat-mod links when such a mod is installed.</summary>
        private void BuildVisualsPage(AvFlow flow, int page)
        {
            bool any = false;
            if (clientSettings != null)
                foreach (ClientSetting row in clientSettings.Rows) any |= ClientTabOf(row.Section) == CVisuals;
            if (!any)
            {
                flow.Section(AvIcon.Eye, "VISUALS", "UNAVAILABLE");
                flow.Add(new AvNote(flow.Content) { MinHeight = 18f }).Set("NOTHING INSTALLED FOR THIS PAGE");
                return;
            }
            BuildBoardRows(flow, page, CVisuals);
            BoardResetButton(flow, CVisuals, "VISUALS");
            BuildBoardRows(flow, page, CCompat);
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
