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
    /// <summary>
    /// The SET console (kit v2). One flat icon-tab strip carries every page instead of a
    /// nested CLIENT/SERVER tab pair: audience is still enforced per row (host-authoritative
    /// rows disable and explain themselves for a remote client), so the extra tab tier added
    /// nothing kit v2's per-row gating doesn't already say.
    /// </summary>
    internal sealed partial class SettingsMfdPanel : MonoBehaviour, ISceneService
    {
        private const int PageMap = 0, PageDisplay = 1, PageBackdrop = 2, PageCamera = 3, PageHud = 4, PagePerf = 5,
                           PageTasking = 6, PageHostSettings = 7, PageEffects = 8;
        private const int PageCount = 9;

        private static readonly string[] PageNames =
        {
            "TACTICAL DISPLAY", "DISPLAY STYLE", "BACKGROUND IMAGERY", "CAMERA & CONTROLS",
            "HUD OVERLAYS", "PERFORMANCE", "FACTION TASKING", "HOST SETTINGS", "WORLD EFFECTS"
        };

        private static string PageName(int page) => page >= 0 && page < PageNames.Length ? PageNames[page] : PageNames[0];

        private CommandSettings settings;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private HostSettingsBoard hostSettings;
        private ClientSettingsBoard clientSettings;
        private GameObject root;
        private GameObject surface;
        private MFDScreen screen;
        private AvConsole con;
        private AvChip savedChip;
        private AvChip roleChip;
        private List<MFDScreen> boundScreens;
        private int boundSlot = -1;
        private bool claimed;
        private bool failed;
        private float nextTick;
        private string actionEcho;
        private float actionEchoUntil;
        private bool appearancePending;
        private bool overlayPending;
        private bool layoutPending;
        private bool tickerPending;

        public void Configure(CommandSettings config, ManualLogSource log, ComMapOverlay mapOverlay = null,
            HostSettingsBoard hostSettingsBoard = null, ClientSettingsBoard clientSettingsBoard = null)
        {
            settings = config;
            overlay = mapOverlay;
            logger = log;
            hostSettings = hostSettingsBoard;
            clientSettings = clientSettingsBoard;
            MfdMapDeck.Configure(config);
            ApplyDisplayEffects();
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            configFile = config.ExpandedMapUi.ConfigFile;
            configFile.SettingChanged += OnSettingChanged;
        }

        private ConfigFile configFile;

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            // Live world-state side effects (display glass, map deck, news ticker, overlay
            // sync) must still react when F1 or another session changes a key, whether or not
            // this panel is even installed. The console's own row values re-read the config
            // on their own next Slow tick, so no "dirty" flag is needed for those.
            string section = args.ChangedSetting.Definition.Section;
            if (section != "Command" && section != "Hud" && section != "Avionics" &&
                section != "Weather" && section != "Garrisons" && section != "Performance") return;
            if (section != "Command") return;
            switch (args.ChangedSetting.Definition.Key)
            {
                case "ExpandedMapUi": layoutPending = true; break;
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
        }

        private static bool HostAuthority() => GameAccess.IsServer();

        private string AmbientStatus()
        {
            int page = con?.CurrentPage ?? PageMap;
            if (page == PageTasking)
                return HostAuthority()
                    ? "Host tasking board. Refreshes while visible."
                    : "Host only. The host issues faction tasking.";
            if (page == PageHostSettings || page == PageEffects)
                return HostAuthority()
                    ? "Host controls are saved automatically."
                    : "Host only. The host's values apply to this server.";
            if (page == PageBackdrop) return MfdMapDeck.WallpaperStatus;
            if (page == PagePerf) return "Changes apply now. No mission or game restart.";
            return "Saved automatically. Hover a control for help.";
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

            root = new GameObject("BoscaliSummer.SET", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            var source = (RectTransform)template.transform;
            rect.SetParent(source.parent, false);
            rect.anchorMin = source.anchorMin;
            rect.anchorMax = source.anchorMax;
            rect.pivot = source.pivot;
            rect.localScale = source.localScale;
            float height = ResolvePanelHeight(source.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rect.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
            ClampPanelIntoCanvas(rect);

            // The dock naming convention ("Content" marks a mod-built panel, vs. a stock
            // screen's "DisplayPanel") predates kit v2 and is enforced elsewhere
            // (MfdPanelDock.IsStockScreen); it is unrelated to AvConsole's own chrome, which
            // is built as a nested child of this wrapper.
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(rect, false);
            var body = (RectTransform)content.transform;
            AvLay.Fill(body);
            surface = content;

            con = AvConsole.Build(body, "SET", PageName(0), PageCount, AvTokens.PanelWidth, height);
            con.PageChanged += index => con.SetTitle(PageName(index));

            AvChip[] chips = con.Chips(2);
            savedChip = chips[0];
            roleChip = chips[1];
            savedChip.Set("SAVED", AvState.Ready);

            con.Tabs(
                (AvIcon.Map2, "MAP"),
                (AvIcon.Typography, "DISPLAY"),
                (AvIcon.Stack2, "BACKDROP"),
                (AvIcon.Camera, "CAMERA"),
                (AvIcon.Eye, "HUD"),
                (AvIcon.Gauge, "PERF"),
                (AvIcon.ListDetails, "TASKING"),
                (AvIcon.Settings, "HOST"),
                (AvIcon.CloudRain, "EFFECTS"));

            BuildMapPage(con.Page(PageMap), PageMap);
            BuildDisplayPage(con.Page(PageDisplay), PageDisplay);
            BuildBackdropPage(con.Page(PageBackdrop), PageBackdrop);
            BuildCameraPage(con.Page(PageCamera), PageCamera);
            BuildHudPage(con.Page(PageHud), PageHud);
            BuildPerformancePage(con.Page(PagePerf), PagePerf);
            BuildTaskingPage(con.Page(PageTasking), PageTasking);
            BuildHostSettingsPage(con.Page(PageHostSettings), PageHostSettings, HostSettingsPage.Settings, "HOST SETTINGS");
            BuildHostSettingsPage(con.Page(PageEffects), PageEffects, HostSettingsPage.Effects, "EFFECTS");

            con.Ticker.Add(-1, AvTickRate.Slow, RefreshChrome);
            con.Finish();

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
            logger?.LogInfo("SET MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
        }

        private void RefreshChrome()
        {
            AvUiSound.Volume = settings.UiSoundVolume.Value;
            bool host = HostAuthority();
            roleChip.Set(host ? "HOST" : "CLIENT", host ? AvState.Ready : AvState.Inert);
            string echo = Time.unscaledTime < actionEchoUntil ? actionEcho : null;
            con.Footer.Set(echo ?? AmbientStatus(), echo != null ? AvState.Info : AvState.Inert);
        }

        /// <summary>The height this panel should take, given the bay it was parented into (mirrors the
        /// v1 screen-height measurement so the panel occupies the same footprint).</summary>
        private static float ResolvePanelHeight(RectTransform parent, float min, float max)
        {
            if (max < min) max = min;
            if (parent == null) return min;

            float available = parent.rect.height;
            RectTransform cursor = parent;
            for (int i = 0; i < 4 && available <= 1f && cursor != null; i++)
            {
                cursor = cursor.parent as RectTransform;
                if (cursor != null) available = cursor.rect.height;
            }

            if (available <= 1f) return min;
            return Mathf.Clamp(Mathf.Floor(available), min, max);
        }

        /// <summary>Keeps a panel taller than its bezel bay fully on-screen (mirrors the v1 canvas-clamp helper).</summary>
        private static void ClampPanelIntoCanvas(RectTransform panel, float margin = 8f)
        {
            if (panel == null) return;
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null || panel.parent == null) return;

            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRt.InverseTransformPoint(corners[i]);
                if (local.x < minX) minX = local.x;
                if (local.x > maxX) maxX = local.x;
                if (local.y < minY) minY = local.y;
                if (local.y > maxY) maxY = local.y;
            }

            Rect bounds = canvasRt.rect;
            float dx = 0f;
            if (minX < bounds.xMin + margin) dx = bounds.xMin + margin - minX;
            else if (maxX > bounds.xMax - margin) dx = bounds.xMax - margin - maxX;
            float dy = 0f;
            if (maxY > bounds.yMax - margin) dy = bounds.yMax - margin - maxY;
            else if (minY < bounds.yMin + margin) dy = bounds.yMin + margin - minY;
            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f)) return;

            Vector3 world = canvasRt.TransformVector(new Vector3(dx, dy, 0f));
            Vector3 local2 = panel.parent.InverseTransformVector(world);
            panel.anchoredPosition += new Vector2(local2.x, local2.y);
        }

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

        /// <summary>Confirm the action on the footer for a moment. Text, not colour alone.</summary>
        private void Echo(string text)
        {
            actionEcho = text;
            actionEchoUntil = Time.unscaledTime + 1.6f;
        }

        private bool EffectsEnabled() => settings.DisplayEffects.Value;
        private static string EffectsDisabled() => "Turn on DISPLAY EFFECTS first.";

        private void ApplyDisplayEffects()
        {
            AvDisplayGlass.Configure(settings.DisplayEffects.Value, settings.DisplayGlass.Value,
                settings.DisplayAutoLight.Value, settings.DisplayScanlines.Value,
                settings.DisplayVignette.Value, settings.DisplayTint.Value, settings.DisplayTintStrength.Value);
        }

        private void ApplyPending()
        {
            if (appearancePending || layoutPending) ApplyDisplayEffects();
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

        private void Changed() => ApplyPending();

        // ------------------------------------------------------------------ row helpers
        //
        // Every SET row used to be one of two shapes: a latched ON/OFF, or a "- value +"
        // stepper, both built from v1's AvBox/AvKit/AvStyled primitives with hand-measured
        // geometry (Page/Heading/TakeRow/rowPitch). Kit v2's AvFlow measures and places every
        // part itself, so that geometry is gone; these two helpers now build one AvRow per
        // setting (state rail, name, always-visible help/reason line, and either the ON/OFF
        // word or a pair of trailing +/- AvControls), refreshed at the console's 2 Hz "Slow"
        // tier while their page is open.

        private AvRow Toggle(AvFlow flow, int page, string title, string help, Func<bool> get, Action<bool> set,
            Func<bool> enabled = null, Func<string> reason = null)
        {
            AvRow row = null;
            void RefreshRow()
            {
                bool avail = enabled == null || enabled();
                bool on = get();
                row.Interactable = avail;
                row.Set(title, avail ? help : (reason != null ? reason() : help), on ? "ON" : "OFF",
                    on ? AvState.Ready : AvState.Inert);
            }
            row = new AvRow(flow.Content, () =>
            {
                if (enabled != null && !enabled()) return;
                set(!get());
                Echo(title + " — " + (get() ? "ON" : "OFF"));
                RefreshRow();
                Changed();
            });
            flow.Add(row);
            RefreshRow();
            flow.Ticker.Add(page, AvTickRate.Slow, RefreshRow);
            return row;
        }

        private AvRow Stepper(AvFlow flow, int page, string title, Func<string> get, Action<int> change,
            Func<bool> decrease, Func<bool> increase, string help, Func<bool> enabled = null,
            Func<string> reason = null, bool readOnlyValue = false)
        {
            var row = new AvRow(flow.Content);
            AvControl minus = row.AddTrailing(new AvControl.Spec("", () =>
            {
                if ((enabled != null && !enabled()) || !decrease()) return;
                change(-1);
                Echo(title + " — " + get());
                Changed();
            }, AvButtonStyle.Quiet, AvIcon.Minus));
            AvControl plus = row.AddTrailing(new AvControl.Spec("", () =>
            {
                if ((enabled != null && !enabled()) || !increase()) return;
                change(1);
                Echo(title + " — " + get());
                Changed();
            }, AvButtonStyle.Quiet, AvIcon.Plus));
            void Refresh()
            {
                bool avail = enabled == null || enabled();
                minus.Interactable = avail && decrease();
                plus.Interactable = avail && increase();
                string text = avail || readOnlyValue ? get() : "--";
                row.Set(title, avail ? help : (reason != null ? reason() : help), text, AvState.Info);
            }
            flow.Add(row);
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            return row;
        }

        private AvRow Percent(AvFlow flow, int page, string title, ConfigEntry<float> entry,
            float min, float max, float step, Func<bool> enabled, Func<string> reason) =>
            Stepper(flow, page, title, () => AvNum.Percent(entry.Value),
                d => entry.Value = Mathf.Clamp(Mathf.Round((entry.Value + d * step) * 100f) / 100f, min, max),
                () => entry.Value > min + .001f, () => entry.Value < max - .001f,
                "Adjust " + title.ToLowerInvariant() + ".", enabled, reason);

        /// <summary>A single wrapped line of prose/status copy, built from kit v2 primitives (a kit gap:
        /// there is no bare "label part" in the v2 set — every text-bearing part carries its own chrome).</summary>
        private sealed class NoteLine : AvPart
        {
            private readonly TMP_Text text;

            public NoteLine(RectTransform parent, AvTextRole role = AvTextRole.ProseSmall)
            {
                Rect = AvLay.Child(parent, "Note");
                text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public void Set(string t) { if (text.text != (t ?? "")) text.text = t ?? ""; }

            public override float Measure(float width) => Mathf.Max(18f, AvText.Height(text, width));

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(text.rectTransform, 0f, 0f, s.W, s.H);
            }

            public override void Restyle() =>
                text.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        }

        // ------------------------------------------------------------------ CLIENT pages

        private void BuildMapPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Map2, "DISPLAY", "CONSOLE");
            Toggle(flow, page, "EXPANDED LAYOUT", "Use the full map console. OFF restores the native layout.",
                () => settings.ExpandedMapUi.Value, v => settings.ExpandedMapUi.Value = v);

            flow.Section(AvIcon.Refresh, "SECTOR FIELD", "REFRESH");
            Stepper(flow, page, "UPDATE INTERVAL",
                () => AvNum.Seconds(settings.GridRefreshInterval.Value, 1),
                d => settings.GridRefreshInterval.Value = Mathf.Clamp(
                    Mathf.Round((settings.GridRefreshInterval.Value + d * .1f) * 10f) / 10f, .2f, 2f),
                () => settings.GridRefreshInterval.Value > .201f,
                () => settings.GridRefreshInterval.Value < 1.999f,
                "Longer intervals reduce CPU work. Recommended: 0.5 s.");

            flow.Section(AvIcon.Satellite, "TERRAIN", "SATELLITE");
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
        }

        private void BuildDisplayPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Typography, "DISPLAY FILTER", "LOCAL");
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
            flow.Buttons(new AvControl.Spec("RESET DISPLAY FILTER", () =>
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
            }));

            flow.Section(AvIcon.Settings, "PANEL THEME", "AVIONICS");
            var themeSeg = flow.Add(new AvSegmented(flow.Content, "THEME", new[] { "STEEL", "ACE", "PHOSPHOR" },
                () => (int)settings.AvionicsTheme.Value,
                i => { settings.AvionicsTheme.Value = (AvThemeId)i; Echo("THEME — " + settings.AvionicsTheme.Value); }));
            var motionCell = flow.Add(AvCell.Toggle(flow.Content, "REDUCED MOTION",
                "Snap every panel animation to its end state.",
                () => settings.AvionicsReducedMotion.Value,
                v => { settings.AvionicsReducedMotion.Value = v; Echo("REDUCED MOTION — " + (v ? "ON" : "OFF")); }));
            flow.Ticker.Add(page, AvTickRate.Slow, () => { themeSeg.Refresh(); motionCell.Refresh(); });

            flow.Section(AvIcon.Stack2, "SURFACE", "DECK");
            Percent(flow, page, "CONSOLE OPACITY", settings.DeckOpacity, .1f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Stepper(flow, page, "BACKGROUND",
                () => SettingsChoices.BackgroundName(settings.DeckGrid.Value, settings.CheckerboardOverlay.Value,
                    settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value),
                d => SetBackground(SettingsChoices.CycleBackground(settings.DeckGrid.Value,
                    settings.CheckerboardOverlay.Value, settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value, d)),
                () => true, () => true,
                "Choose one decoration: plain, grid, checker, hexagon, carbon, radar or custom image. MIXED preserves your old combination.",
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Percent(flow, page, "CHECKER STRENGTH", settings.CheckerboardOpacity, .02f, .4f, .02f,
                () => settings.ExpandedMapUi.Value && settings.CheckerboardOverlay.Value,
                () => "Choose CHECKER on DISPLAY first.");
            Percent(flow, page, "MAP DARKENING", settings.MapTrayOpacity, 0f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");

            flow.Section(AvIcon.Message2, "DISPATCHES", "WIRE");
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

        private void BuildBackdropPage(AvFlow flow, int page)
        {
            flow.Section(AvIcon.Satellite, "LOCAL IMAGERY", "PNG / JPEG");
            var cue = flow.Add(new NoteLine(flow.Content));
            AvControl openButton = flow.Buttons(new AvControl.Spec("OPEN MAP",
                () => con.SetPage(settings.ExpandedMapUi.Value ? PageDisplay : PageMap))).Controls[0];
            flow.Ticker.Add(page, AvTickRate.Slow, () =>
            {
                bool ready = CustomEnabled();
                openButton.Rect.gameObject.SetActive(!ready);
                openButton.Label = settings.ExpandedMapUi.Value ? "OPEN STYLE" : "OPEN MAP";
                cue.Set(ready ? "ADD PNG/JPEG FILES, THEN RESCAN"
                    : settings.ExpandedMapUi.Value ? "SELECT CUSTOM BACKGROUND ON DISPLAY"
                    : "TURN ON EXPANDED LAYOUT ON MAP");
            });

            Percent(flow, page, "IMAGE STRENGTH", settings.BackgroundImageOpacity, .05f, 1f, .05f,
                ImageEnabled, () => "Choose an image background on DISPLAY first.");
            Stepper(flow, page, "IMAGE FILE", MfdMapDeck.GetCurrentWallpaperFileName,
                MfdMapDeck.CycleCustomWallpaper,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                "Local PNG/JPEG files. Use RESCAN after adding or replacing files.", CustomEnabled,
                () => "Choose CUSTOM on DISPLAY. Add files to BepInEx/config/BoscaliSummer/wallpapers.");
            string[] fits = { "COVER", "FIT", "STRETCH" };
            Stepper(flow, page, "IMAGE FIT",
                () => fits[Mathf.Clamp(settings.WallpaperFitMode.Value, 0, 2)],
                d => settings.WallpaperFitMode.Value = (settings.WallpaperFitMode.Value + d + 3) % 3,
                () => true, () => true, "COVER crops; FIT keeps the full image; STRETCH fills the screen.",
                CustomEnabled, () => "Choose CUSTOM on DISPLAY first.");
            AvControl scan = flow.Buttons(new AvControl.Spec("RESCAN LOCAL FILES", () =>
            {
                MfdMapDeck.RescanWallpapers();
                Echo("RESCAN — " + MfdMapDeck.WallpaperStatus);
                Changed();
            }, AvButtonStyle.Primary)).Controls[0];
            flow.Ticker.Add(page, AvTickRate.Slow, () => scan.Interactable = CustomEnabled());
        }

        private void BuildCameraPage(AvFlow flow, int page)
        {
            ModServices.TryGet(out IHudBoard board);
            flow.Section(AvIcon.Camera, "CAMERA", "TARGET");
            Toggle(flow, page, "TARGET CAMERA",
                "Show the native target camera feed inset on the status panel while a target is selected.",
                () => board != null && board.CameraFeedEnabled, v => { if (board != null) board.CameraFeedEnabled = v; },
                () => board != null, () => "HUD service unavailable in this scene.");

            flow.Section(AvIcon.Target, "TARGETING", "RADIAL");
            Toggle(flow, page, "RADIAL PRESETS",
                "Offer the TGT quick slots as a page in the native cockpit radial menu.",
                () => settings.TargetPresetWheel.Value, v => settings.TargetPresetWheel.Value = v);
        }

        public void ResetForScene()
        {
            ReleaseClaim();
            if (root != null) Destroy(root);
            root = null;
            screen = null;
            failed = false;
            nextTick = 0f;
            actionEcho = null;
            actionEchoUntil = 0f;
            tasking = null;
            taskCards = null;
            nextTaskingRefresh = 0f;
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
            con = null;
            savedChip = null;
            roleChip = null;
            taskRequest = null;
            taskNote = null;
            taskList = null;
            tasking = null;
            taskCards = null;
        }

        private void OnDestroy()
        {
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            ResetForScene();
        }
    }
}
