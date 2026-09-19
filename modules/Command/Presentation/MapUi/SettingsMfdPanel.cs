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
    internal sealed partial class SettingsMfdPanel : MonoBehaviour, ISceneService
    {
        private const int TabClient = 0;
        private const int TabServer = 1;
        private const int ClientPageCount = 4;

        /// <summary>Display index of the SERVER page, after the four CLIENT sub-pages.</summary>
        private const int ServerDisplay = 4;

        private const int DisplayCount = ServerDisplay + 1;

        private CommandSettings settings;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private HostSettingsBoard hostSettings;
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
        private GameObject[] clientPages;
        private AvButton[] clientTabs;
        private int clientPage;

        public void Configure(CommandSettings config, ManualLogSource log, ComMapOverlay mapOverlay = null,
            HostSettingsBoard hostSettingsBoard = null)
        {
            settings = config;
            overlay = mapOverlay;
            logger = log;
            hostSettings = hostSettingsBoard;
            MfdMapDeck.Configure(config);
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            configFile = config.ExpandedMapUi.ConfigFile;
            configFile.SettingChanged += OnSettingChanged;
        }

        private ConfigFile configFile;

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            // Hud is the one section this panel does not own: the common HUD element reads its
            // own entries live, and this only repaints the rows so the panel is not left showing
            // a value the config file no longer holds.
            string section = args.ChangedSetting.Definition.Section;
            if (section != "Command" && section != "Hud") return;
            dirty = true;
            if (section == "Hud") return;
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
                // The SERVER page carries live host state (tasking clocks, host values), so it
                // refreshes on the tick; the CLIENT pages only when something actually changed.
                if (dirty || !wasVisible || shell.Page == TabServer) RefreshPanel();
                string echo = Time.unscaledTime < actionEchoUntil ? actionEcho : null;
                shell?.WriteStatus(null, echo ?? MapPicker.Prompt, AmbientStatus());
            }
            wasVisible = visible;
        }

        private static bool HostAuthority() => GameAccess.IsServer();

        private int DisplayIndex =>
            shell != null && shell.Page == TabServer ? ServerDisplay : clientPage;

        private string AmbientStatus()
        {
            if (shell != null && shell.Page == TabServer)
                return HostAuthority()
                    ? "Host settings apply immediately and are saved to the configuration file."
                    : "Host only. These settings are read-only on a remote client.";
            if (DisplayIndex == 2) return MfdMapDeck.WallpaperStatus;
            return pageScrolls[Mathf.Clamp(DisplayIndex, 0, DisplayCount - 1)]
                ? "Saved automatically. Scroll for more; hover for help."
                : "Saved automatically. Hover a control for help.";
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
                body, "SET", new[] { "CLIENT", "SERVER" }, null, 2,
                AvTokens.PanelWidth, height, page =>
                {
                    shell.DataBar.State.text = PageName(page == TabServer ? ServerDisplay : clientPage);
                    nextTick = 0f;
                    RefreshPanel();
                });
            shell.Tabs[TabClient].WithTooltip(
                "Client-local display settings: the map, console surface, background imagery and cockpit view.");
            shell.Tabs[TabServer].WithTooltip(
                "Host settings: faction tasking and every installed feature's host-authoritative options.");
            shell.DataBar.SetChip(0, "SAVED", true);
            shell.Status.richText = false;

            RectTransform clientRoot = (RectTransform)shell.CreatePage(TabClient, "ClientPage").transform;
            BuildClientArea(clientRoot, shell.Body);

            RectTransform serverRoot = (RectTransform)shell.CreatePage(TabServer, "ServerPage").transform;
            BuildServerPage(serverRoot, shell.Body);

            shell.SetPage(TabClient);

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
        {
            "TACTICAL DISPLAY", "CONSOLE SURFACE", "BACKGROUND IMAGERY", "COCKPIT VIEW",
            "SERVER SETTINGS"
        };

        private readonly bool[] pageScrolls = new bool[DisplayCount];

        private static string PageName(int page) =>
            page >= 0 && page < PageNames.Length ? PageNames[page] : PageNames[0];

        /// <summary>
        /// The CLIENT main tab: the four client-local pages behind a second, smaller tab
        /// strip. The main strip names the audience (CLIENT / SERVER); this one names the
        /// console surface, so a player reads the hierarchy in one glance.
        /// </summary>
        private void BuildClientArea(RectTransform page, Rect body)
        {
            const float barHeight = 26f;
            const float gap = 6f;
            string[] names = { "MAP", "STYLE", "IMAGE", "COCKPIT" };
            string[] hints =
            {
                "Map layout, overlays and terrain.",
                "Console surface, backdrop decoration and dispatches.",
                "Local background imagery and its rescans.",
                "Third-person HUD, camera and the shared cockpit HUD element.",
            };

            AvNode bar = AvBox.Row("subtabs").Height(barHeight);
            for (int i = 0; i < names.Length; i++) bar.Add(AvBox.Cell("c" + i).Grow());
            bar.Arrange(new Rect(body.x, body.y, body.width, barHeight));

            clientTabs = new AvButton[ClientPageCount];
            for (int i = 0; i < ClientPageCount; i++)
            {
                int index = i;
                clientTabs[i] = AvStyled.Button(page, bar.At("c" + i), names[i], "tab",
                    () => SetClientPage(index), AvButtonStyle.Tab)
                    .WithTooltip(hints[i]);
            }

            var area = new Rect(body.x, body.y - barHeight - gap, body.width, body.height - barHeight - gap);
            clientPages = new GameObject[ClientPageCount];
            for (int i = 0; i < ClientPageCount; i++)
            {
                var sub = new GameObject("ClientPage" + i, typeof(RectTransform));
                var rect = (RectTransform)sub.transform;
                rect.SetParent(page, false);
                AvKit.Stretch(rect);
                clientPages[i] = sub;
            }

            BuildMapPage((RectTransform)clientPages[0].transform, area);
            BuildStylePage((RectTransform)clientPages[1].transform, area);
            BuildImagePage((RectTransform)clientPages[2].transform, area);
            BuildViewPage((RectTransform)clientPages[3].transform, area);

            SetClientPage(0);
        }

        private void SetClientPage(int page)
        {
            clientPage = Mathf.Clamp(page, 0, ClientPageCount - 1);
            if (clientPages != null)
            {
                for (int i = 0; i < clientPages.Length; i++)
                    if (clientPages[i] != null) clientPages[i].SetActive(i == clientPage);
            }
            if (clientTabs != null)
            {
                for (int i = 0; i < clientTabs.Length; i++)
                    if (clientTabs[i] != null) clientTabs[i].SetLatched(i == clientPage);
            }
            if (shell == null) return;

            AvButton.ClearTooltip();
            if (shell.Page != TabServer) shell.DataBar.State.text = PageName(clientPage);
            nextTick = 0f;
            RefreshPanel();
        }

        // One row geometry for every page: a 10px state pip in the left gutter, the name
        // in a shared label column, and a control group of one width. A toggle puts its
        // state plate in the same slot a stepper's figure occupies, so ON/OFF and every
        // number line up on one value column across pages. The value column is wide
        // enough for a wallpaper filename and still shrinks to the micro floor when a
        // host string runs longer. RowHeight is the 30px token; the pitch is per page,
        // because Page() spreads a short page over the bay instead of pooling the empty
        // height under its last row.
        private const float PipSize = 6f;
        private const float CellGap = 4f;
        private const float StateWidth = 96f;
        private const float StepWidth = 26f;
        private const float RowRightPad = 8f;
        private const float RowInset = 2f; // keeps the 26px controls on the 30px row midline

        // Section headings: the number column is fixed, so every title on every page
        // starts on the same x, and the rule hangs a fixed distance below the labels.
        private const float HeadingHeight = 26f;
        private const float HeadingGap = 6f;
        private const float PageTail = 12f;

        /// <summary>Flow of the page currently being built, set by <see cref="Page"/>.</summary>
        private float rowPitch = AvTokens.RowPitch;
        private float sectionGap = HeadingGap;
        private int sectionsOnPage;

        // Pages are built once. Dependencies disable controls without rebuilding the tree.
        private RectTransform Page(int display, RectTransform parent, Rect body, int rows, int sections, out Rect area) =>
            Page(display, parent, body, rows, sections, 0f, out area);

        /// <summary>
        /// A page: the spine, then the scroll viewport when the copy is taller than the
        /// bay, and the content column every heading and row builds into.
        ///
        /// The page's natural height is measured from its rows and sections. When it is
        /// shorter than the bay, the slack is spread evenly across the row advances and
        /// the gaps between sections, so the copy reaches the bottom of Shell.Body rather
        /// than leaving a dead band under the last row; a page taller than its bay scrolls
        /// at the natural pitch instead of compressing. <paramref name="fixedHeight"/> is
        /// the part of a page whose height does not come from rows — the SERVER tasking
        /// board — and never stretches.
        /// </summary>
        private RectTransform Page(int display, RectTransform parent, Rect body, int rows, int sections,
            float fixedHeight, out Rect area)
        {
            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            int gaps = Mathf.Max(0, sections - 1);
            // The last row's own advance is never spent — the page ends at the last row's
            // bottom — so it is removed from the measured height and only rows and section
            // gaps that actually move the bottom take a share of the slack.
            float natural = fixedHeight + sections * HeadingHeight + gaps * HeadingGap
                          + rows * AvTokens.RowPitch - (AvTokens.RowPitch - AvTokens.RowHeight)
                          + PageTail;
            bool scrolls = natural > body.height;
            int units = rows + gaps - 1;
            // Settings are a form, not a fill-height dashboard: keep adjacent labels
            // close enough to scan and leave calm space below short pages.
            float extra = 4f;
            natural += Mathf.Max(0, units) * extra;
            scrolls = natural > body.height;
            rowPitch = AvTokens.RowPitch + extra;
            sectionGap = HeadingGap + extra;
            sectionsOnPage = 0;
            if (display >= 0 && display < pageScrolls.Length) pageScrolls[display] = scrolls;

            RectTransform content = AvScreen.Scroll(parent, body, Mathf.Max(natural, body.height), out area);
            area = new Rect(area.x + AvScreen.SpineInset, area.y,
                Mathf.Max(0f, area.width - AvScreen.SpineInset), area.height);
            return content;
        }

        /// <summary>
        /// A numbered section heading hanging off the spine, like the theater panels:
        /// index in accent, name in the shared section-title step, status on the right,
        /// hairline under. Number and title share the page's left column, so the numbering
        /// lines up across pages.
        /// </summary>
        private void Heading(RectTransform parent, ref Rect area, string index, string title, string note)
        {
            if (sectionsOnPage > 0) area.y -= sectionGap;
            sectionsOnPage++;

            var rect = new Rect(area.x, area.y, area.width, HeadingHeight);
            AvStyled.SpineTick(parent, rect.x - AvScreen.SpineInset + 2f, rect.y - 13f);

            const float numberWidth = 24f;
            TMP_Text number = AvStyled.Label(parent, new Rect(rect.x, rect.y, numberWidth, 16f), index, "section-title");
            number.color = AvTheme.Accent;
            AvStyled.Label(parent,
                new Rect(rect.x + numberWidth + CellGap, rect.y,
                    Mathf.Max(0f, rect.width * 0.5f - numberWidth - CellGap), 16f), title, "section-title");
            if (!string.IsNullOrEmpty(note))
                AvStyled.Label(parent, new Rect(rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f, 16f),
                    note, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            AvKit.Rule(parent, new Rect(rect.x, rect.y - 20f, rect.width, 1f),
                AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.5f)));

            area.y -= HeadingHeight;
        }

        private Rect TakeRow(ref Rect area)
        {
            var row = new Rect(area.x, area.y, area.width, AvTokens.RowHeight);
            area.y -= rowPitch;
            return row;
        }

        private void BuildMapPage(RectTransform parent, Rect body)
        {
            parent = Page(0, parent, body, 7, 3, out var area);

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
                () => settings.FrontlinesOverlay.Value, () => "Turn on the control field first.");
            Percent(parent, TakeRow(ref area), "FRONTLINE STRENGTH", settings.OverlayOpacity, .1f, 1f, .05f,
                () => settings.FrontlinesOverlay.Value, () => "Turn on the control field first.");
            Stepper(parent, TakeRow(ref area), "UPDATE INTERVAL",
                () => settings.GridRefreshInterval.Value.ToString("0.0") + " s",
                d => settings.GridRefreshInterval.Value = Mathf.Clamp(
                    Mathf.Round((settings.GridRefreshInterval.Value + d * .1f) * 10f) / 10f, .2f, 2f),
                () => settings.GridRefreshInterval.Value > .201f,
                () => settings.GridRefreshInterval.Value < 1.999f,
                "Longer intervals reduce CPU work. Recommended: 0.5 s.",
                () => settings.FrontlinesOverlay.Value, () => "Turn on the control field first.");

            Heading(parent, ref area, "03", "TERRAIN", "SATELLITE");
            Toggle(parent, TakeRow(ref area), "TERRAIN IMAGE",
                "Show the satellite terrain beneath map symbols.",
                () => settings.MapTerrainImage.Value, v => settings.MapTerrainImage.Value = v,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Percent(parent, TakeRow(ref area), "TERRAIN STRENGTH", settings.MapTerrainOpacity, .1f, 1f, .1f,
                () => settings.ExpandedMapUi.Value && settings.MapTerrainImage.Value,
                () => "Enable expanded layout and terrain image first.");
        }

        private void BuildStylePage(RectTransform parent, Rect body)
        {
            parent = Page(1, parent, body, 6, 2, out var area);

            Heading(parent, ref area, "01", "SURFACE", "DECK");
            Percent(parent, TakeRow(ref area), "CONSOLE OPACITY", settings.DeckOpacity, .1f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Stepper(parent, TakeRow(ref area), "BACKGROUND",
                () => SettingsChoices.BackgroundName(settings.DeckGrid.Value, settings.CheckerboardOverlay.Value,
                    settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value),
                d => SetBackground(SettingsChoices.CycleBackground(settings.DeckGrid.Value,
                    settings.CheckerboardOverlay.Value, settings.BackgroundImage.Value, settings.BackgroundImagePreset.Value, d)),
                () => true, () => true,
                "Choose one decoration: plain, grid, checker, hexagon, carbon, radar or custom image. MIXED preserves your old combination.",
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Percent(parent, TakeRow(ref area), "CHECKER STRENGTH", settings.CheckerboardOpacity, .02f, .4f, .02f,
                () => settings.ExpandedMapUi.Value && settings.CheckerboardOverlay.Value,
                () => "Choose CHECKER on STYLE first.");
            Percent(parent, TakeRow(ref area), "MAP DARKENING", settings.MapTrayOpacity, 0f, 1f, .05f,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");

            Heading(parent, ref area, "02", "DISPATCHES", "WIRE");
            Toggle(parent, TakeRow(ref area), "NEWS TICKER", "Show theater dispatches above the map.",
                () => settings.NewsTickerEnabled.Value, v => settings.NewsTickerEnabled.Value = v,
                () => settings.ExpandedMapUi.Value, () => "Turn on expanded layout first.");
            Stepper(parent, TakeRow(ref area), "TICKER SPEED",
                () => settings.NewsTickerSpeed.Value.ToString("0") + " px/s",
                d => settings.NewsTickerSpeed.Value = Mathf.Clamp(settings.NewsTickerSpeed.Value + d * 15f, 15f, 150f),
                () => settings.NewsTickerSpeed.Value > 15f, () => settings.NewsTickerSpeed.Value < 150f,
                "Lower speeds are easier to read. Disable NEWS TICKER to stop motion.",
                () => settings.ExpandedMapUi.Value && settings.NewsTickerEnabled.Value,
                () => "Enable expanded layout and news ticker first.");
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
            parent = Page(2, parent, body, 4, 1, out var area);

            Heading(parent, ref area, "01", "LOCAL IMAGERY", "PNG / JPEG");
            Percent(parent, TakeRow(ref area), "IMAGE STRENGTH", settings.BackgroundImageOpacity, .05f, 1f, .05f,
                ImageEnabled, () => "Choose an image background on STYLE first.");
            Stepper(parent, TakeRow(ref area), "IMAGE FILE", MfdMapDeck.GetCurrentWallpaperFileName,
                MfdMapDeck.CycleCustomWallpaper,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                () => MfdMapDeck.DiscoveredWallpaperCount > 1,
                "Local PNG/JPEG files. Use RESCAN after adding or replacing files.", CustomEnabled,
                () => "Choose CUSTOM on STYLE. Add files to BepInEx/config/BoscaliSummer/wallpapers.");
            string[] fits = { "COVER", "FIT", "STRETCH" };
            Stepper(parent, TakeRow(ref area), "IMAGE FIT",
                () => fits[Mathf.Clamp(settings.WallpaperFitMode.Value, 0, 2)],
                d => settings.WallpaperFitMode.Value = (settings.WallpaperFitMode.Value + d + 3) % 3,
                () => true, () => true, "COVER crops; FIT keeps the full image; STRETCH fills the screen.",
                CustomEnabled, () => "Choose CUSTOM on STYLE first.");
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
        /// through <see cref="IThirdPersonHud"/>, the common HUD element published by the Hud
        /// module through <see cref="IHudBoard"/>, plus Command's own radial preset page.
        /// Every row here writes live state; the camera rows wait for the HUD they belong to.
        /// </summary>
        private void BuildViewPage(RectTransform parent, Rect body)
        {
            ModServices.TryGet(out IThirdPersonHud hud);
            ModServices.TryGet(out IHudBoard board);
            int feeds = board != null ? Mathf.Min(board.Channels.Count, HudLayout.MaxChannels) : 0;
            // The unavailable case still paints one FEEDS row saying so, so the page's
            // measured height matches what it builds either way.
            parent = Page(3, parent, body, 5 + HudSettingRows + (board == null ? 1 : feeds), 4, out var area);

            Heading(parent, ref area, "01", "HUD", "THIRD PERSON");
            Toggle(parent, TakeRow(ref area), "THIRD-PERSON HUD",
                "Show the compact flight overlay in external orbit and chase views.",
                () => hud != null && hud.IsEnabled, v => { if (hud != null && hud.IsEnabled != v) hud.Toggle(); },
                () => hud != null, () => "HUD service unavailable in this scene.");
            Toggle(parent, TakeRow(ref area), "HIDE PITCH LADDER",
                "Hide the floating pitch ladder in third person, keeping reticle, ammo and radar.",
                () => hud != null && hud.HidePitchLadder, v => { if (hud != null) hud.HidePitchLadder = v; },
                () => hud != null && hud.IsEnabled, () => "Turn on third-person HUD first.");

            Heading(parent, ref area, "02", "CAMERA", "CHASE");
            Toggle(parent, TakeRow(ref area), "TARGET CAMERA",
                "Show the native target camera feed in third person while contacts are selected.",
                () => hud != null && hud.CameraFeedEnabled, v => { if (hud != null) hud.CameraFeedEnabled = v; },
                () => hud != null && hud.IsEnabled, () => "Turn on third-person HUD first.");
            Toggle(parent, TakeRow(ref area), "FLIGHT CAMERA",
                "Smooth aircraft-relative orbit and rear chase framing with a steady horizon.",
                () => hud != null && hud.FlightCameraEnabled, v => { if (hud != null) hud.FlightCameraEnabled = v; },
                () => hud != null && hud.IsEnabled, () => "Turn on third-person HUD first.");

            Heading(parent, ref area, "03", "TARGETING", "RADIAL");
            Toggle(parent, TakeRow(ref area), "RADIAL PRESETS",
                "Offer the TGT quick slots as a page in the native cockpit radial menu.",
                () => settings.TargetPresetWheel.Value, v => settings.TargetPresetWheel.Value = v);

            Heading(parent, ref area, "04", "COMMON HUD", "OVERLAY");
            BuildHudRows(parent, ref area, board);
        }

        /// <summary>The seven element-wide rows, before one row per declared feed.</summary>
        private const int HudSettingRows = 7;

        /// <summary>
        /// The common HUD element's own rows. Everything here is client-local presentation and
        /// applies on the board's next tick, so the pilot sees the change while flying. The
        /// feeds below are listed from whatever modules declared one, not from a list kept here.
        /// </summary>
        private void BuildHudRows(RectTransform parent, ref Rect area, IHudBoard board)
        {
            Func<bool> on = () => board != null && board.Enabled;
            Func<string> off = () => "Turn the common HUD element on first.";

            Toggle(parent, TakeRow(ref area), "HUD ELEMENT",
                "Draw the one cockpit HUD element every feature shares for status lines and notices.",
                () => on(), v => { if (board != null) board.Enabled = v; });

            Stepper(parent, TakeRow(ref area), "POSITION",
                () => board != null ? HudLayout.AnchorName((int)board.Anchor) : "--",
                d => { if (board != null) board.Anchor = (HudAnchor)HudLayout.Cycle((int)board.Anchor, HudLayout.AnchorCount, d); },
                () => board != null, () => board != null,
                "Where the element hangs and which way it stacks. Keep it clear of the pitch ladder.",
                on, off);

            Stepper(parent, TakeRow(ref area), "SIZE",
                () => board != null ? HudLayout.ScaleName(board.ScaleStep) : "--",
                d => { if (board != null) board.ScaleStep = HudLayout.Cycle(board.ScaleStep, HudLayout.ScaleCount, d); },
                () => board != null, () => board != null,
                "Text size as a multiple of the game's own overlay text size option.",
                on, off);

            Stepper(parent, TakeRow(ref area), "OPACITY",
                () => board != null ? HudLayout.OpacityName(board.OpacityStep) : "--",
                d => { if (board != null) board.OpacityStep = HudLayout.Cycle(board.OpacityStep, HudLayout.OpacityCount, d); },
                () => board != null, () => board != null,
                "How solid the element reads over a bright sky. OFF hides it without unloading it.",
                on, off);

            Stepper(parent, TakeRow(ref area), "MAX LINES",
                () => board != null ? board.MaxRows.ToString() : "--",
                d => { if (board != null) board.MaxRows = Mathf.Clamp(board.MaxRows + d, HudLayout.MinRows, HudLayout.MaxRows); },
                () => board != null && board.MaxRows > HudLayout.MinRows,
                () => board != null && board.MaxRows < HudLayout.MaxRows,
                "How many lines the element may show at once. Two are kept for live notices.",
                on, off);

            Toggle(parent, TakeRow(ref area), "NOTICES",
                "Show transient notices: an ace hunt starting, entering or leaving a contract area.",
                () => board != null && board.NoticesEnabled,
                v => { if (board != null) board.NoticesEnabled = v; },
                on, off);

            Stepper(parent, TakeRow(ref area), "NOTICE TIME",
                () => board != null ? board.NoticeSeconds.ToString("0") + " s" : "--",
                d => { if (board != null) board.NoticeSeconds = Mathf.Clamp(board.NoticeSeconds + d, HudLayout.MinNoticeSeconds, HudLayout.MaxNoticeSeconds); },
                () => board != null && board.NoticeSeconds > HudLayout.MinNoticeSeconds,
                () => board != null && board.NoticeSeconds < HudLayout.MaxNoticeSeconds,
                "How long a transient notice stays up.",
                () => on() && board.NoticesEnabled, () => "Turn notices on first.");

            if (board == null)
            {
                Toggle(parent, TakeRow(ref area), "FEEDS",
                    "The common HUD element is not installed in this session.",
                    () => false, v => { }, () => false, () => "HUD element unavailable.");
                return;
            }

            int feeds = Mathf.Min(board.Channels.Count, HudLayout.MaxChannels);
            for (int i = 0; i < feeds; i++)
            {
                IHudChannel feed = board.Channels[i];
                Toggle(parent, TakeRow(ref area), feed.Label,
                    "Show this feed on the common HUD element. Switching it off hides its lines " +
                    "and changes nothing about how the feature itself runs.",
                    () => feed.Enabled, v => { if (feed.Enabled != v) feed.Toggle(); },
                    on, off);
            }
        }

        private void Percent(RectTransform parent, Rect area, string title, ConfigEntry<float> entry,
            float min, float max, float step, Func<bool> enabled, Func<string> reason)
        {
            Stepper(parent, area, title, () => entry.Value.ToString("P0"),
                d => entry.Value = Mathf.Clamp(Mathf.Round((entry.Value + d * step) * 100f) / 100f, min, max),
                () => entry.Value > min + .001f, () => entry.Value < max - .001f,
                "Adjust " + title.ToLowerInvariant() + ".", enabled, reason);
        }

        /// <summary>
        /// The five shared row columns: state pip, name, minus, value, plus. A toggle
        /// draws only the pip and the value plate, a stepper the minus/value/plus, so the
        /// label column and the value column never move between the two kinds of row.
        /// </summary>
        private static AvNode SettingRow(string name) =>
            AvBox.Row(name).Height(AvTokens.RowHeight).Pad(0f, RowInset, RowRightPad, RowInset).Gaps(CellGap)
                .Add(AvBox.Cell("pip").Width(PipSize))
                .Add(AvBox.Cell("label").Grow())
                .Add(AvBox.Cell("minus").Width(StepWidth))
                .Add(AvBox.Cell("value").Width(StateWidth))
                .Add(AvBox.Cell("plus").Width(StepWidth));

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
        // and subtle wash, so ON reads at a glance without overpowering the panel.
        private static Color LatchedRow => AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), 0.12f, 0.16f));
        private static Color LatchedRowHover => AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), 0.18f, 0.24f));
        private static Color HoverRow => AvTheme.Unity(AvTokens.SurfaceRaised.WithAlpha(0.5f));

        // The ON plate is a wash of the accent, not a solid block. The wash keeps the dark
        // ink label at roughly 5:1 on the panel ground, and the state is never colour alone:
        // the pip is lit and the plate says ON.
        private static Color LatchedState => AvTheme.Unity(AvTokens.Wash(AvTheme.Accent.ToRgba(), 0.16f, 0.75f));

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
            Func<bool> get, Action<bool> set, Func<bool> enabled = null, Func<string> reason = null)
        {
            var row = SettingRow("row").Arrange(area);
            var hover = RowHover(parent, row.Rect, tooltip);
            Rect pip = row.At("pip");
            Image led = AvKit.Panel(parent,
                new Rect(pip.x, pip.y - (pip.height - PipSize) * 0.5f, PipSize, PipSize),
                AvTheme.RailInert);
            TMP_Text label = AvStyled.Label(parent, row.At("label"), title, "row-name",
                align: TextAlignmentOptions.MidlineLeft);
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
                string why = available ? tooltip : reason != null ? reason() : tooltip;
                button.SetEnabled(available);
                button.SetText(on ? "ON" : "OFF");
                button.SetLatched(on);
                if (on)
                {
                    button.SetCustomColors(LatchedState, AvTheme.Frame, AvTheme.Accent);
                }
                else
                {
                    button.ClearCustomColors();
                }
                button.WithTooltip(why);
                hover.SetText(why);
                led.color = !available ? AvTheme.Disabled : on ? AvTheme.RailReady : AvTheme.RailInert;
                label.color = available ? AvTheme.TextPrimary : AvTheme.Disabled;
                hover.SetColors(on && available ? LatchedRow : Color.clear,
                                on && available ? LatchedRowHover : HoverRow);
            });
        }

        private void Stepper(RectTransform parent, Rect area, string title, Func<string> get,
            Action<int> change, Func<bool> decrease, Func<bool> increase, string tooltip,
            Func<bool> enabled = null, Func<string> reason = null, bool readOnlyValue = false)
        {
            var row = SettingRow("row").Arrange(area);
            var hover = RowHover(parent, row.Rect, tooltip);
            TMP_Text label = AvStyled.Label(parent, row.At("label"), title, "row-name",
                align: TextAlignmentOptions.MidlineLeft);
            Action<int> click = d =>
            {
                if (enabled != null && !enabled() || !(d < 0 ? decrease() : increase())) return;
                change(d);
                Echo(title + " — " + get());
                Changed();
            };
            var minus = AvStyled.Button(parent, row.At("minus"), "-", "btn", () => click(-1));
            var value = AvStyled.Label(parent, row.At("value"), "", "row-value", align: TextAlignmentOptions.Center);
            // A value is never traded for an ellipsis: it shrinks to the micro floor and
            // then overflows its cell, the module's FitSingleLine rule.
            value.enableWordWrapping = false;
            value.overflowMode = TextOverflowModes.Overflow;
            value.enableAutoSizing = true;
            value.fontSizeMin = AvTokens.FontMicro;
            value.fontSizeMax = value.fontSize;
            value.richText = false;
            var plus = AvStyled.Button(parent, row.At("plus"), "+", "btn", () => click(1));
            minus.GetComponentInChildren<TMP_Text>().fontSize = AvTokens.FontLead;
            plus.GetComponentInChildren<TMP_Text>().fontSize = AvTokens.FontLead;
            refreshers.Add(() =>
            {
                bool available = enabled == null || enabled();
                minus.SetEnabled(available && decrease());
                plus.SetEnabled(available && increase());
                string text = available || readOnlyValue ? get() : "--";
                if (value.text != text) value.text = text;
                string why = available ? tooltip : reason != null ? reason() : tooltip;
                minus.WithTooltip(available ? tooltip + " Previous / decrease. " + text : why);
                plus.WithTooltip(available ? tooltip + " Next / increase. " + text : why);
                hover.SetText(why);
                label.color = available ? AvTheme.TextPrimary : AvTheme.Disabled;
                // A remote client sees the host's value read-only; the figure stays legible,
                // the controls around it say why they are inert.
                value.color = available || readOnlyValue ? AvTheme.TextPrimary : AvTheme.Disabled;
            });
        }

        private void RefreshPanel()
        {
            bool host = HostAuthority();
            shell?.DataBar.SetChip(1, host ? "HOST" : "CLIENT", host ? "live" : "inert");
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
            clientPages = null;
            clientTabs = null;
            clientPage = 0;
            tasking = null;
            taskRequest = null;
            taskNote = null;
            nextTaskingRefresh = 0f;
            Array.Clear(taskRows, 0, taskRows.Length);
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
            clientPages = null;
            clientTabs = null;
            Array.Clear(taskRows, 0, taskRows.Length);
            taskRequest = null;
            taskNote = null;
            refreshers.Clear();
        }

        private void OnDestroy()
        {
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            ResetForScene();
        }
    }
}
