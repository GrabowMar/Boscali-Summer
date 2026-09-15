using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BepInEx.Configuration;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Presentation;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal sealed class SettingsMfdPanel : MonoBehaviour, ISceneService
    {
        private CommandSettings settings;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private GameObject root;
        private GameObject surface;
        private MFDScreen screen;
        private AvScreen shell;
        private List<MFDScreen> boundScreens;
        private int boundSlot = -1;
        private bool claimed;
        private bool failed;
        private float nextTick;
        private readonly List<Action> refreshers = new List<Action>();
        private bool dirty = true;
        private bool wasVisible;
        private bool appearancePending;
        private bool overlayPending;
        private bool layoutPending;
        private bool tickerPending;
        private string actionEcho;
        private float actionEchoUntil;

        public void Configure(CommandSettings config, ManualLogSource log, ComMapOverlay mapOverlay = null)
        {
            settings = config;
            overlay = mapOverlay;
            logger = log;
            MfdMapDeck.Configure(config);
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            configFile = config.ExpandedMapUi.ConfigFile;
            configFile.SettingChanged += OnSettingChanged;
        }

        private ConfigFile configFile;

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (args.ChangedSetting.Definition.Section != "Command") return;
            dirty = true;
            switch (args.ChangedSetting.Definition.Key)
            {
                case "ExpandedMapUi": layoutPending = true; break;
                case "FrontlinesOverlay":
                case "FrontlineTrace":
                case "OverlayOpacity":
                case "GridRefreshInterval": overlayPending = true; break;
                case "NewsTicker": tickerPending = true; break;
                case "NewsTickerSpeed": break;
                default: appearancePending = true; break;
            }
        }

        private void Update()
        {
            if (settings == null || failed || Application.isBatchMode || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + (screen == null ? 1f : 0.25f);
            ApplyPending();
            if (screen == null)
            {
                // A torn-down dock slot can destroy the screen root without a scene reset;
                // release the stale reservation so the panel can install again.
                if (claimed) ReleaseClaim();
                VirtualMFD mfd = MapMfdLookup.Resolve(SceneSingleton<DynamicMap>.i?.maximizedMapCanvas);
                if (mfd == null) return;
                try { Install(mfd); }
                catch (Exception error)
                {
                    ResetForScene();
                    failed = true;
                    logger?.LogWarning("SET panel installation failed: " + error);
                }
            }
            bool mapOpen = DynamicMap.mapMaximized &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            bool visible = screen != null && screen.isActive && mapOpen;

            // Vanilla's close path is authoritative for showing. This keeps the surface in
            // step with the screen's own state, so a close path that skips CloseScreen can no
            // longer leave the settings panel floating over the cockpit.
            if (surface != null && surface.activeSelf != visible) surface.SetActive(visible);

            if (visible)
            {
                if (dirty || !wasVisible) RefreshPanel();
                string echo = Time.unscaledTime < actionEchoUntil ? actionEcho : null;
                string ambient = pageScrolls[Mathf.Clamp(shell.Page, 0, pageScrolls.Length - 1)]
                    ? "Saved automatically. Scroll for more; hover for help."
                    : "Saved automatically. Hover a control for help.";
                shell?.WriteStatus(null, echo ?? MapPicker.Prompt,
                    shell.Page == 2 ? MfdMapDeck.WallpaperStatus : ambient);
            }
            wasVisible = visible;
        }

        private void Install(VirtualMFD mfd)
        {
            if (claimed) return;
            if (!MfdBezel.TryClaim(MfdSlots.Set, preferLeft: false, mfd,
                    out var buttons, out var screens, out int slot, out bool left)) return;
            claimed = true;

            var template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
            if (template == null)
            {
                MfdBezel.Release(MfdSlots.Set);
                claimed = false;
                return;
            }

            var bezel = buttons[slot];
            var label = bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null)
            {
                var tmp = bezel.GetComponentInChildren<TMP_Text>(true);
                if (tmp is TextMeshProUGUI ugui) label = ugui;
            }
            var highlight = FindHighlight(bezel);
            if (label == null || highlight == null)
                throw new InvalidOperationException("SET bezel label or highlight is missing.");

            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            if (sourceText != null && sourceText.font != null) AvFont.Font = sourceText.font;

            root = new GameObject("BoscaliSummer.SET", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            var source = (RectTransform)template.transform;
            rect.SetParent(source.parent, false);
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.localScale = source.localScale;
            float height = AvScreen.ResolveHeight(
                source.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            AvKit.ClampIntoCanvas(rect);
            var content = new GameObject("Content", typeof(RectTransform), typeof(Image));
            var background = content.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;


            content.transform.SetParent(rect, false);
            var body = (RectTransform)content.transform;
            AvKit.Stretch(body);
            surface = content;

            shell = AvScreen.Build(
                body, "SET", new[] { "MAP", "STYLE", "IMAGE", "COCKPIT" }, null, 1,
                AvTokens.PanelWidth, height, page =>
                {
                    shell.DataBar.State.text = PageName(page);
                    nextTick = 0f;
                    RefreshPanel();
                });
            shell.DataBar.SetChip(0, "SAVED", true);
            shell.Status.richText = false;

            RectTransform displayPage = (RectTransform)shell.CreatePage(0, "DisplayPage").transform;
            BuildMapPage(displayPage, shell.Body);

            RectTransform deckPage = (RectTransform)shell.CreatePage(1, "DeckPage").transform;
            BuildStylePage(deckPage, shell.Body);
            var imagePage = (RectTransform)shell.CreatePage(2, "ImagePage").transform;
            BuildImagePage(imagePage, shell.Body);
            var viewPage = (RectTransform)shell.CreatePage(3, "ViewPage").transform;
            BuildViewPage(viewPage, shell.Body);

            shell.SetPage(0);

            screen = root.AddComponent<MFDScreen>();
            screen.shortName = MfdSlots.Set;
            screen.displayPanel = content;
            screen.aircraftOnly = false;
            screen.label = label;
            screen.highlight = highlight;
            boundScreens = screens;
            boundSlot = slot;
            if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                throw new InvalidOperationException("SET bezel changed before binding.");

            if (DynamicMap.mapMaximized)
            {
                var dynMap = SceneSingleton<DynamicMap>.i;
                if (dynMap != null) MfdRailPatch.OnStructureChanged(dynMap);
            }

            MfdMapDeck.ApplyAppearance(settings);
            RefreshPanel();
            logger?.LogInfo("SET MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
        }

        private static readonly string[] PageNames =
            { "TACTICAL DISPLAY", "CONSOLE SURFACE", "BACKGROUND IMAGERY", "COCKPIT VIEW" };

        private readonly bool[] pageScrolls = new bool[4];

        private static string PageName(int page) =>
            page >= 0 && page < PageNames.Length ? PageNames[page] : PageNames[0];

        // Compact row geometry: the toggle and step buttons match the inline control size
        // the other panels use, so a settings page reads as an instrument instead of a
        // wall of boxes.
        private const float RowHeight = 46f;
        private const float RowPitch = 52f;
        private const float ToggleValueWidth = 78f;
        private const float StepButtonWidth = 38f;
        private const float StepValueWidth = 96f;

        // Pages are built once. Dependencies disable controls without rebuilding the tree.
        private RectTransform Page(int page, RectTransform parent, Rect body, int rows, int sections, out Rect area)
        {
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));
            float contentHeight = rows * RowPitch + sections * 30f + 42f;
            if (page >= 0 && page < pageScrolls.Length) pageScrolls[page] = contentHeight > body.height;
            return AvScreen.Scroll(parent, body, contentHeight, out area);
        }

        /// <summary>
        /// A numbered section heading hanging off the spine, like the theater panels:
        /// index in accent, name in primary, status on the right, hairline under.
        /// </summary>
        private static void Heading(RectTransform parent, ref Rect area, string index, string title, string note)
        {
            const float height = 24f;
            var rect = new Rect(area.x, area.y, area.width, height);

            AvStyled.SpineTick(parent, rect.x, rect.y - 13f);

            TMP_Text number = AvStyled.Label(parent, new Rect(rect.x + 12f, rect.y, 30f, 16f), index, "section-title");
            number.color = AvTheme.Accent;
            AvStyled.Label(parent, new Rect(rect.x + 42f, rect.y, Mathf.Max(0f, rect.width - 42f - 140f), 16f),
                title, "row-name");
            if (!string.IsNullOrEmpty(note))
                AvStyled.Label(parent, new Rect(rect.x + rect.width - 140f, rect.y, 140f, 16f), note,
                    "section-title-note", align: TextAlignmentOptions.MidlineRight);
            AvKit.Rule(parent, new Rect(rect.x, rect.y - 20f, rect.width, 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.5f)));

            area.y -= height + 6f;
        }

        private static Rect TakeRow(ref Rect area)
        {
            var row = new Rect(area.x, area.y, area.width, RowHeight);
            area.y -= RowPitch;
            return row;
        }

        private void BuildMapPage(RectTransform parent, Rect body)
        {
            parent = Page(0, parent, body, 6, 3, out var area);

            Heading(parent, ref area, "01", "DISPLAY", "CONSOLE");
            Toggle(parent, TakeRow(ref area), "EXPANDED LAYOUT",
                "Use the full map console. OFF restores the native layout.",
                () => settings.ExpandedMapUi.Value, v => settings.ExpandedMapUi.Value = v);

            Heading(parent, ref area, "02", "OVERLAYS", "FRONTLINES");
            Toggle(parent, TakeRow(ref area), "CONTROL FIELD",
                "Show faction control and contested sectors.",
                () => settings.FrontlinesOverlay.Value, v => settings.FrontlinesOverlay.Value = v);
            Toggle(parent, TakeRow(ref area), "FRONT LINE",
                "Draw the front line trace above the control field.",
                () => settings.FrontlineTrace.Value, v => settings.FrontlineTrace.Value = v,
                () => settings.FrontlinesOverlay.Value, "Turn on the control field first.");
            Percent(parent, TakeRow(ref area), "FRONTLINE STRENGTH", settings.OverlayOpacity, .1f, 1f, .05f,
                () => settings.FrontlinesOverlay.Value, "Turn on the control field first.");
            Stepper(parent, TakeRow(ref area), "UPDATE INTERVAL",
                () => settings.GridRefreshInterval.Value.ToString("0.0") + " s",
                d => settings.GridRefreshInterval.Value = Mathf.Clamp(
                    Mathf.Round((settings.GridRefreshInterval.Value + d * .1f) * 10f) / 10f, .2f, 2f),
                () => settings.GridRefreshInterval.Value > .201f,
                () => settings.GridRefreshInterval.Value < 1.999f,
                "Longer intervals reduce CPU work. Recommended: 0.5 s.",
                () => settings.FrontlinesOverlay.Value, "Turn on the control field first.");

            Heading(parent, ref area, "03", "TERRAIN", "SATELLITE");
            Toggle(parent, TakeRow(ref area), "TERRAIN IMAGE",
                "Show the satellite terrain beneath map symbols.",
                () => settings.MapTerrainImage.Value, v => settings.MapTerrainImage.Value = v,
                () => settings.ExpandedMapUi.Value, "Turn on expanded layout first.");
            Percent(parent, TakeRow(ref area), "TERRAIN STRENGTH", settings.MapTerrainOpacity, .1f, 1f, .1f,
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                "Enable expanded layout and terrain image first.");
        }

        private void BuildStylePage(RectTransform parent, Rect body)
        {
            parent = Page(1, parent, body, 6, 2, out var area);

            Heading(parent, ref area, "01", "SURFACE", "DECK");
            Percent(parent, TakeRow(ref area), "CONSOLE OPACITY", settings.DeckOpacity, .1f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, "Turn on expanded layout first.");
            Stepper(parent, TakeRow(ref area), "BACKGROUND",
                () => SettingsChoices.BackgroundName(settings.DeckGrid.Value, settings.CheckerboardOverlay.Value,
                    settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value),
                d => SetBackground(SettingsChoices.CycleBackground(settings.DeckGrid.Value,
                    settings.CheckerboardOverlay.Value, settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value, d)),
                () => true, () => true,
                "Choose one decoration: plain, grid, checker, hexagon, carbon, radar or custom image. MIXED preserves your old combination.",
                () => settings.ExpandedMapUi.Value, "Turn on expanded layout first.");
            Percent(parent, TakeRow(ref area), "CHECKER STRENGTH", settings.CheckerboardOpacity, .02f, .4f, .02f,
                () => settings.ExpandedMapUi.Value && settings.CheckerboardOverlay.Value,
                "Choose CHECKER on STYLE first.");
            Percent(parent, TakeRow(ref area), "MAP DARKENING", settings.MapTrayOpacity, 0f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, "Turn on expanded layout first.");

            Heading(parent, ref area, "02", "DISPATCHES", "WIRE");
            Toggle(parent, TakeRow(ref area), "NEWS TICKER", "Show theater dispatches above the map.",
                () => settings.NewsTickerEnabled.Value, v => settings.NewsTickerEnabled.Value = v,
                () => settings.ExpandedMapUi.Value, "Turn on expanded layout first.");
            Stepper(parent, TakeRow(ref area), "TICKER SPEED",
                () => settings.NewsTickerSpeed.Value.ToString("0") + " px/s",
                d => settings.NewsTickerSpeed.Value = Mathf.Clamp(settings.NewsTickerSpeed.Value + d * 15f, 15f, 150f),
                () => settings.NewsTickerSpeed.Value > 15f, () => settings.NewsTickerSpeed.Value < 150f,
                "Lower speeds are easier to read. Disable NEWS TICKER to stop motion.",
                () => settings.ExpandedMapUi.Value && settings.NewsTickerEnabled.Value,
                "Enable expanded layout and news ticker first.");
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

        private void BuildImagePage(RectTransform parent, Rect body)
        {
            parent = Page(2, parent, body, 5, 1, out var area);

            Heading(parent, ref area, "01", "LOCAL IMAGERY", "PNG / JPEG");
            Percent(parent, TakeRow(ref area), "IMAGE STRENGTH", settings.BackgroundImageOpacity, .05f, 1f, .05f,
                ImageEnabled, "Choose an image background on STYLE first.");
            Stepper(parent, TakeRow(ref area), "IMAGE FILE", MfdMapDeck.GetCurrentWallpaperFileName,
                MfdMapDeck.CycleCustomWallpaper,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                "Local PNG/JPEG files. Use RESCAN after adding or replacing files.", CustomEnabled,
                "Choose CUSTOM on STYLE. Add files to BepInEx/config/BoscaliSummer/wallpapers.");
            string[] fits = { "COVER", "FIT", "STRETCH" };
            Stepper(parent, TakeRow(ref area), "IMAGE FIT",
                () => fits[Mathf.Clamp(settings.WallpaperFitMode.Value, 0, 2)],
                d => settings.WallpaperFitMode.Value = (settings.WallpaperFitMode.Value + d + 3) % 3,
                () => true, () => true, "COVER crops; FIT keeps the full image; STRETCH fills the screen.",
                CustomEnabled, "Choose CUSTOM on STYLE first.");
            var scan = AvStyled.Button(parent, TakeRow(ref area), "RESCAN LOCAL FILES  →", "btn", () =>
            {
                MfdMapDeck.RescanWallpapers();
                Echo("RESCAN — " + MfdMapDeck.WallpaperStatus);
                Changed();
            }, AvButtonStyle.Primary)
                .WithTooltip("Scan up to 512 directory entries; images are limited to 16 MB and 4096 pixels per side.");
            refreshers.Add(() =>
            {
                scan.SetEnabled(CustomEnabled());
                scan.WithTooltip(CustomEnabled()
                    ? "Scan up to 512 entries. PNG/JPEG: 16 MB and 4096 pixels per side."
                    : "Choose CUSTOM on STYLE first.");
            });
        }

        /// <summary>
        /// Cockpit-side presentation: the third-person HUD and cameras published by QoL
        /// through <see cref="IThirdPersonHud"/>, plus Command's own radial preset page.
        /// Every row here writes live state; the camera rows wait for the HUD they belong to.
        /// </summary>
        private void BuildViewPage(RectTransform parent, Rect body)
        {
            parent = Page(3, parent, body, 5, 3, out var area);
            ModServices.TryGet(out IThirdPersonHud hud);

            Heading(parent, ref area, "01", "HUD", "THIRD PERSON");
            Toggle(parent, TakeRow(ref area), "THIRD-PERSON HUD",
                "Show the compact flight overlay in external orbit and chase views.",
                () => hud != null && hud.IsEnabled, v => { if (hud != null && hud.IsEnabled != v) hud.Toggle(); },
                () => hud != null, "HUD service unavailable in this scene.");
            Toggle(parent, TakeRow(ref area), "HIDE PITCH LADDER",
                "Hide the floating pitch ladder in third person, keeping reticle, ammo and radar.",
                () => hud != null && hud.HidePitchLadder, v => { if (hud != null) hud.HidePitchLadder = v; },
                () => hud != null && hud.IsEnabled, "Turn on third-person HUD first.");

            Heading(parent, ref area, "02", "CAMERA", "CHASE");
            Toggle(parent, TakeRow(ref area), "TARGET CAMERA",
                "Show the native target camera feed in third person while contacts are selected.",
                () => hud != null && hud.CameraFeedEnabled, v => { if (hud != null) hud.CameraFeedEnabled = v; },
                () => hud != null && hud.IsEnabled, "Turn on third-person HUD first.");
            Toggle(parent, TakeRow(ref area), "FLIGHT CAMERA",
                "Smooth aircraft-relative orbit and rear chase framing with a steady horizon.",
                () => hud != null && hud.FlightCameraEnabled, v => { if (hud != null) hud.FlightCameraEnabled = v; },
                () => hud != null && hud.IsEnabled, "Turn on third-person HUD first.");

            Heading(parent, ref area, "03", "TARGETING", "RADIAL");
            Toggle(parent, TakeRow(ref area), "RADIAL PRESETS",
                "Offer the TGT quick slots as a page in the native cockpit radial menu.",
                () => settings.TargetPresetWheel.Value, v => settings.TargetPresetWheel.Value = v);
        }

        private void Percent(RectTransform parent, Rect area, string title, ConfigEntry<float> entry,
            float min, float max, float step, Func<bool> enabled, string reason)
        {
            Stepper(parent, area, title, () => entry.Value.ToString("P0"),
                d => entry.Value = Mathf.Clamp(Mathf.Round((entry.Value + d * step) * 100f) / 100f, min, max),
                () => entry.Value > min + .001f, () => entry.Value < max - .001f,
                "Adjust " + title.ToLowerInvariant() + ".", enabled, reason);
        }

        private static AvNode ToggleRow(string name) =>
            AvBox.Row(name).Height(RowHeight).Pad(14f, 5f, 8f, 5f).Gaps(8f)
                .Add(AvBox.Cell("label").Grow()).Add(AvBox.Cell("value").Width(ToggleValueWidth));

        private static AvNode StepperRow(string name) =>
            AvBox.Row(name).Height(RowHeight).Pad(14f, 5f, 8f, 5f).Gaps(6f)
                .Add(AvBox.Cell("label").Grow()).Add(AvBox.Cell("minus").Width(StepButtonWidth))
                .Add(AvBox.Cell("value").Width(StepValueWidth)).Add(AvBox.Cell("plus").Width(StepButtonWidth));

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].gameObject != button.gameObject)
                    return images[i];
            }
            return button.GetComponent<Image>();
        }

        private void Changed()
        {
            ApplyPending();
            dirty = true;
            RefreshPanel();
        }

        // The row's own state, before hover: an active control carries an accent marker
        // and wash, so ON reads at a glance without relying on the value text alone.
        private static Color LatchedRow => AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), 0.16f, 0.30f));
        private static Color LatchedRowHover => AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), 0.24f, 0.42f));
        private static Color HoverRow => AvTheme.Unity(AvTokens.SurfaceRaised.WithAlpha(0.5f));

        /// <summary>
        /// Confirm the action on the status strip for a moment. Text, not colour alone:
        /// the value label changes too, so the cue survives a colour-blind reading.
        /// </summary>
        private void Echo(string text)
        {
            actionEcho = text;
            actionEchoUntil = Time.unscaledTime + 1.6f;
        }

        /// <summary>
        /// A full-row hover target behind the controls: the help has to appear whether the
        /// pointer is over the label or the value box, and the row lights as one control.
        /// </summary>
        private static AvTooltipTarget RowHover(RectTransform parent, AvRect area, string tooltip)
        {
            Image background = AvKit.Panel(parent, area.ToUnity(), Color.clear);
            background.raycastTarget = true;
            var target = background.gameObject.AddComponent<AvTooltipTarget>();
            target.Initialise(tooltip);
            target.SetTint(background, Color.clear, HoverRow);
            return target;
        }

        private void ApplyPending()
        {
            if (layoutPending) MfdRailPatch.Reconcile();
            if (appearancePending || layoutPending) MfdMapDeck.ApplyAppearance(settings);
            if (overlayPending) overlay?.SyncSettings();
            if (tickerPending)
            {
                var map = SceneSingleton<DynamicMap>.i;
                if (settings.ExpandedMapUi.Value && DynamicMap.mapMaximized && map != null &&
                    MfdLayout.TryResolve(map.maximizedMapCanvas, out var columns))
                    MfdNewsTicker.Ensure(map.maximizedMapCanvas, columns, settings);
            }
            appearancePending = overlayPending = layoutPending = tickerPending = false;
        }

        private void Toggle(RectTransform parent, Rect area, string title, string tooltip,
            Func<bool> get, Action<bool> set, Func<bool> enabled = null, string reason = null)
        {
            var row = ToggleRow("row").Arrange(area);
            var hover = RowHover(parent, row.Rect, tooltip);
            Image marker = AvKit.Rule(parent,
                new Rect(row.Rect.X + 6f, row.Rect.Y - 8f, 3f, row.Rect.Height - 16f), Color.clear);
            AvStyled.Label(parent, row.At("label"), title, "row-value", align: TextAlignmentOptions.MidlineLeft);
            var button = AvStyled.Button(parent, row.At("value"), "", "btn", () =>
            {
                if (enabled != null && !enabled()) return;
                set(!get());
                Echo(title + " — " + (get() ? "ON" : "OFF"));
                Changed();
            });
            refreshers.Add(() =>
            {
                bool available = enabled == null || enabled();
                bool on = get();
                button.SetEnabled(available);
                button.SetText(on ? "ON" : "OFF");
                button.SetLatched(on);
                button.WithTooltip(available ? tooltip : reason);
                hover.SetText(available ? tooltip : reason);
                marker.color = on ? AvTheme.Accent : Color.clear;
                hover.SetColors(on ? LatchedRow : Color.clear, on ? LatchedRowHover : HoverRow);
            });
        }

        private void Stepper(RectTransform parent, Rect area, string title, Func<string> get,
            Action<int> change, Func<bool> decrease, Func<bool> increase, string tooltip,
            Func<bool> enabled = null, string reason = null)
        {
            var row = StepperRow("row").Arrange(area);
            var hover = RowHover(parent, row.Rect, tooltip);
            AvStyled.Label(parent, row.At("label"), title, "row-value", align: TextAlignmentOptions.MidlineLeft);
            Action<int> click = d =>
            {
                if (enabled != null && !enabled() || !(d < 0 ? decrease() : increase())) return;
                change(d);
                Echo(title + " — " + get());
                Changed();
            };
            var minus = AvStyled.Button(parent, row.At("minus"), "-", "btn", () => click(-1));
            var value = AvStyled.Label(parent, row.At("value"), "", "row-value", align: TextAlignmentOptions.Center);
            value.enableWordWrapping = false;
            value.overflowMode = TextOverflowModes.Ellipsis;
            value.richText = false;
            var plus = AvStyled.Button(parent, row.At("plus"), "+", "btn", () => click(1));
            minus.GetComponentInChildren<TMP_Text>().fontSize = 15f;
            plus.GetComponentInChildren<TMP_Text>().fontSize = 15f;
            refreshers.Add(() =>
            {
                bool available = enabled == null || enabled();
                minus.SetEnabled(available && decrease());
                plus.SetEnabled(available && increase());
                string text = available ? get() : "--";
                if (value.text != text) value.text = text;
                minus.WithTooltip(available ? tooltip + " Previous / decrease. " + text : reason);
                plus.WithTooltip(available ? tooltip + " Next / increase. " + text : reason);
                hover.SetText(available ? tooltip : reason);
            });
        }

        private void RefreshPanel()
        {
            foreach (Action refresh in refreshers) refresh();
            dirty = false;
        }

        public void ResetForScene()
        {
            ReleaseClaim();
            if (root != null) Destroy(root);
            root = null;
            screen = null;
            failed = false;
            nextTick = 0f;
            dirty = true;
            wasVisible = false;
            actionEcho = null;
            actionEchoUntil = 0f;
            AvUiSound.Reset();
        }

        /// <summary>
        /// Drop the bezel reservation and every reference into the screen tree without
        /// touching the service itself. A torn-down dock slot can destroy the root before a
        /// scene reset; releasing here lets the next Update install a fresh panel.
        /// </summary>
        private void ReleaseClaim()
        {
            MfdBezel.Release(MfdSlots.Set);
            if (boundScreens != null && boundSlot >= 0 && boundSlot < boundScreens.Count &&
                ReferenceEquals(boundScreens[boundSlot], screen)) boundScreens[boundSlot] = null;
            boundScreens = null;
            boundSlot = -1;
            claimed = false;
            surface = null;
            shell = null;
            refreshers.Clear();
        }

        private void OnDestroy()
        {
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            ResetForScene();
        }
    }
}
