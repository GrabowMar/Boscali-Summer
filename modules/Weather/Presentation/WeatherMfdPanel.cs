using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// Read-only battlefield environment briefing on the maximised map.
    /// </summary>
    internal sealed class WeatherMfdPanel : MonoBehaviour, ISceneService
    {
        private const int TabForecast = 0;
        private const int TabEnvironment = 1;
        private const float Width = AvTokens.PanelWidth;
        private const int ChipCount = 2;

        private WeatherSettings settings;
        private WeatherManager weather;
        private ManualLogSource logger;

        private GameObject screenRoot;
        private MFDScreen screen;
        private AvScreen shell;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        // Forecast Page widgets: Hero Card
        private Image regimeCardRail;
        private WeatherGlyph liveGlyph;
        private TMP_Text liveRegimeBadge;
        private Image liveRegimeBadgeBorder;
        private TMP_Text liveRegimeTitle;
        private TMP_Text liveCoverLabel;
        private Image liveCoverBar;
        private TMP_Text liveQuickMetrics;
        private TMP_Text liveTacticalBrief;

        // Forecast Page widgets: Airspace Profile / Stratification
        private TMP_Text profileStatusBadge;
        private Image profileTrack;
        private Image profileCloudLayer;
        private Image profileOwnshipMarker;
        private TMP_Text profileOwnshipLabel;
        private TMP_Text profileCloudRangeLabel;

        // Forecast Page widgets: Timeline Table
        private readonly List<TimelineRowWidgets> timelineRows = new List<TimelineRowWidgets>(6);

        // Forecast Page widgets: Local Flight Level Telemetry
        private TMP_Text localCol1;
        private TMP_Text localCol2;

        // Sky & Air watch: three tactical assessments derived from the same readings below.
        private Image advisoryRail;
        private TMP_Text advisoryLight;
        private TMP_Text advisoryDeck;
        private TMP_Text advisoryWind;

        // Environment Page widgets: Solar Ephemeris
        private Image solarCardRail;
        private TMP_Text solarStateBadge;
        private Image solarElevationTrack;
        private Image solarElevationMarker;
        private TMP_Text solarElevationText;
        private TMP_Text solarAzimuthText;
        private TMP_Text solarEventsText;

        // Environment Page widgets: Lunar Ephemeris
        private Image lunarCardRail;
        private TMP_Text lunarPhaseText;
        private Image lunarIllumFill;
        private TMP_Text lunarIllumText;
        private TMP_Text lunarTacticalGuidance;

        // Environment Page widgets: Wind Dynamics
        private Image windCardRail;
        private Image windNeedle;
        private TMP_Text windSummaryText;
        private TMP_Text windShearText;
        private TMP_Text windFlightAssistText;

        // Environment Page widgets: Atmosphere & Performance
        private Image atmoCardRail;
        private Image atmoDensityFill;
        private TMP_Text atmoDensityText;
        private TMP_Text atmoSpeedOfSoundText;
        private TMP_Text atmoAltitudeText;

        private sealed class TimelineRowWidgets
        {
            public Image RiskRail;
            public TMP_Text OffsetLabel;
            public TMP_Text RegimeBadgeText;
            public WeatherGlyph Glyph;
            public Image RegimeBadgeBorder;
            public TMP_Text ConditionsLabel;
            public TMP_Text DeckLabel;
            public Image RainFill;
            public TMP_Text RainText;
        }

        public void Configure(WeatherSettings config, WeatherManager manager, ManualLogSource log)
        {
            settings = config;
            weather = manager;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdScreenHost.Release(MfdSlots.Weather);
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            shell = null;
            timelineRows.Clear();
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || weather == null || settings == null) return;
            if (!settings.Enabled.Value)
            {
                if (screen != null) ResetForScene();
                return;
            }
            if (Application.isBatchMode || !GameAccess.MfdAvailable)
            {
                failed = true;
                return;
            }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            if (!visible) return;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.25f; // 4 Hz refresh
                Refresh();
            }
        }

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? UnityEngine.Object.FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                if (!MfdScreenHost.TryHost(MfdSlots.Weather, preferLeft: false, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger?.LogWarning("ENV MFD unavailable: could not add a host button.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdScreenHost.Release(MfdSlots.Weather);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdScreenHost.Release(MfdSlots.Weather);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    ResetForScene();
                    failed = true;
                    logger?.LogWarning("ENV MFD unavailable: bezel changed during installation.");
                    return;
                }

                logger?.LogInfo("ENV MFD installed on " + (left ? "left" : "right") +
                                " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                ResetForScene();
                failed = true;
                logger?.LogError("ENV MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            TMP_Text sourceText = template.GetComponentInChildren<TMP_Text>(true);
            TMP_FontAsset font = sourceText != null ? sourceText.font : null;
            if (font != null) AvFont.Font = font;

            var root = new GameObject("BoscaliWeather.Screen", typeof(RectTransform), typeof(Image));
            screenRoot = root;
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvScreen.ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvKit.ClampIntoCanvas(rootRect);

            Image background = root.GetComponent<Image>();
            background.sprite = AvSprites.Panel;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            var content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            AvKit.Stretch(content);

            string[] tabs = { "WEATHER", "SKY & AIR" };

            shell = AvScreen.Build(
                content, MfdSlots.Weather,
                tabs,
                new[]
                {
                    new[] { "COVER", "CLOUD COVER" },
                    new[] { "BASE", "CLOUD BASE" },
                    new[] { "WIND", "WIND FROM" },
                    new[] { "DENSITY", "CAMERA ALT" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            for (int i = 0; i < shell.Metrics.Length; i++)
            {
                FitMetric(shell.Metrics[i]);
            }

            BuildForecastPage(shell.CreatePage(TabForecast, "ForecastPage"));
            BuildEnvironmentPage(shell.CreatePage(TabEnvironment, "EnvironmentPage"));

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Weather;
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            shell.SetPage(TabForecast);
            return result;
        }

        private static AvStyled.Metric FitMetric(AvStyled.Metric metric)
        {
            if (metric?.Value == null) return metric;
            metric.Value.enableAutoSizing = true;
            metric.Value.fontSizeMax = metric.Value.fontSize;
            metric.Value.fontSizeMin = AvTokens.FontMicro;
            metric.Value.alignment = TextAlignmentOptions.MidlineRight;
            return metric;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].gameObject != button.gameObject) return images[i];
            }
            return button.GetComponent<Image>();
        }

        // ---- Page Builders ---------------------------------------------------------------

        private void BuildForecastPage(GameObject page)
        {
            var pageRect = (RectTransform)page.transform;
            Rect body = shell.Body;
            float totalHeight = Mathf.Max(600f, body.height);
            RectTransform container = AvScreen.Scroll(pageRect, body, totalHeight, out Rect area);

            float x = area.x + 4f;
            float y = area.y;
            float width = area.width - 8f;

            // Current weather is the instrument's visual anchor; the outlook remains below.
            float cardH = 172f;
            Rect cardRect = new Rect(x, y, width, cardH);
            AvKit.Panel(container, cardRect, AvTheme.Surface, AvSprites.Card);
            AvKit.Outline(container, cardRect, AvTheme.Hairline);
            regimeCardRail = AvKit.Rule(container, new Rect(x, y, 4f, cardH), AvTheme.Accent);

            AvKit.Label(container, "CURRENT CONDITIONS  /  LIVE WEATHER",
                new Rect(x + 12f, y - 8f, width - 24f, 14f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            Rect symbol = new Rect(x + 12f, y - 30f, 58f, 58f);
            AvKit.Panel(container, symbol, AvTheme.SurfaceInert);
            AvKit.Outline(container, symbol, AvTheme.Frame);
            liveGlyph = WeatherGlyph.Create(container, new Rect(x + 17f, y - 35f, 48f, 48f));

            AvKit.Panel(container, new Rect(x + 80f, y - 65f, 52f, 18f), AvTheme.SurfaceInert);
            liveRegimeBadgeBorder = AvKit.Outline(container, new Rect(x + 80f, y - 65f, 52f, 18f), AvTheme.Frame)[0];
            liveRegimeBadge = AvKit.Label(
                container, "CLR", new Rect(x + 80f, y - 65f, 52f, 18f),
                AvTheme.Accent, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

            liveRegimeTitle = AvKit.Label(
                container, "CLEAR SKY", new Rect(x + 80f, y - 33f, width - 204f, 27f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold, TextAlignmentOptions.Left);

            liveCoverLabel = AvKit.Label(
                container, "0% COVER", new Rect(x + width - 116f, y - 37f, 104f, 18f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            AvKit.Panel(container, new Rect(x + width - 116f, y - 65f, 104f, 4f), AvTheme.SurfaceInert);
            liveCoverBar = AvKit.Panel(container, new Rect(x + width - 116f, y - 65f, 0f, 4f), AvTheme.Accent);

            AvKit.Rule(container, new Rect(x + 12f, y - 98f, width - 24f, 1f), AvTheme.Hairline);

            liveQuickMetrics = AvKit.Label(
                container, "DECK ---- M   /   WIND -- KT   /   RAIN --%",
                new Rect(x + 12f, y - 105f, width - 24f, 17f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            AvKit.Label(container, "FLIGHT ADVISORY  /  MODEL ASSESSMENT",
                new Rect(x + 12f, y - 128f, width - 24f, 13f),
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            liveTacticalBrief = AvKit.Label(
                container, "Current battlefield conditions",
                new Rect(x + 12f, y - 142f, width - 24f, 27f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left, wrap: true);

            y -= cardH + 8f;

            // 2. Airspace Profile / Stratification Card
            float profH = 64f;
            float profileInset = (profH - 64f) * 0.5f;
            Rect profRect = new Rect(x, y, width, profH);
            AvKit.Panel(container, profRect, AvTheme.Surface, AvSprites.Card);
            AvKit.Outline(container, profRect, AvTheme.Hairline);

            AvKit.Label(
                container, "VERTICAL PROFILE", new Rect(x + 10f, y - 6f - profileInset, 160f, 14f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);

            profileStatusBadge = AvKit.Label(
                container, "BELOW CLOUD BASE",
                new Rect(x + 170f, y - 6f - profileInset, width - 180f, 14f),
                AvTheme.Accent, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            // Horizontal Stratification Bar (0m to 5000m)
            float trackW = width - 20f;
            profileTrack = AvKit.Panel(container, new Rect(x + 10f, y - 23f - profileInset, trackW, 16f), AvTheme.SurfaceInert);
            AvKit.Outline(container, new Rect(x + 10f, y - 23f - profileInset, trackW, 16f), AvTheme.Hairline);
            for (int tick = 1; tick < 5; tick++)
                AvKit.Rule(container, new Rect(x + 10f + trackW * tick / 5f,
                    y - 23f - profileInset, 1f, 16f), AvTheme.Hairline.WithAlpha(.55f));

            profileCloudLayer = AvKit.Panel(
                container, new Rect(x + 10f, y - 23f - profileInset, 100f, 16f), AvTheme.RailInfo.WithAlpha(0.45f));

            profileCloudRangeLabel = AvKit.Label(
                container, "CLOUD BASE  ---- M",
                new Rect(x + 12f, y - 23f - profileInset, trackW - 4f, 16f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);

            profileOwnshipMarker = AvKit.Rule(
                container, new Rect(x + 10f, y - 21f - profileInset, 3f, 20f), AvTheme.Accent);

            profileOwnshipLabel = AvKit.Label(
                container, "CAMERA ALTITUDE  ---- M",
                new Rect(x + 10f, y - 44f - profileInset, width - 20f, 14f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            y -= profH + 8f;

            // 3. Timeline Section Header & Table Header
            AvKit.Label(
                container, "NEXT 60 MINUTES",
                new Rect(x + 6f, y - 2f, 220f, 14f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            AvKit.Label(
                container, "MODEL OUTLOOK  /  MISSION TIME",
                new Rect(x + 230f, y - 2f, width - 236f, 14f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

            AvKit.Rule(container, new Rect(x, y - 18f, width, 1f), AvTheme.Hairline);
            y -= 20f;

            // Table Column Headers
            AvKit.Panel(container, new Rect(x, y, width, 18f), AvTheme.SurfaceInert);
            AvKit.Label(container, "T+MIN", new Rect(x + 8f, y, 45f, 18f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            AvKit.Label(container, "WX", new Rect(x + 58f, y, 62f, 18f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            AvKit.Label(container, "COVER", new Rect(x + 126f, y, 42f, 18f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
            AvKit.Label(container, "BASE", new Rect(x + 174f, y, 62f, 18f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Right);
            AvKit.Label(container, "PRECIPITATION / RISK", new Rect(x + 250f, y, width - 258f, 18f), AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            y -= 20f;

            // 6 Timeline Rows
            int[] offsets = WeatherForecast.DefaultOffsetsMinutes;
            timelineRows.Clear();

            float rowH = 38f;
            for (int i = 0; i < offsets.Length; i++)
            {
                float rowInset = (rowH - 38f) * 0.5f;
                Color rowBg = (i % 2 == 0) ? AvTheme.Surface : AvTheme.SurfaceRaised;
                AvKit.Panel(container, new Rect(x, y, width, rowH), rowBg);

                var row = new TimelineRowWidgets
                {
                    RiskRail = AvKit.Rule(container, new Rect(x, y, 2f, rowH), AvTheme.RailInert)
                };

                string timeText = (i == 0) ? "NOW" : $"+{offsets[i]} MIN";
                row.OffsetLabel = AvKit.Label(
                    container, timeText, new Rect(x + 8f, y, 50f, rowH),
                    (i == 0) ? AvTheme.TextPrimary : AvTheme.Dim, AvTokens.FontMicro, (i == 0) ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

                row.Glyph = WeatherGlyph.Create(container,
                    new Rect(x + 56f, y - 6f - rowInset, 26f, 26f));
                AvKit.Panel(container, new Rect(x + 88f, y - 10f - rowInset, 34f, 18f), AvTheme.SurfaceInert);
                row.RegimeBadgeBorder = AvKit.Outline(container, new Rect(x + 88f, y - 10f - rowInset, 34f, 18f), AvTheme.Frame)[0];
                row.RegimeBadgeText = AvKit.Label(
                    container, "---", new Rect(x + 88f, y - 10f - rowInset, 34f, 18f),
                    AvTheme.Accent, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);

                row.ConditionsLabel = AvKit.Label(
                    container, "--%", new Rect(x + 126f, y, 42f, rowH),
                    AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

                row.DeckLabel = AvKit.Label(
                    container, "---- M", new Rect(x + 174f, y, 62f, rowH),
                    AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineRight);

                float barW = 92f;
                AvKit.Panel(container, new Rect(x + 250f, y - 15f - rowInset, barW, 8f), AvTheme.SurfaceInert);
                row.RainFill = AvKit.Panel(container, new Rect(x + 250f, y - 15f - rowInset, 0f, 8f), AvTheme.Accent);

                row.RainText = AvKit.Label(
                    container, "— DRY —", new Rect(x + 350f, y, width - 356f, rowH),
                    AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

                timelineRows.Add(row);
                y -= rowH + 2f;
            }

            y -= 6f;

            // 4. Local Flight Level Telemetry Card
            float localH = 58f;
            Rect localRect = new Rect(x, y, width, localH);
            AvKit.Panel(container, localRect, AvTheme.Surface, AvSprites.Card);
            AvKit.Outline(container, localRect, AvTheme.Hairline);

            AvKit.Label(
                container, "LOCAL AIR AT CAMERA ALTITUDE",
                new Rect(x + 10f, y - 6f, width - 20f, 14f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            localCol1 = AvKit.Label(
                container, "ALT — M   DENSITY —% SL   SOUND — M/S",
                new Rect(x + 10f, y - 22f, width - 20f, 15f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            localCol2 = AvKit.Label(
                container, "WIND — KTS   TURBULENCE —",
                new Rect(x + 10f, y - 38f, width - 20f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
        }

        private void BuildEnvironmentPage(GameObject page)
        {
            var pageRect = (RectTransform)page.transform;
            Rect body = shell.Body;
            float extra = Mathf.Max(0f, body.height - 455f);
            float gap = 10f + Mathf.Min(8f, extra / 20f);
            float cardGrow = Mathf.Min(32f, extra / 6f);
            float inset = cardGrow * 0.5f;
            float totalHeight = Mathf.Max(body.height, 392f + 4f * cardGrow + 4f * gap);
            RectTransform container = AvScreen.Scroll(pageRect, body, totalHeight, out Rect area);

            float x = area.x + 4f;
            float y = area.y;
            float width = area.width - 8f;

            Rect watch = new Rect(x, y, width, 72f);
            AvKit.Panel(container, watch, AvTheme.SurfaceRaised, AvSprites.Card);
            AvKit.Outline(container, watch, AvTheme.Hairline);
            advisoryRail = AvKit.Rule(container, new Rect(x, y, 4f, watch.height), AvTheme.RailInfo);
            AvKit.Label(container, "FLIGHT CONDITIONS  /  ENVIRONMENTAL WATCH",
                new Rect(x + 12f, y - 8f, width - 24f, 14f),
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            float cellW = (width - 24f) / 3f;
            string[] watchLabels = { "LIGHT", "CLOUD DECK", "WIND" };
            TMP_Text[] watchValues = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float cellX = x + 12f + i * cellW;
                if (i > 0) AvKit.Rule(container, new Rect(cellX, y - 29f, 1f, 31f), AvTheme.Hairline);
                AvKit.Label(container, watchLabels[i],
                    new Rect(cellX + 8f, y - 30f, cellW - 16f, 13f),
                    AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
                watchValues[i] = AvKit.Label(container, "—",
                    new Rect(cellX + 8f, y - 45f, cellW - 16f, 17f),
                    AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
            }
            advisoryLight = watchValues[0];
            advisoryDeck = watchValues[1];
            advisoryWind = watchValues[2];
            y -= watch.height + gap;

            // 1. Solar Ephemeris Card
            float solarH = 92f + cardGrow;
            Rect solarRect = new Rect(x, y, width, solarH);
            AvKit.Panel(container, solarRect, AvTheme.Surface, AvSprites.Card);
            AvKit.Outline(container, solarRect, AvTheme.Hairline);
            solarCardRail = AvKit.Rule(container, new Rect(x, y, 4f, solarH), AvTheme.Accent);

            AvKit.Label(
                container, "DAYLIGHT",
                new Rect(x + 10f, y - 6f - inset, 260f, 15f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);

            solarStateBadge = AvKit.Label(
                container, "[ DAYLIGHT ]",
                new Rect(x + width - 140f, y - 6f - inset, 130f, 15f),
                AvTheme.Accent, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

            // Solar Elevation Track (-90° to +90°)
            float trackW = width - 20f;
            solarElevationTrack = AvKit.Panel(container, new Rect(x + 10f, y - 25f - inset, trackW, 14f), AvTheme.SurfaceInert);
            AvKit.Outline(container, new Rect(x + 10f, y - 25f - inset, trackW, 14f), AvTheme.Hairline);
            // Horizon Tick at 0° (center)
            AvKit.Rule(container, new Rect(x + 10f + trackW * 0.5f, y - 24f - inset, 1f, 12f), AvTheme.Dim);
            AvKit.Rule(container, new Rect(x + 10f + trackW * 0.25f, y - 27f - inset, 1f, 4f), AvTheme.Hairline);
            AvKit.Rule(container, new Rect(x + 10f + trackW * 0.75f, y - 27f - inset, 1f, 4f), AvTheme.Hairline);

            solarElevationMarker = AvKit.Rule(
                container, new Rect(x + 10f + trackW * 0.5f, y - 23f - inset, 4f, 14f), AvTheme.Warning);

            solarElevationText = AvKit.Label(
                container, "ELEVATION: +00.0° (HORIZON)",
                new Rect(x + 10f, y - 44f - inset, width - 20f, 15f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);

            solarAzimuthText = AvKit.Label(
                container, "AZIMUTH: ---°  |  TIME OF DAY: --:--",
                new Rect(x + 10f, y - 60f - inset, width - 20f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            solarEventsText = AvKit.Label(
                container, "SUNRISE / SUNSET: CALCULATING...",
                new Rect(x + 10f, y - 76f - inset, width - 20f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            y -= solarH + gap;

            // 2. Lunar Ephemeris Card
            float lunarH = 80f + cardGrow;
            Rect lunarRect = new Rect(x, y, width, lunarH);
            AvKit.Panel(container, lunarRect, AvTheme.Surface, AvSprites.Card);
            AvKit.Outline(container, lunarRect, AvTheme.Hairline);
            lunarCardRail = AvKit.Rule(container, new Rect(x, y, 4f, lunarH), AvTheme.Accent);

            AvKit.Label(
                container, "MOONLIGHT",
                new Rect(x + 10f, y - 6f - inset, width - 20f, 15f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);

            lunarPhaseText = AvKit.Label(
                container, "PHASE: --- (0% ILLUMINATED)",
                new Rect(x + 10f, y - 24f - inset, 220f, 15f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            // Illumination bar
            float barW = 120f;
            Image lunarIllumTrack = AvKit.Panel(container, new Rect(x + width - 130f, y - 26f - inset, barW, 10f), AvTheme.SurfaceInert);
            lunarIllumFill = AvKit.Panel(container, new Rect(x + width - 130f, y - 26f - inset, 0f, 10f), AvTheme.RailInfo);
            for (int tick = 1; tick < 4; tick++)
                AvKit.Rule(container, new Rect(x + width - 130f + barW * tick / 4f,
                    y - 26f - inset, 1f, 10f), AvTheme.Hairline.WithAlpha(.65f));

            lunarIllumText = AvKit.Label(
                container, "0% LUNAR GLOW",
                new Rect(x + 10f, y - 42f - inset, width - 20f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            lunarTacticalGuidance = AvKit.Label(
                container, "Night visibility depends on moonlight and cloud cover",
                new Rect(x + 10f, y - 58f - inset, width - 20f, 16f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            y -= lunarH + gap;

            // 3. Wind Dynamics Card
            float windH = 74f + cardGrow;
            Rect windRect = new Rect(x, y, width, windH);
            AvKit.Panel(container, windRect, AvTheme.Surface, AvSprites.Card);
            AvKit.Outline(container, windRect, AvTheme.Hairline);
            windCardRail = AvKit.Rule(container, new Rect(x, y, 4f, windH), AvTheme.Accent);

            AvKit.Label(
                container, "WIND & TURBULENCE",
                new Rect(x + 10f, y - 6f - inset, width - 20f, 15f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);

            windSummaryText = AvKit.Label(
                container, "WIND -- KTS   FROM ---°   TO ---°",
                new Rect(x + 10f, y - 24f - inset, width - 96f, 15f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            windShearText = AvKit.Label(
                container, "MISSION WIND  |  TURBULENCE --",
                new Rect(x + 10f, y - 40f - inset, width - 96f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            windFlightAssistText = AvKit.Label(
                container, "Wind advisory pending",
                new Rect(x + 10f, y - 55f - inset, width - 96f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            // Direction is a vector, so the card gives it a compass instead of a second
            // numeral. The needle alone moves; the rose is built once with the page.
            float dialX = x + width - 70f;
            float dialY = y - 10f - inset;
            AvKit.Panel(container, new Rect(dialX, dialY, 54f, 54f), AvTheme.SurfaceInert);
            AvKit.Outline(container, new Rect(dialX, dialY, 54f, 54f), AvTheme.Frame.WithAlpha(.65f));
            AvKit.Rule(container, new Rect(dialX + 27f, dialY - 9f, 1f, 36f), AvTheme.Hairline.WithAlpha(.55f));
            AvKit.Rule(container, new Rect(dialX + 9f, dialY - 27f, 36f, 1f), AvTheme.Hairline.WithAlpha(.55f));
            AvKit.Label(container, "N", new Rect(dialX + 22f, dialY - 2f, 10f, 11f),
                AvTheme.RailInfo, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Center);
            AvKit.Label(container, "S", new Rect(dialX + 22f, dialY - 41f, 10f, 11f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Center);
            windNeedle = AvKit.Rule(container, new Rect(0f, 0f, 2f, 19f), AvTheme.RailInfo);
            windNeedle.rectTransform.pivot = new Vector2(.5f, 0f);
            windNeedle.rectTransform.anchoredPosition = new Vector2(dialX + 27f, dialY - 27f);
            windNeedle.enabled = false;
            AvKit.Panel(container, new Rect(dialX + 24f, dialY - 24f, 6f, 6f), AvTheme.RailInfo, AvSprites.Led);

            y -= windH + gap;

            // 4. Atmosphere & Engine Performance Card
            float atmoH = 74f + cardGrow;
            Rect atmoRect = new Rect(x, y, width, atmoH);
            AvKit.Panel(container, atmoRect, AvTheme.Surface, AvSprites.Card);
            AvKit.Outline(container, atmoRect, AvTheme.Hairline);
            atmoCardRail = AvKit.Rule(container, new Rect(x, y, 4f, atmoH), AvTheme.Accent);

            AvKit.Label(
                container, "AIR AT CAMERA ALTITUDE",
                new Rect(x + 10f, y - 6f - inset, width - 20f, 15f),
                AvTheme.TextPrimary, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);

            atmoDensityText = AvKit.Label(
                container, "AIR DENSITY  --- % OF SEA LEVEL",
                new Rect(x + 10f, y - 24f - inset, width - 125f, 15f),
                AvTheme.TextPrimary, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            atmoSpeedOfSoundText = AvKit.Label(
                container, "SPEED OF SOUND  --- M/S",
                new Rect(x + 10f, y - 40f - inset, width - 125f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);

            atmoAltitudeText = AvKit.Label(
                container, "SAMPLED ALTITUDE  ---- M",
                new Rect(x + 10f, y - 55f - inset, width - 125f, 15f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Normal, TextAlignmentOptions.Left);
            float densityX = x + width - 108f;
            float densityY = y - 37f - inset;
            AvKit.Label(container, "DENSITY / SL", new Rect(densityX, densityY + 17f, 98f, 12f),
                AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold, TextAlignmentOptions.Left);
            AvKit.Panel(container, new Rect(densityX, densityY, 98f, 10f), AvTheme.SurfaceInert);
            atmoDensityFill = AvKit.Panel(container, new Rect(densityX, densityY, 0f, 10f), AvTheme.RailInfo);
            AvKit.Rule(container, new Rect(densityX + 59f, densityY, 1f, 10f), AvTheme.Warning);
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (shell == null || weather == null) return;
            LevelInfo level = LevelInfo.i;
            if (level == null)
            {
                shell.DataBar.State.text = "METOC / NO MISSION";
                shell.DataBar.SetChip(0, "WX OFFLINE", "info");
                shell.DataBar.SetChip(1, "NO MISSION", "info");
                if (shell.Status != null) shell.Status.text = "BATTLEFIELD ENVIRONMENT UNAVAILABLE";
                return;
            }

            // Top metrics row
            float cond = weather.CurrentConditions;
            WeatherRegime regime = weather.CurrentRegime;
            Color condColor = GetRegimeColor(regime.Type);
            shell.Metrics[0].Set($"{Mathf.RoundToInt(cond * 100f)}% {regime.Code}", "CLOUD COVER", cond, condColor);

            float deck = weather.CurrentCloudHeight;
            Color deckColor = deck < 1600f ? AvTheme.Warning : AvTheme.Accent;
            shell.Metrics[1].Set($"{Mathf.RoundToInt(deck)} M", "CLOUD BASE", Mathf.Clamp01(deck / 4000f), deckColor);

            WeatherForecast.FormatWind(weather.CurrentWindVelocity.x, weather.CurrentWindVelocity.z, out float kts, out int towards, out int from);
            Color windColor = kts > 25f ? AvTheme.Warning : AvTheme.Accent;
            shell.Metrics[2].Set($"{Mathf.RoundToInt(kts)}K {from:D3}°", "WIND FROM", Mathf.Clamp01(kts / 40f), windColor);

            Camera camera = Camera.main;
            Vector3 samplePos = camera != null ? camera.transform.position : Vector3.zero;
            float airDensity = camera != null ? LevelInfo.GetAirDensity(samplePos.y) : 0f;
            shell.Metrics[3].Set(camera != null ? $"{Mathf.RoundToInt(airDensity * 100f)}%" : "—", "AIR DENSITY", Mathf.Clamp01(airDensity), AvTheme.Accent);

            // Chips
            shell.DataBar.State.text = "METOC / BATTLEFIELD";
            shell.DataBar.SetChip(0, "WX " + regime.Code,
                regime.Type == WeatherRegimeType.Storm ? "warn" : "live");
            shell.DataBar.SetChip(1, settings.DynamicWeatherEnabled.Value ? "MODEL ACTIVE" : "STATIC WEATHER", "info");

            // Active Tab Content
            if (shell.Page == TabForecast)
            {
                RefreshForecastTab(cond, regime, deck, kts, samplePos, airDensity, camera != null);
            }
            else if (shell.Page == TabEnvironment)
            {
                RefreshEnvironmentTab(level, kts, towards);
            }

            // Status strip
            if (shell.Status != null)
            {
                if (weather.IsManualOverride)
                {
                    shell.Status.text = $"MANUAL DEBUG OVERRIDE ACTIVE // {regime.Name.ToUpperInvariant()} FROZEN";
                }
                else if (settings.DynamicWeatherEnabled.Value)
                {
                    float prog = weather.TransitionProgress;
                    shell.Status.text = prog < 1f
                        ? $"REGIME SHIFT IN PROGRESS ({Mathf.RoundToInt(prog * 100f)}%) // BLENDING NATIVE LEVERS"
                        : $"STEADY STATE: {regime.Name.ToUpperInvariant()} // NEXT EVALUATION IN QUEUE";
                }
                else
                {
                    shell.Status.text = "DYNAMIC TRANSITIONS PAUSED // STATIC ENVIRONMENT";
                }
            }
        }

        private void RefreshForecastTab(
            float cond,
            WeatherRegime regime,
            float deck,
            float windKts,
            Vector3 samplePos,
            float airDensity,
            bool hasCamera)
        {
            Color regimeCol = GetRegimeColor(regime.Type);

            if (regimeCardRail != null) regimeCardRail.color = regimeCol;
            if (liveGlyph != null)
            {
                liveGlyph.SetKind(regime.Type);
                liveGlyph.color = regimeCol;
            }
            if (liveRegimeBadge != null)
            {
                liveRegimeBadge.text = regime.Code;
                liveRegimeBadge.color = regimeCol;
            }
            if (liveRegimeBadgeBorder != null) liveRegimeBadgeBorder.color = regimeCol * 0.75f;

            if (liveRegimeTitle != null)
            {
                liveRegimeTitle.text = $"{regime.Name.ToUpperInvariant()}";
            }

            if (liveCoverLabel != null)
            {
                liveCoverLabel.text = $"{Mathf.RoundToInt(cond * 100f)}% COVER";
            }

            if (liveCoverBar != null)
            {
                float fullW = 104f;
                var rt = liveCoverBar.rectTransform;
                rt.sizeDelta = new Vector2(fullW * Mathf.Clamp01(cond), rt.sizeDelta.y);
                liveCoverBar.color = regimeCol;
            }

            if (liveQuickMetrics != null)
            {
                float rain = WeatherForecast.ResolveRainIntensity(cond, weather.ForcedRainIntensity);
                liveQuickMetrics.text = $"DECK {Mathf.RoundToInt(deck)} M   /   WIND {Mathf.RoundToInt(windKts)} KT   /   RAIN {Mathf.RoundToInt(rain * 100f)}%";
            }

            if (liveTacticalBrief != null)
            {
                liveTacticalBrief.text = regime.TacticalBriefing;
            }

            // Airspace Profile & Stratification
            if (profileTrack != null)
            {
                float trackW = profileTrack.rectTransform.sizeDelta.x;
                float cloudBaseNorm = Mathf.Clamp01(deck / 5000f);

                if (profileCloudLayer != null)
                {
                    var rt = profileCloudLayer.rectTransform;
                    rt.sizeDelta = new Vector2(3f, rt.sizeDelta.y);
                    float layerX = Mathf.Clamp(cloudBaseNorm * trackW - rt.sizeDelta.x * 0.5f,
                        0f, trackW - rt.sizeDelta.x);
                    rt.anchoredPosition = new Vector2(
                        profileTrack.rectTransform.anchoredPosition.x + layerX, rt.anchoredPosition.y);
                }

                float ownAlt = samplePos.y;
                float ownNorm = Mathf.Clamp01(ownAlt / 5000f);
                if (profileOwnshipMarker != null)
                {
                    profileOwnshipMarker.enabled = hasCamera;
                    var rt = profileOwnshipMarker.rectTransform;
                    float markerX = Mathf.Clamp(ownNorm * trackW - rt.sizeDelta.x * 0.5f,
                        0f, trackW - rt.sizeDelta.x);
                    rt.anchoredPosition = new Vector2(
                        profileTrack.rectTransform.anchoredPosition.x + markerX, rt.anchoredPosition.y);
                }

                if (profileOwnshipLabel != null)
                {
                    profileOwnshipLabel.text = hasCamera ? $"CAMERA ALTITUDE  {Mathf.RoundToInt(ownAlt):+#;-#;0} M" : "CAMERA ALTITUDE  —";
                }

                if (profileCloudRangeLabel != null)
                {
                    profileCloudRangeLabel.text = $"CLOUD BASE  {Mathf.RoundToInt(deck)} M";
                }

                if (profileStatusBadge != null)
                {
                    if (!hasCamera)
                    {
                        profileStatusBadge.text = "CAMERA UNAVAILABLE";
                        profileStatusBadge.color = AvTheme.Dim;
                    }
                    else if (ownAlt < deck - 50f)
                    {
                        profileStatusBadge.text = "BELOW CLOUD BASE";
                        profileStatusBadge.color = AvTheme.Accent;
                    }
                    else if (ownAlt <= deck + 50f)
                    {
                        profileStatusBadge.text = "NEAR CLOUD BASE";
                        profileStatusBadge.color = AvTheme.Warning;
                    }
                    else
                    {
                        profileStatusBadge.text = "ABOVE CLOUD BASE";
                        profileStatusBadge.color = new Color(0.22f, 0.75f, 0.95f, 1f);
                    }
                }
            }

            // Timeline rows
            ForecastStep[] steps = weather.GetForecastTimeline();
            if (steps != null)
            {
                for (int i = 0; i < steps.Length && i < timelineRows.Count; i++)
                {
                    TimelineRowWidgets row = timelineRows[i];
                    ForecastStep s = steps[i];
                    Color stepCol = GetRegimeColor(s.Regime.Type);
                    if (row.Glyph != null)
                    {
                        row.Glyph.SetKind(s.Regime.Type);
                        row.Glyph.color = stepCol;
                    }
                    bool storm = s.Regime.Type == WeatherRegimeType.Storm;
                    row.RiskRail.color = storm ? AvTheme.RailDanger
                        : s.RainProbability > .2f ? AvTheme.RailCaution : stepCol;

                    if (row.RegimeBadgeText != null)
                    {
                        row.RegimeBadgeText.text = s.Regime.Code;
                        row.RegimeBadgeText.color = stepCol;
                    }

                    if (row.RegimeBadgeBorder != null)
                    {
                        row.RegimeBadgeBorder.color = stepCol * 0.75f;
                    }

                    if (row.ConditionsLabel != null)
                    {
                        row.ConditionsLabel.text = $"{Mathf.RoundToInt(s.Conditions * 100f)}%";
                    }

                    if (row.DeckLabel != null)
                    {
                        row.DeckLabel.text = $"{Mathf.RoundToInt(s.CloudDeckMetres)} M";
                    }

                    if (row.RainFill != null)
                    {
                        float maxW = 92f;
                        float w = maxW * s.RainProbability;
                        var rt = row.RainFill.rectTransform;
                        rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
                        row.RainFill.color = storm ? AvTheme.Alert
                            : s.RainProbability > .2f ? AvTheme.Warning : AvTheme.RailInfo;
                    }

                    if (row.RainText != null)
                    {
                        if (s.RainProbability <= 0.05f)
                        {
                            row.RainText.text = "— DRY —";
                            row.RainText.color = AvTheme.Disabled;
                        }
                        else
                        {
                            row.RainText.text = $"{Mathf.RoundToInt(s.RainProbability * 100f)}% RAIN";
                            row.RainText.color = storm ? AvTheme.Alert
                                : s.RainProbability > .2f ? AvTheme.Warning : AvTheme.RailInfo;
                        }
                    }
                }
            }

            // Local telemetry
            if (localCol1 != null)
            {
                float alt = samplePos.y;
                float soundSpeed = LevelInfo.GetSpeedOfSound(alt);
                localCol1.text = hasCamera ? $"ALT {Mathf.RoundToInt(alt):+#;-#;0} M   DENSITY {airDensity * 100f:F1}% SL   SOUND {Mathf.RoundToInt(soundSpeed)} M/S" : "CAMERA DATA UNAVAILABLE";
            }

            if (localCol2 != null)
            {
                localCol2.text = $"WIND {Mathf.RoundToInt(windKts)} KTS   TURBULENCE {weather.CurrentTurbulence:F2}";
            }
        }

        private void RefreshEnvironmentTab(LevelInfo level, float kts, int towardsDeg)
        {
            // 1. Solar Ephemeris
            SolarData solar = weather.GetSolarData();
            float deck = weather.CurrentCloudHeight;
            bool lowLight = solar.ElevationDegrees <= -6f;
            bool lowDeck = deck < 1600f;
            bool strongWind = kts > 30f;
            if (advisoryRail != null)
                advisoryRail.color = lowDeck || strongWind ? AvTheme.RailCaution : AvTheme.RailInfo;
            if (advisoryLight != null)
            {
                advisoryLight.text = solar.ElevationDegrees > 0f ? "DAYLIGHT" :
                    lowLight ? "NIGHT" : "TWILIGHT";
                advisoryLight.color = lowLight ? AvTheme.RailCaution : AvTheme.TextPrimary;
            }
            if (advisoryDeck != null)
            {
                advisoryDeck.text = (lowDeck ? "LOW " : "BASE ") + Mathf.RoundToInt(deck) + " M";
                advisoryDeck.color = lowDeck ? AvTheme.RailCaution : AvTheme.TextPrimary;
            }
            if (advisoryWind != null)
            {
                advisoryWind.text = (strongWind ? "HIGH " : "") + Mathf.RoundToInt(kts) + " KT";
                advisoryWind.color = strongWind ? AvTheme.RailCaution : AvTheme.TextPrimary;
            }
            if (solarCardRail != null)
            {
                solarCardRail.color = solar.ElevationDegrees > 0f ? AvTheme.Accent : (solar.ElevationDegrees > -12f ? AvTheme.Warning : AvTheme.Dim);
            }

            if (solarStateBadge != null)
            {
                if (solar.PolarDay)
                {
                    solarStateBadge.text = "[ POLAR DAY ]";
                    solarStateBadge.color = AvTheme.Accent;
                }
                else if (solar.PolarNight)
                {
                    solarStateBadge.text = "[ POLAR NIGHT ]";
                    solarStateBadge.color = AvTheme.Alert;
                }
                else if (solar.ElevationDegrees > 0f)
                {
                    solarStateBadge.text = "[ DAYLIGHT ]";
                    solarStateBadge.color = AvTheme.Accent;
                }
                else if (solar.ElevationDegrees > -6f)
                {
                    solarStateBadge.text = "[ CIVIL TWILIGHT ]";
                    solarStateBadge.color = AvTheme.Warning;
                }
                else
                {
                    solarStateBadge.text = "[ NIGHT ]";
                    solarStateBadge.color = new Color(0.35f, 0.45f, 0.65f, 1f);
                }
            }

            if (solarElevationTrack != null && solarElevationMarker != null)
            {
                float trackW = solarElevationTrack.rectTransform.sizeDelta.x;
                float elevNorm = Mathf.Clamp01((solar.ElevationDegrees + 90f) / 180f);
                var rt = solarElevationMarker.rectTransform;
                float markerX = Mathf.Clamp(elevNorm * trackW - rt.sizeDelta.x * 0.5f,
                    0f, trackW - rt.sizeDelta.x);
                rt.anchoredPosition = new Vector2(
                    solarElevationTrack.rectTransform.anchoredPosition.x + markerX,
                    rt.anchoredPosition.y);
            }

            if (solarElevationText != null)
            {
                solarElevationText.text = $"ELEVATION: {solar.ElevationDegrees:+#0.0;-#0.0;0.0}° {(solar.ElevationDegrees >= 0f ? "ABOVE HORIZON" : "BELOW HORIZON")}";
            }

            if (solarAzimuthText != null)
            {
                solarAzimuthText.text = $"SUN BEARING {Mathf.RoundToInt(solar.AzimuthDegrees):D3}°    SIM TIME {level.timeOfDay:F2}H";
            }

            if (solarEventsText != null)
            {
                solarEventsText.text = solar.PolarDay ? "CONTINUOUS POLAR DAYLIGHT (NO SUNSET)"
                    : solar.PolarNight ? "CONTINUOUS POLAR NIGHT (NO SUNRISE)"
                    : $"{solar.NextEventName.ToUpperInvariant()} IN {Mathf.RoundToInt(solar.TimeToNextEventMinutes)} MIN";
            }

            // 2. Lunar Ephemeris
            LunarData lunar = weather.GetLunarData();
            if (lunarCardRail != null) lunarCardRail.color = new Color(0.6f, 0.8f, 1.0f, 1f);

            if (lunarPhaseText != null)
            {
                lunarPhaseText.text = $"{lunar.PhaseName.ToUpperInvariant()}  /  {Mathf.RoundToInt(lunar.IlluminationFraction * 100f)}% LIT";
            }

            if (lunarIllumFill != null)
            {
                float barW = 120f;
                var rt = lunarIllumFill.rectTransform;
                rt.sizeDelta = new Vector2(barW * lunar.IlluminationFraction, rt.sizeDelta.y);
            }

            if (lunarIllumText != null)
            {
                lunarIllumText.text = $"MODEL MOONLIGHT {Mathf.RoundToInt(lunar.MoonlightIntensity * 100f)}%   /   {(lunar.IsMoonless ? "LOW LIGHT" : "MOONLIT")}";
            }

            if (lunarTacticalGuidance != null)
            {
                lunarTacticalGuidance.text = lunar.IsMoonless
                    ? "LOW NATURAL LIGHT // VISUAL IDENTIFICATION MAY BE HARDER"
                    : "MOONLIGHT PRESENT // CHECK CLOUD COVER FOR VISIBILITY";
            }

            // 3. Wind Dynamics
            WeatherForecast.FormatWind(weather.CurrentWindVelocity.x, weather.CurrentWindVelocity.z, out _, out _, out int fromDeg);
            if (windCardRail != null) windCardRail.color = kts > 25f ? AvTheme.Warning : AvTheme.Accent;
            if (windNeedle != null)
            {
                windNeedle.enabled = true;
                windNeedle.color = kts > 25f ? AvTheme.Warning : AvTheme.RailInfo;
                windNeedle.rectTransform.localEulerAngles = new Vector3(0f, 0f, -towardsDeg);
            }

            if (windSummaryText != null)
            {
                windSummaryText.text = $"WIND {Mathf.RoundToInt(kts)} KTS    FROM {fromDeg:D3}°    TO {towardsDeg:D3}°";
            }

            if (windShearText != null)
            {
                windShearText.text = $"MISSION WIND  |  TURBULENCE: {weather.CurrentTurbulence:F2}";
            }

            if (windFlightAssistText != null)
            {
                windFlightAssistText.text = kts > 30f
                    ? "STRONG WIND // DRIFT AND TURBULENCE"
                    : "NORMAL WIND // WATCH FOR DRIFT";
            }

            // 4. Atmosphere & Performance
            Camera camera = Camera.main;
            float rho = camera != null ? LevelInfo.GetAirDensity(camera.transform.position.y) : 0f;
            float sos = camera != null ? LevelInfo.GetSpeedOfSound(camera.transform.position.y) : 0f;

            if (atmoCardRail != null)
            {
                atmoCardRail.color = rho < 0.6f ? AvTheme.Warning : AvTheme.Accent;
            }
            if (atmoDensityFill != null)
            {
                atmoDensityFill.rectTransform.sizeDelta = new Vector2(
                    camera != null ? 98f * Mathf.Clamp01(rho) : 0f, 10f);
                atmoDensityFill.color = rho < .6f ? AvTheme.Warning : AvTheme.RailInfo;
            }

            if (atmoDensityText != null)
            {
                atmoDensityText.text = camera != null ? $"AIR DENSITY  {rho * 100f:F1}% OF SEA LEVEL" : "AIR DENSITY  —  /  CAMERA UNAVAILABLE";
            }

            if (atmoSpeedOfSoundText != null)
            {
                atmoSpeedOfSoundText.text = camera != null ? $"SPEED OF SOUND  {Mathf.RoundToInt(sos)} M/S  /  {Mathf.RoundToInt(sos * 3.6f)} KM/H" : "SPEED OF SOUND  —";
            }

            if (atmoAltitudeText != null)
            {
                atmoAltitudeText.text = camera != null ? $"SAMPLED ALTITUDE  {Mathf.RoundToInt(camera.transform.position.y):+#;-#;0} M" : "NO CAMERA ALTITUDE AVAILABLE";
            }
        }

        private static Color GetRegimeColor(WeatherRegimeType type)
        {
            switch (type)
            {
                case WeatherRegimeType.Clear:
                case WeatherRegimeType.Fair:
                    return new Color(0.22f, 0.75f, 0.95f, 1f); // Sky Cyan
                case WeatherRegimeType.Scattered:
                    return AvTheme.Accent; // Mint Green
                case WeatherRegimeType.Broken:
                case WeatherRegimeType.Overcast:
                    return AvTheme.Warning; // Amber
                case WeatherRegimeType.RainSquall:
                    return AvTheme.Warning;
                case WeatherRegimeType.Storm:
                    return AvTheme.Alert; // Alert Red
                default:
                    return AvTheme.Accent;
            }
        }
    }
}
