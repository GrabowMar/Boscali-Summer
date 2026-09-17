using System;
using System.Collections.Generic;
using System.Text;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>
    /// "WEA" — the environment panel. Observation first: the hazard banner and the current
    /// conditions carry the weight and the top of the page, then the single wind rose, the
    /// nearest front and the deterministic forecast. The host alone gets one compact row of
    /// schedule control; a client reads the same forecast, because every peer derives it from
    /// the mission clock and nothing here is transmitted.
    ///
    /// <para>Every block is measured and arranged once, at build, to fit the bay the panel was
    /// placed in. Nothing scrolls and nothing moves at refresh: a refresh writes text, tints and
    /// needle rotations only.</para>
    /// </summary>
    internal sealed class WeatherMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        private const int ChipCount = 2;
        private const int PageEnvironment = 0;
        private const int PageRadar = 1;

        // ---- Layout grid, in AvTokens spacing steps --------------------------------------
        private const float RowHeight = 16f;
        private const float RowPitch = 17f;
        private const float RowPitchMax = 34f;
        private const float HeaderHeight = 22f;
        private const float BannerHeight = 38f;
        private const float CellHeight = 32f;
        private const float CellGap = 8f;
        private const float FrontRowHeight = 18f;
        private const float LegendHeight = 30f;
        private const float ControlHeight = 22f;
        private const float TrackGap = 8f;
        private const float TrackHeight = 6f;
        private const float TrackLabelHeight = 14f;
        private const float RoseBase = 76f;
        private const float RoseMax = 132f;
        private const float SectionGap = 6f;
        private const float SectionGapMax = 20f;
        private const float ToggleWidth = 54f;
        private const float TickHeight = 10f;
        private const float MarkerHeight = 12f;
        private const float BarWidth = 3f;
        private const float BarGap = 2f;
        private const float BarHeight = 12f;
        private const float BarBlock = 4f * BarWidth + 3f * BarGap;

        private const int BarCount = 4;
        private const int RampRungs = 4;

        private const int ObsCategory = 0;
        private const int ObsCloudBase = 1;
        private const int ObsVisibility = 2;
        private const int ObsTemperature = 3;
        private const int ObsPrecipitation = 4;
        private const int ObsOcclusion = 5;
        private const int ObsCount = 6;

        /// <summary>Rain below this is not worth naming as precipitation, the HUD's own floor.</summary>
        private const float PrecipitationVisible = 0.02f;

        private const string ScheduleOnTooltip =
            "The schedule drives the sky. Turn OFF to hold the current sky and set it by hand.";
        private const string ScheduleOffTooltip =
            "The sky is held by an override. Turn ON to release it and resume the schedule.";

        private WeatherSettings settings;
        private WeatherManager manager;

        private WeatherRadarPage radar;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvScreen shell;
        private int rowCapacity;
        private float trackWidth;

        private TMP_Text bannerTier;
        private TMP_Text bannerDetail;
        private TMP_Text bannerHazard;
        private Image bannerRail;
        private readonly Image[] bannerBar = new Image[BarCount];

        private readonly TMP_Text[] obsValue = new TMP_Text[ObsCount];
        private readonly TMP_Text[] obsKey = new TMP_Text[ObsCount];
        private readonly string[] obsKeyText = new string[ObsCount];
        private readonly Image[] obsBar = new Image[BarCount];

        private RectTransform roseMean;
        private RectTransform roseGust;
        private RectTransform roseVeer;
        private TMP_Text roseMeanText;
        private TMP_Text roseGustText;
        private TMP_Text roseVeerText;
        private TMP_Text roseShearText;

        private TMP_Text frontGlyph;
        private TMP_Text frontText;

        private TMP_Text forecastLegend;
        private TMP_Text forecastKey;

        private TMP_Text controlClock;
        private TMP_Text controlNext;
        private TMP_Text trackLeft;
        private TMP_Text trackRight;
        private Image trackFill;
        private RectTransform marker;
        private readonly Image[] trackTicks = new Image[WeatherForecast.MaxEntries];

        private readonly List<ForecastRow> forecastRows = new List<ForecastRow>(WeatherForecast.MaxEntries);

        private AvButton scheduleButton;
        private AvTooltipTarget scheduleHover;

        private readonly Color[] flightInk = new Color[RampRungs];
        private readonly Color[] flightFill = new Color[RampRungs];
        private readonly Color[] tierInk = new Color[RampRungs];
        private readonly Color[] tierFill = new Color[RampRungs];
        private readonly Color[] regimeInk = new Color[RampRungs];
        private readonly Color[] regimeChipFill = new Color[RampRungs];
        private readonly Color[] regimeRail = new Color[RampRungs];

        private readonly StringBuilder text = new StringBuilder(192);

        private string actionEcho;
        private float actionEchoUntil;
        private float nextAttempt;
        private float nextRefresh;
        private bool wasVisible;
        private bool failed;

        public void Configure(WeatherSettings config, WeatherManager weatherManager)
        {
            settings = config;
            manager = weatherManager;
        }

        public void ResetForScene()
        {
            MfdScreenHost.Release(MfdSlots.Weather);
            if (radar != null)
            {
                radar.Reset();
                radar = null;
            }
            if (screenRoot != null) UnityEngine.Object.Destroy(screenRoot);

            screenRoot = null;
            screen = null;
            shell = null;
            rowCapacity = 0;
            trackWidth = 0f;

            bannerTier = null;
            bannerDetail = null;
            bannerHazard = null;
            bannerRail = null;
            Array.Clear(bannerBar, 0, bannerBar.Length);
            Array.Clear(obsValue, 0, obsValue.Length);
            Array.Clear(obsKey, 0, obsKey.Length);
            Array.Clear(obsBar, 0, obsBar.Length);

            roseMean = null;
            roseGust = null;
            roseVeer = null;
            roseMeanText = null;
            roseGustText = null;
            roseVeerText = null;
            roseShearText = null;

            frontGlyph = null;
            frontText = null;
            forecastLegend = null;
            forecastKey = null;

            controlClock = null;
            controlNext = null;
            trackLeft = null;
            trackRight = null;
            trackFill = null;
            marker = null;
            Array.Clear(trackTicks, 0, trackTicks.Length);

            forecastRows.Clear();
            scheduleButton = null;
            scheduleHover = null;
            actionEcho = null;
            actionEchoUntil = 0f;
            nextAttempt = 0f;
            nextRefresh = 0f;
            wasVisible = false;
            failed = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || settings == null || manager == null) return;
            if (Application.isBatchMode) { failed = true; return; }
            if (!settings.Enabled.Value) { failed = true; return; }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

            // A manager that latched failed has nothing to read; take the panel down with it
            // rather than render dashes for the rest of the session.
            if (!manager.Enabled)
            {
                ResetForScene();
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
            if (visible && (!wasVisible || Time.unscaledTime >= nextRefresh))
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
            wasVisible = visible;
        }

        // ---- Installation ----------------------------------------------------------------

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
                    // The six vanilla slots are for WMC and the claimed screens; WEA owns an
                    // appended one, so the only failure here is a missing host adapter.
                    failed = true;
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
                }
            }
            catch (Exception)
            {
                ResetForScene();
                failed = true;
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

            // Two tabs, and only two: everything else the bar used to pretend to tab is a status
            // chip. No metric row — the observation block below the tabs carries the numbers and
            // needs the room the row would have taken.
            shell = AvScreen.Build(
                content, MfdSlots.Weather,
                new[] { "ENV", "RADAR" },
                null,
                ChipCount, Width, height, _ => nextRefresh = 0f);

            ResolvePalette();

            BuildEnvironmentPage(shell.CreatePage(PageEnvironment, "EnvironmentPage"));
            BuildRadarPage(shell.CreatePage(PageRadar, "RadarPage"));

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

            shell.SetPage(PageEnvironment);
            return result;
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

        /// <summary>
        /// Resolve the severity ramps once, at build. The refresh pass then indexes a colour
        /// instead of asking the stylesheet, so a tint costs one array read and no lookup.
        /// </summary>
        private void ResolvePalette()
        {
            FlightCategory[] categories =
            {
                FlightCategory.Vfr, FlightCategory.Mvfr, FlightCategory.Ifr, FlightCategory.Lifr
            };
            for (int i = 0; i < categories.Length; i++)
            {
                AvStyle rail = AvStyleHost.Style(WeatherReadout.FlightRailClass(categories[i]));
                AvStyle chip = AvStyleHost.Style(WeatherReadout.FlightChipClass(categories[i]));
                flightFill[i] = AvStyleHost.Resolve(rail.Background, AvTheme.RailInert);
                flightInk[i] = AvStyleHost.Resolve(chip.Color, AvTheme.Dim);
            }

            for (int tier = 0; tier < RampRungs; tier++)
            {
                StormWarning warning = (StormWarning)tier;
                AvStyle rail = AvStyleHost.Style(StormReadout.RailClass(warning));
                AvStyle chip = AvStyleHost.Style(StormReadout.ChipClass(warning));
                tierFill[tier] = AvStyleHost.Resolve(rail.Background, AvTheme.RailInert);
                tierInk[tier] = AvStyleHost.Resolve(chip.Color, AvTheme.Dim);
            }

            for (int i = 0; i < WeatherRegimes.Count; i++)
            {
                WeatherRegime regime = WeatherRegimes.FromIndex(i);
                int rung = Mathf.Clamp(WeatherReadout.Severity(regime) - 1, 0, RampRungs - 1);
                regimeRail[rung] = AvStyleHost.Resolve(
                    AvStyleHost.Style(WeatherReadout.RailClass(regime)).Background, AvTheme.RailInert);
                AvStyle chip = AvStyleHost.Style(WeatherReadout.ChipClass(regime));
                regimeChipFill[rung] = AvStyleHost.Resolve(chip.Background, AvTheme.SurfaceInert);
                regimeInk[rung] = AvStyleHost.Resolve(chip.Color, AvTheme.Dim);
            }
        }

        // ---- Page ------------------------------------------------------------------------

        private void BuildEnvironmentPage(GameObject page)
        {
            Rect body = shell.Body;
            var pageRect = (RectTransform)page.transform;
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;
            AvStyled.Spine(pageRect, new Rect(body.x, body.y, 3f, body.height));

            bool host = manager != null && manager.HostAuthority;
            int configured = settings != null
                ? Mathf.Clamp(settings.ForecastSteps.Value, 1, WeatherForecast.MaxEntries)
                : WeatherForecast.DefaultSteps;
            float controlBlock = host
                ? HeaderHeight + ControlHeight + TrackGap + TrackHeight + TrackLabelHeight
                : 0f;

            // The observation block, the section headers and the legends are fixed. What is left
            // over is split between the wind rose, the forecast row pitch and the four section
            // gaps, so the page fills its bay and never leaves a hole above CONTROL. When the bay
            // is too short for even one row, the row count drops instead of overflowing.
            float fixedHeight = HeaderHeight + BannerHeight + AvTokens.Space1 + CellHeight
                              + AvTokens.Space1 + CellHeight
                              + HeaderHeight + HeaderHeight + FrontRowHeight
                              + HeaderHeight + LegendHeight;
            float flexible = Mathf.Max(0f, body.height - fixedHeight - controlBlock - 4f * SectionGap);

            float roseSize = Mathf.Clamp(flexible * 0.28f, RoseBase, RoseMax);
            int capacity = Mathf.FloorToInt((flexible - roseSize) / RowPitch);
            rowCapacity = Mathf.Clamp(Mathf.Min(configured, capacity), 0, WeatherForecast.MaxEntries);
            float pitch = rowCapacity > 0
                ? Mathf.Clamp((flexible - roseSize) / rowCapacity, RowPitch, RowPitchMax)
                : RowPitch;
            float residual = Mathf.Max(0f, flexible - roseSize - pitch * rowCapacity);
            float gap = SectionGap + Mathf.Clamp(residual / 4f, 0f, SectionGapMax);

            float y = body.y;
            y = SectionHeader(pageRect, x, y, width, "CONDITIONS", "OBSERVATION", band: false);
            BuildBanner(pageRect, x, y, width);
            y -= BannerHeight + AvTokens.Space1;

            float column = (width - CellGap * 2f) / 3f;
            for (int i = 0; i < ObsCount; i++)
            {
                float cx = x + i % 3 * (column + CellGap);
                float cy = y - i / 3 * (CellHeight + AvTokens.Space1);
                BuildObsCell(pageRect, cx, cy, column, i);
            }
            y -= CellHeight * 2f + AvTokens.Space1 + gap;

            y = SectionHeader(pageRect, x, y, width, "WIND", "MEAN · GUST · VEER · SHEAR", band: false);
            BuildRose(pageRect, x, y, width, roseSize);
            y -= roseSize + gap;

            y = SectionHeader(pageRect, x, y, width, "FRONT", "NEAREST BOUNDARY", band: false);
            frontGlyph = AvStyled.Label(pageRect, new Rect(x, y, 26f, FrontRowHeight), "", "row-name");
            frontText = AvStyled.Label(pageRect, new Rect(x + 26f, y, width - 26f, FrontRowHeight), "",
                "row-main", align: TextAlignmentOptions.MidlineLeft);
            frontText.enableWordWrapping = false;
            frontText.overflowMode = TextOverflowModes.Ellipsis;
            y -= FrontRowHeight + gap;

            y = SectionHeader(pageRect, x, y, width, "FORECAST", "IDENTICAL ON EVERY PEER", band: true);
            forecastLegend = AvStyled.Label(pageRect, new Rect(x, y, width, 14f),
                "▲ BUILDING   ▼ EASING   = STEADY", "section-title-note");
            forecastKey = AvStyled.Label(pageRect, new Rect(x, y - 14f, width, 14f),
                "CHIP COLOUR = SEVERITY   ·   WIND = POINT SPEED (CHANGE M/S)", "section-title-note");
            y -= LegendHeight;

            forecastRows.Clear();
            for (int i = 0; i < rowCapacity; i++)
            {
                forecastRows.Add(ForecastRow.Build(pageRect, x, y - i * pitch, width, pitch));
            }
            y -= pitch * rowCapacity + gap;

            if (host) BuildControl(pageRect, x, y, width);
        }

        private void BuildRadarPage(GameObject page)
        {
            Rect body = shell.Body;
            var pageRect = (RectTransform)page.transform;
            // The radar stack is fixed-height and owned by the radar page, so it keeps the one
            // viewport in this panel: only it can still be taller than the bay.
            RectTransform parent = AvScreen.Scroll(pageRect, body, 560f, out body);
            radar = new WeatherRadarPage(parent, body.x + AvScreen.SpineInset, body.y,
                body.width - AvScreen.SpineInset, settings);
            radar.Build(parent, body.x + AvScreen.SpineInset, body.y, body.width - AvScreen.SpineInset);
        }

        private void RefreshRadar(WeatherSnapshot snapshot)
        {
            if (radar == null) return;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return;
            Vector3 position = cameras.transform.position;
            radar.Refresh(snapshot, manager, position.x, position.z);
        }

        private static float SectionHeader(
            RectTransform parent, float x, float y, float width, string title, string note, bool band)
        {
            if (band) AvStyled.Box(parent, new Rect(x - 6f, y + 4f, width + 12f, 22f), "section band");
            AvStyled.SpineTick(parent, x - AvScreen.SpineInset + 3f, y - 7f);

            float titleWidth = width * 0.5f;
            AvStyled.Label(parent, new Rect(x, y, titleWidth, 14f), title, "section-title");

            if (!string.IsNullOrEmpty(note))
            {
                AvStyled.Label(parent, new Rect(x + titleWidth, y, width - titleWidth, 14f),
                               note, "section-title-note", align: TextAlignmentOptions.MidlineRight);
            }
            return y - HeaderHeight;
        }

        /// <summary>
        /// The hazard banner. It is the top of the page and the largest type on it: the tier
        /// word and its bars, then the cell and the hazard in words.
        /// </summary>
        private void BuildBanner(RectTransform parent, float x, float y, float width)
        {
            AvStyled.Box(parent, new Rect(x, y, width, BannerHeight), "section band");
            bannerRail = AvKit.Rule(parent, new Rect(x, y, 3f, BannerHeight), AvTheme.RailInert);
            bannerTier = AvStyled.Label(parent, new Rect(x + 12f, y, width - 12f, 22f), "", "page-title");

            float barX = x + width - BarBlock - 2f;
            for (int i = 0; i < BarCount; i++)
            {
                bannerBar[i] = AvKit.Rule(parent,
                    new Rect(barX + i * (BarWidth + BarGap), y - 6f, BarWidth, BarHeight), AvTheme.RailInert);
            }

            // Two labels, not one wrapped sentence: the cell reads on the left and the hazard
            // verdict reads on the right, so neither can crowd the other into an ellipsis.
            float detailWidth = width * 0.62f;
            bannerDetail = AvStyled.Label(parent, new Rect(x + 12f, y - 22f, detailWidth, 14f), "",
                "section-title-note");
            bannerHazard = AvStyled.Label(parent, new Rect(x + 12f + detailWidth, y - 22f,
                width - detailWidth - 24f, 14f), "", "section-title-note",
                align: TextAlignmentOptions.MidlineRight);
        }

        /// <summary>One observation cell: the value, its severity bars where it has a ramp, and its name.</summary>
        private void BuildObsCell(RectTransform parent, float x, float y, float width, int index)
        {
            bool category = index == ObsCategory;
            float valueWidth = category ? width - BarBlock - 6f : width;

            TMP_Text value = AvStyled.Label(parent, new Rect(x, y, valueWidth, 17f), "", "row-name");
            value.fontSize = AvTokens.FontTitle;
            obsValue[index] = value;

            if (category)
            {
                float barX = x + width - BarBlock - 2f;
                for (int i = 0; i < BarCount; i++)
                {
                    obsBar[i] = AvKit.Rule(parent,
                        new Rect(barX + i * (BarWidth + BarGap), y, BarWidth, BarHeight), AvTheme.RailInert);
                }
            }

            obsKey[index] = AvStyled.Label(parent, new Rect(x, y - 17f, width, 13f), "", "section-title-note");
        }

        private void BuildRose(RectTransform parent, float x, float y, float width, float size)
        {
            var roseObject = new GameObject("WindRose", typeof(RectTransform), typeof(Image));
            var rose = roseObject.GetComponent<RectTransform>();
            rose.SetParent(parent, false);
            AvKit.Place(rose, new Rect(x, y, size, size));

            Image ground = roseObject.GetComponent<Image>();
            ground.sprite = AvSprites.Control;
            ground.type = Image.Type.Sliced;
            ground.color = AvTheme.SurfaceInert;
            ground.raycastTarget = false;

            AvKit.Outline(rose, new Rect(0f, 0f, size, size), AvTheme.Hairline);
            float centre = size * 0.5f;
            AvKit.Rule(rose, new Rect(centre - 0.5f, 0f, 1f, size), AvTheme.Hairline);
            AvKit.Rule(rose, new Rect(0f, -centre, size, 1f), AvTheme.Hairline);
            AvKit.Rule(rose, new Rect(centre - 1f, 0f, 2f, 6f), AvTheme.TextPrimary);

            // Needles pivot at the hub and point at the heading they came from, so a refresh
            // rotates one transform and rebuilds nothing.
            roseVeer = Needle(rose, size * 0.30f, 1f, AvTheme.RailInfo);
            roseGust = Needle(rose, size * 0.46f, 2f, AvTheme.RailCaution);
            roseMean = Needle(rose, size * 0.40f, 2f, AvTheme.Accent);

            float legendX = x + size + AvTokens.Space3;
            float legendWidth = width - size - AvTokens.Space3;
            float line = y;
            roseMeanText = AvStyled.Label(parent, new Rect(legendX, line, legendWidth, 14f), "", "kv-key");
            line -= 15f;
            roseGustText = AvStyled.Label(parent, new Rect(legendX, line, legendWidth, 14f), "", "kv-key");
            line -= 15f;
            roseVeerText = AvStyled.Label(parent, new Rect(legendX, line, legendWidth, 14f), "", "kv-key");
            line -= 15f;
            roseShearText = AvStyled.Label(parent, new Rect(legendX, line, legendWidth, 14f), "", "kv-key");
        }

        private static RectTransform Needle(RectTransform rose, float length, float width, Color color)
        {
            var rect = AvKit.Rule(rose, new Rect(0f, 0f, width, length), color).rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, length);
            return rect;
        }

        private void BuildControl(RectTransform parent, float x, float y, float width)
        {
            y = SectionHeader(parent, x, y, width, "CONTROL", "HOST ONLY", band: false);

            scheduleHover = RowHover(parent, new Rect(x, y, width, ControlHeight), ScheduleOnTooltip);
            AvStyled.Label(parent, new Rect(x, y, 78f, ControlHeight), "SCHEDULE", "row-value",
                align: TextAlignmentOptions.MidlineLeft);
            controlClock = AvStyled.Label(parent, new Rect(x + 78f, y, 72f, ControlHeight), "", "kv-key");
            controlNext = AvStyled.Label(
                parent, new Rect(x + 150f, y, width - 150f - ToggleWidth - 6f, ControlHeight),
                "", "kv-key", align: TextAlignmentOptions.MidlineRight);
            scheduleButton = AvStyled.Button(
                parent, new Rect(x + width - ToggleWidth, y, ToggleWidth, ControlHeight),
                "ON", "btn", ToggleSchedule);
            scheduleButton.WithTooltip(ScheduleOnTooltip);
            y -= ControlHeight + TrackGap;

            var trackObject = new GameObject("ScheduleTrack", typeof(RectTransform), typeof(Image));
            var track = trackObject.GetComponent<RectTransform>();
            track.SetParent(parent, false);
            AvKit.Place(track, new Rect(x, y, width, TrackHeight));
            trackWidth = width;
            Image trackGround = trackObject.GetComponent<Image>();
            trackGround.sprite = AvSprites.Control;
            trackGround.type = Image.Type.Sliced;
            trackGround.color = AvTheme.Unity(AvTokens.Hairline);
            trackGround.raycastTarget = false;

            trackFill = AvKit.Rule(track, new Rect(0f, 0f, 0f, TrackHeight), AvTheme.RailCaution);

            marker = AvKit.Rule(track, new Rect(0f, 0f, 2f, MarkerHeight), AvTheme.RailCaution).rectTransform;
            marker.anchorMin = marker.anchorMax = new Vector2(0f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.anchoredPosition = Vector2.zero;

            for (int i = 0; i < trackTicks.Length; i++)
            {
                trackTicks[i] = AvKit.Rule(track, new Rect(0f, 0f, 1f, TickHeight), AvTheme.RailInert);
                trackTicks[i].rectTransform.anchorMin = trackTicks[i].rectTransform.anchorMax = new Vector2(0f, 0.5f);
                trackTicks[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
                trackTicks[i].gameObject.SetActive(false);
            }
            y -= TrackHeight;

            trackLeft = AvStyled.Label(parent, new Rect(x, y, width * 0.5f, TrackLabelHeight), "", "kv-key");
            trackRight = AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, TrackLabelHeight),
                "", "kv-key", align: TextAlignmentOptions.MidlineRight);
        }

        private static AvTooltipTarget RowHover(RectTransform parent, Rect area, string tooltip)
        {
            Image background = AvKit.Panel(parent, area, Color.clear);
            background.raycastTarget = true;
            var target = background.gameObject.AddComponent<AvTooltipTarget>();
            target.Initialise(tooltip);
            target.SetTint(background, Color.clear, AvTheme.Unity(AvTokens.SurfaceRaised.WithAlpha(0.5f)));
            return target;
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void Refresh()
        {
            if (shell == null || manager == null) return;

            WeatherSnapshot snapshot = manager.Snapshot;
            WeatherForecast forecast = manager.Forecast;
            bool available = snapshot.Available;
            WeatherState live = snapshot.Live;
            Atmosphere atmosphere = snapshot.Atmosphere;
            WeatherFront front = snapshot.Front;
            StormWarning warning = snapshot.Warning;

            // Exactly two status chips: who owns the sky, and whether the schedule still does.
            bool host = manager.HostAuthority;
            shell.DataBar.SetChip(0, host ? "HOST" : "CLIENT", host ? "live" : "inert");
            bool overridden = manager.OverrideActive || snapshot.Overridden;
            shell.DataBar.SetChip(1, overridden ? "OVERRIDE" : "SCHEDULE",
                                  overridden ? "warn" : available ? "live" : "inert");

            // The state line's budget is the bar minus the tag and the two chips. Keep it short:
            // the truncated title the old panel wore was the reason to measure this at all.
            text.Length = 0;
            text.Append(available ? WeatherReadout.Regime(live.Regime) : WeatherReadout.Unknown);
            if (available)
            {
                text.Append(" — ");
                text.Append(StormReadout.Warning(warning));
            }
            shell.DataBar.State.text = text.ToString();

            RefreshBanner(snapshot, atmosphere, warning);
            RefreshObservation(snapshot, atmosphere, live, available);
            RefreshRose(snapshot, atmosphere, front, live);
            RefreshFront(atmosphere, front);
            RefreshForecast(snapshot, forecast);
            RefreshControl(snapshot, forecast, front);
            RefreshRadar(snapshot);

            // The hazard already owns the top of the page and the banner, so the strip stays
            // quiet: a host action or an armed map gesture, else the mission clock.
            string echo = Time.unscaledTime < actionEchoUntil ? actionEcho : null;
            text.Length = 0;
            text.Append("MISSION T+");
            text.Append(WeatherReadout.Clock(available ? snapshot.MissionTime : manager.MissionTime));
            shell.WriteStatus(
                available ? null : "NO READOUT — NO WEATHER SCHEDULE ON THIS MISSION",
                echo ?? MapPicker.Prompt,
                text.ToString());
        }

        private void RefreshBanner(WeatherSnapshot snapshot, Atmosphere atmosphere, StormWarning warning)
        {
            int tier = Mathf.Clamp((int)warning, 0, RampRungs - 1);
            bannerTier.text = StormReadout.Warning(warning);
            bannerTier.color = tierInk[tier];
            bannerRail.color = tierFill[tier];
            for (int i = 0; i < BarCount; i++)
            {
                bannerBar[i].color = i < tier ? tierFill[tier] : AvTheme.RailInert;
            }

            text.Length = 0;
            if (warning != StormWarning.None && snapshot.Available)
            {
                float x = ReaderX();
                float z = ReaderZ();
                StormCell source = snapshot.WarningSource;
                text.Append(StormReadout.Kind(source.Kind));
                text.Append("  ");
                text.Append(StormReadout.NauticalMiles(source.DistanceTo(x, z)));
                text.Append("  ");
                text.Append(WeatherReadout.Compass16(StormReadout.BearingDegrees(x, z, source.X, source.Z)));
            }
            else if (atmosphere.Available)
            {
                text.Append(atmosphere.IsSevereConvection ? "CONVECTIVE" : "STABLE AIR");
                text.Append("   ·   ");
                text.Append(AirMasses.Name(atmosphere.AirMass));
            }
            bannerDetail.text = text.ToString();
            bannerHazard.text = Hazard(snapshot);
        }

        /// <summary>
        /// The current conditions. Without the new physics the panel still has the synced cloud
        /// base, cloud cover and rain to read, so those cells stay live and the rest say so.
        /// </summary>
        private void RefreshObservation(
            WeatherSnapshot snapshot, Atmosphere atmosphere, WeatherState live, bool available)
        {
            bool hasAtmosphere = available && atmosphere.Available;
            int rung = 0;

            if (hasAtmosphere)
            {
                rung = Mathf.Clamp(WeatherReadout.FlightSeverity(atmosphere.Category), 0, RampRungs - 1);
                obsValue[ObsCategory].text = Atmospheres.Label(atmosphere.Category);
            }
            else if (available)
            {
                // No physics yet: the schedule's regime is the honest headline, and its own
                // severity rank paints the same ramp.
                rung = Mathf.Clamp(WeatherReadout.Severity(live.Regime) - 1, 0, RampRungs - 1);
                obsValue[ObsCategory].text = WeatherReadout.Regime(live.Regime);
            }
            else
            {
                obsValue[ObsCategory].text = WeatherReadout.Unknown;
            }
            obsValue[ObsCategory].color = available ? flightInk[rung] : AvTheme.Disabled;
            for (int i = 0; i < BarCount; i++)
            {
                obsBar[i].color = available && i < rung ? flightFill[rung] : AvTheme.RailInert;
            }

            // The vanilla cloud base is the ceiling in every way that matters, so it reads in
            // both units whether or not the new physics has landed.
            float baseMetres = hasAtmosphere ? atmosphere.Lcl : live.CloudBase;
            bool hasBase = available && !float.IsNaN(baseMetres);
            obsValue[ObsCloudBase].text = hasBase ? WeatherReadout.Feet(baseMetres) : WeatherReadout.Unknown;
            obsKeyText[ObsCloudBase] = hasBase ? WeatherReadout.MetersAgL(baseMetres) : "CLOUD BASE";

            obsValue[ObsVisibility].text = hasAtmosphere
                ? WeatherReadout.Kilometres(atmosphere.Visibility) : WeatherReadout.Unknown;
            obsKeyText[ObsVisibility] = "VISIBILITY";

            obsValue[ObsTemperature].text = hasAtmosphere
                ? WeatherReadout.Celsius(atmosphere.TemperatureC) : WeatherReadout.Unknown;
            obsKeyText[ObsTemperature] = hasAtmosphere
                ? "DEW " + WeatherReadout.Celsius(atmosphere.DewpointC) : "TEMP / DEWPOINT";

            obsValue[ObsPrecipitation].text = Precipitation(snapshot, atmosphere, hasAtmosphere);
            obsKeyText[ObsPrecipitation] = "PRECIPITATION";

            obsValue[ObsOcclusion].text = available
                ? WeatherReadout.Percent01(snapshot.CloudOcclusion) : WeatherReadout.Unknown;
            obsKeyText[ObsOcclusion] = "CLOUD OCCLUSION";

            // The key has to match what the value actually is: a category when the physics has
            // landed, the schedule's regime when it has not.
            obsKeyText[ObsCategory] = hasAtmosphere ? "FLIGHT CATEGORY" : "SKY";
            for (int i = 0; i < ObsCount; i++) obsKey[i].text = obsKeyText[i];
        }

        private static string Precipitation(WeatherSnapshot snapshot, Atmosphere atmosphere, bool hasAtmosphere)
        {
            if (hasAtmosphere) return Atmospheres.Label(atmosphere.Precipitation);
            if (snapshot.RainIntensity <= PrecipitationVisible) return Atmospheres.Label(PrecipitationKind.None);
            return Atmospheres.Label(snapshot.Precipitation);
        }

        /// <summary>
        /// One rose for every wind reading: the synced mean, the gust, the veer the arriving air
        /// mass carries and the shear. Mean and gust share a heading, so the gust needle simply
        /// reaches further; the veer needle points where the wind is going.
        /// </summary>
        private void RefreshRose(WeatherSnapshot snapshot, Atmosphere atmosphere, WeatherFront front, WeatherState live)
        {
            bool hasAtmosphere = snapshot.Available && atmosphere.Available;
            float heading = live.WindHeading;

            PlaceNeedle(roseMean, heading);
            PlaceNeedle(roseGust, heading);
            SetActive(roseGust.gameObject, hasAtmosphere && atmosphere.GustSpeed > live.WindSpeed + 0.5f);

            // WeatherFront.Behind is the air mass the boundary leaves in its wake, which is the
            // one the reader ends up in once it has passed.
            float arriving = front.Present ? AirMasses.Get(front.Behind).WindHeading : heading;
            PlaceNeedle(roseVeer, arriving);
            SetActive(roseVeer.gameObject, front.Present);

            text.Length = 0;
            text.Append("MEAN   ");
            text.Append(snapshot.Available
                ? WeatherReadout.Wind(live.WindSpeed, live.WindHeading)
                : WeatherReadout.Unknown);
            roseMeanText.text = text.ToString();

            text.Length = 0;
            text.Append("GUST   ");
            if (hasAtmosphere)
            {
                text.Append(WeatherReadout.Speed(atmosphere.GustSpeed));
                text.Append("  ");
                text.Append(WeatherReadout.SignedDecimal(atmosphere.GustSpeed - live.WindSpeed, 1));
                text.Append(" M/S");
            }
            else
            {
                text.Append(WeatherReadout.Unknown);
            }
            roseGustText.text = text.ToString();

            text.Length = 0;
            text.Append("VEER   ");
            if (front.Present)
            {
                text.Append(WeatherReadout.Veer(heading, arriving));
                text.Append("  ");
                text.Append(FrontKinds.Label(front.Kind));
            }
            else
            {
                text.Append(WeatherReadout.Unknown);
            }
            roseVeerText.text = text.ToString();

            text.Length = 0;
            text.Append("SHEAR  ");
            if (hasAtmosphere)
            {
                text.Append(WeatherReadout.Decimal(atmosphere.Shear, 2));
                text.Append("  ");
                text.Append(WeatherReadout.ShearLabel(atmosphere.Shear));
            }
            else
            {
                text.Append(WeatherReadout.Unknown);
            }
            roseShearText.text = text.ToString();
        }

        private static void PlaceNeedle(RectTransform needle, float heading)
        {
            if (needle == null) return;
            needle.localRotation = Quaternion.Euler(0f, 0f, -WeatherState.WrapHeading(heading));
        }

        private static void SetActive(GameObject target, bool on)
        {
            if (target != null && target.activeSelf != on) target.SetActive(on);
        }

        private void RefreshFront(Atmosphere atmosphere, WeatherFront front)
        {
            if (front.Present)
            {
                float x = ReaderX();
                float z = ReaderZ();
                bool passed = front.SignedDistanceTo(x, z) >= 0f;

                text.Length = 0;
                text.Append(FrontKinds.Label(front.Kind));
                text.Append("   ");
                text.Append(StormReadout.NauticalMiles(front.DistanceTo(x, z)));
                text.Append(passed ? "   PASSED" : "   AHEAD");
                if (!passed)
                {
                    text.Append("   ETA ");
                    text.Append(WeatherReadout.Clock(front.SecondsUntil(x, z)));
                }
                frontText.text = text.ToString();
                frontText.color = AvTheme.TextPrimary;
                frontGlyph.text = FrontKinds.Glyph(front.Kind);
                frontGlyph.color = AvTheme.RailInfo;
                return;
            }

            frontGlyph.text = "";
            frontText.color = AvTheme.Dim;
            frontText.text = atmosphere.Available
                ? "NO ACTIVE FRONT — " + AirMasses.Name(atmosphere.AirMass)
                : "NO ACTIVE FRONT — UNIFORM AIR MASS";
        }

        private void RefreshForecast(WeatherSnapshot snapshot, WeatherForecast forecast)
        {
            bool hasForecast = forecast != null && forecast.Count > 0;
            forecastLegend.text = hasForecast
                ? "▲ BUILDING   ▼ EASING   = STEADY"
                : "NO FORECAST — THE SCHEDULE HAS NOT SEEDED A MISSION";
            SetActive(forecastKey.gameObject, hasForecast);

            int shown = hasForecast ? Mathf.Min(rowCapacity, forecast.Count) : 0;
            for (int i = 0; i < forecastRows.Count; i++)
            {
                ForecastRow row = forecastRows[i];
                if (i >= shown)
                {
                    row.SetVisible(false);
                    continue;
                }

                WeatherForecastEntry entry = forecast[i];
                int rung = Mathf.Clamp(WeatherReadout.Severity(entry.State.Regime) - 1, 0, RampRungs - 1);

                row.SetVisible(true);
                row.Age.text = WeatherReadout.InSeconds(entry.AtSeconds - snapshot.MissionTime);
                row.ChipText.text = WeatherReadout.Regime(entry.State.Regime);
                row.ChipText.color = regimeInk[rung];
                row.Chip.color = regimeChipFill[rung];
                row.Trend.text = WeatherReadout.TrendMark(entry.ConditionsDelta, WeatherReadout.TrendDeadband) +
                                 " " + WeatherReadout.SignedPercent(entry.ConditionsDelta);
                // The compass point is the wind glyph; the legend names the units once.
                row.Wind.text = WeatherReadout.Compass16(entry.State.WindHeading) + " " +
                                WeatherReadout.Decimal(entry.State.WindSpeed, 1) + " (" +
                                WeatherReadout.SignedDecimal(entry.WindDelta, 1) + ")";
            }
        }

        /// <summary>
        /// The host's one row of control, and the schedule it shows: the mission clock, the next
        /// transition, the front's approach drawn along the forecast horizon, and a tick per
        /// transition, tinted by that transition's severity.
        /// </summary>
        private void RefreshControl(WeatherSnapshot snapshot, WeatherForecast forecast, WeatherFront front)
        {
            if (controlClock == null) return;

            text.Length = 0;
            text.Append("T+");
            text.Append(WeatherReadout.Clock(snapshot.Available ? snapshot.MissionTime : manager.MissionTime));
            controlClock.text = text.ToString();

            bool hasForecast = forecast != null && forecast.Count > 0;
            if (hasForecast)
            {
                text.Length = 0;
                text.Append("NEXT ");
                text.Append(WeatherReadout.Regime(forecast.NextRegime));
                text.Append("  ");
                text.Append(WeatherReadout.InSeconds(forecast.NextChangeSeconds));
                controlNext.text = text.ToString();
            }
            else
            {
                controlNext.text = "AWAITING SCHEDULE";
            }

            bool scheduleOn = !manager.OverrideActive;
            string tooltip = scheduleOn ? ScheduleOnTooltip : ScheduleOffTooltip;
            scheduleButton.SetText(scheduleOn ? "ON" : "OFF");
            scheduleButton.SetLatched(scheduleOn);
            scheduleButton.WithTooltip(tooltip);
            if (scheduleHover != null) scheduleHover.SetText(tooltip);

            float horizon = hasForecast
                ? Mathf.Max(WeatherForecast.MinStepSeconds,
                            forecast[forecast.Count - 1].AtSeconds - snapshot.MissionTime)
                : 0f;

            for (int i = 0; i < trackTicks.Length; i++)
            {
                if (!hasForecast || i >= forecast.Count || horizon <= 0f)
                {
                    SetActive(trackTicks[i].gameObject, false);
                    continue;
                }

                float fraction = Mathf.Clamp01((forecast[i].AtSeconds - snapshot.MissionTime) / horizon);
                trackTicks[i].rectTransform.anchoredPosition = new Vector2(fraction * trackWidth, 0f);
                trackTicks[i].color = regimeRail[
                    Mathf.Clamp(WeatherReadout.Severity(forecast[i].State.Regime) - 1, 0, RampRungs - 1)];
                SetActive(trackTicks[i].gameObject, true);
            }

            trackLeft.text = hasForecast ? "NOW" : "NO FORECAST";
            text.Length = 0;
            text.Append("T+");
            text.Append(WeatherReadout.Clock(horizon));
            trackRight.text = hasForecast ? text.ToString() : WeatherReadout.Unknown;

            // The front fill grows towards the marker as the boundary closes, so the clock and
            // the picture of the approach always agree.
            float fill = 0f;
            bool frontAhead = false;
            if (front.Present && horizon > 0f)
            {
                float x = ReaderX();
                float z = ReaderZ();
                if (front.SignedDistanceTo(x, z) < 0f)
                {
                    frontAhead = true;
                    fill = Mathf.Clamp01(front.SecondsUntil(x, z) / horizon);
                }
            }
            trackFill.rectTransform.sizeDelta = new Vector2(frontAhead ? fill * trackWidth : 0f, TrackHeight);
            SetActive(marker.gameObject, frontAhead);
            if (frontAhead) marker.anchoredPosition = new Vector2(fill * trackWidth, 0f);
        }

        /// <summary>The reader's world position, the same reading the radar page is handed.</summary>
        private static float ReaderX()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            return cameras == null ? 0f : cameras.transform.position.x;
        }

        private static float ReaderZ()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            return cameras == null ? 0f : cameras.transform.position.z;
        }

        // ---- Actions ---------------------------------------------------------------------

        /// <summary>
        /// The panel's one control. Forcing a named regime by hand lives in the debug overlay,
        /// where a host that wants to hand-set the sky already is.
        /// </summary>
        private void ToggleSchedule()
        {
            if (manager == null) return;

            if (manager.OverrideActive)
            {
                manager.ReleaseOverride();
                Echo("SCHEDULE — ON");
            }
            else if (manager.Snapshot.Available)
            {
                manager.ApplyOverride(manager.Snapshot.Live);
                Echo("SCHEDULE — OFF, HOLDING " + WeatherReadout.Regime(manager.Snapshot.Live.Regime));
            }
            else
            {
                Echo("NO READOUT — SCHEDULE UNCHANGED");
            }
            nextRefresh = 0f;
        }

        /// <summary>Confirm the action on the status strip for a moment.</summary>
        private void Echo(string message)
        {
            actionEcho = message;
            actionEchoUntil = Time.unscaledTime + 1.6f;
        }

        // ---- Formatting ------------------------------------------------------------------

        /// <summary>
        /// The one hazard worth a sentence, short enough for the banner's right column. The tier
        /// word beside it already carries the "how bad"; this says what it will do to the reader.
        /// </summary>
        private static string Hazard(WeatherSnapshot snapshot)
        {
            if (snapshot.Live.IsSevere) return "EXPECT LIGHTNING";
            if (snapshot.CloudOcclusion > 0.5f) return "IR SEEKERS DEGRADED";
            if (snapshot.LocalWindSpeed > 15f) return "GUSTY WINDS";
            return "NO WEATHER HAZARD";
        }

        private sealed class ForecastRow
        {
            /// <summary>Column edges inside one row: age, severity chip, trend, wind.</summary>
            private const float AgeWidth = 78f;
            private const float ChipWidth = 94f;
            private const float TrendWidth = 108f;
            private const float ColumnGap = 6f;

            public GameObject Root;
            public TMP_Text Age;
            public Image Chip;
            public TMP_Text ChipText;
            public TMP_Text Trend;
            public TMP_Text Wind;

            public static ForecastRow Build(RectTransform parent, float x, float y, float width, float pitch)
            {
                var row = new ForecastRow();
                var root = new GameObject("ForecastRow", typeof(RectTransform));
                row.Root = root;
                var rect = (RectTransform)root.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, pitch));

                float labelTop = -(pitch - RowHeight) * 0.5f;
                float chipTop = -(pitch - 14f) * 0.5f;

                row.Age = AvStyled.Label(rect, new Rect(0f, labelTop, AgeWidth, RowHeight), "", "kv-key");

                float chipX = AgeWidth + ColumnGap;
                var chipRect = new Rect(chipX, chipTop, ChipWidth, 14f);
                row.Chip = AvStyled.Box(rect, chipRect, "chip live");
                row.ChipText = AvStyled.Label(rect, chipRect, "", "chip live",
                    align: TextAlignmentOptions.Center);

                float trendX = chipX + ChipWidth + ColumnGap;
                row.Trend = AvStyled.Label(rect, new Rect(trendX, labelTop, TrendWidth, RowHeight),
                    "", "kv-value", align: TextAlignmentOptions.MidlineLeft);
                row.Wind = AvStyled.Label(
                    rect, new Rect(trendX + TrendWidth, labelTop, width - trendX - TrendWidth, RowHeight),
                    "", "kv-value");
                return row;
            }

            public void SetVisible(bool on) => SetActive(Root, on);
        }
    }
}
