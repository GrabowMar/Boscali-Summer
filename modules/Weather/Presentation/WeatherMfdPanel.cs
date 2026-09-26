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
    /// "WEA" — the environment panel, one page and five sections: the hazard banner and the
    /// current observation, the single wind rose, the deterministic forecast, the radar picture
    /// with its bounded cell table, and — on the host alone — one row of schedule control. The
    /// radar is a section of this page, not a second tab, so there is one panel to open and one
    /// grid everything is measured against.
    ///
    /// <para>Every displayed fact has one source. The world reads from <c>Live</c>, the synced
    /// vanilla channels and the storm buffer; the physics the schedule alone can see — flight
    /// category, visibility, temperature, dewpoint — reads from <c>Atmosphere</c> and is marked
    /// <c>· SCHED</c> whenever a held sky makes the schedule an estimate rather than an
    /// observation. A forced sky therefore reads self-consistent from the banner to the forecast.</para>
    ///
    /// <para>Every block is measured and arranged once, at build, to fit the bay the panel was
    /// placed in. A bay too short for even one forecast row drops that whole section and gives
    /// its room to the radar and the wind rose; only a bay too short even for those scrolls,
    /// rather than clipping the page. Nothing moves at refresh: a refresh writes text, tints
    /// and needle rotations only.</para>
    /// </summary>
    internal sealed class WeatherMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        private const int ChipCount = 2;

        // ---- Layout grid -------------------------------------------------------------------
        //
        // One grid for the whole page: every section hangs off the same left column, every
        // reading row is one 30px row (AvTokens.RowHeight), and every label/value pair is the
        // same two classes. The two data tables are the deliberate exception — they share one
        // denser grid (WeatherRadarPage.TableInset / TableColumnEdges / RowPitch) so a column
        // edge lands on the same x in the forecast and in the echo table.
        private const float HeaderHeight = 20f;
        private const float BannerHeight = 38f;

        /// <summary>One metric cell: the key line over the reading line, exactly the row token.</summary>
        private const float MetricRowHeight = AvTokens.RowHeight;
        private const float MetricKeyHeight = 13f;
        private const float MetricGap = 6f;

        /// <summary>One wind reading: label and value on one 30px row, two rows beside the rose.</summary>
        private const float WindLabelWidth = 44f;
        private const int WindColumns = 2;
        private const int WindRows = 2;

        private const float LegendHeight = 14f;
        private const float ControlHeight = AvTokens.RowHeight;
        private const float TrackGap = 8f;
        private const float TrackHeight = 6f;
        private const float TrackLabelHeight = 12f;
        private const float RoseBase = 60f;
        private const float RoseMax = 130f;
        private const float MapBase = 72f;
        private const float MapMax = 220f;
        private const float MapShare = 0.40f;
        private const float SectionGap = AvTokens.Space2;
        private const float SectionGapMax = 16f;
        private const float ToggleWidth = 54f;
        private const float MarkerHeight = 10f;
        private const float BarWidth = 3f;
        private const float BarGap = 2f;
        private const float BarHeight = 12f;
        private const float PipSize = 7f;
        private const float PipGap = 3f;
        private const float PrecipBarWidth = 2f;
        private const float PrecipBarHeight = 11f;

        private const int BarCount = 4;
        private const int RampRungs = 4;
        private const int PipCount = 4;
        private const int PrecipBarCount = 3;
        private const int ObsColumns = 3;
        private const int ObsRows = 2;

        private const int ObsCategory = 0;
        private const int ObsCloudBase = 1;
        private const int ObsVisibility = 2;
        private const int ObsTemperature = 3;
        private const int ObsPrecipitation = 4;
        private const int ObsOcclusion = 5;
        private const int ObsCount = 6;

        /// <summary>The metric row's fixed keys, so all six cells share one label baseline.</summary>
        private static readonly string[] ObsKeys =
        {
            "FLIGHT CATEGORY", "CEILING", "VISIBILITY", "TEMP / DEW", "PRECIPITATION", "DECK OCCLUSION"
        };

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

        private TMP_Text bannerTier;
        private TMP_Text bannerDetail;
        private TMP_Text bannerHazard;
        private Image bannerRail;
        private readonly Image[] bannerBar = new Image[BarCount];

        private readonly TMP_Text[] obsValue = new TMP_Text[ObsCount];
        private readonly TMP_Text[] obsKey = new TMP_Text[ObsCount];
        private readonly TMP_Text[] obsNote = new TMP_Text[ObsCount];
        private readonly Image[] obsPip = new Image[PipCount];
        private readonly Image[] obsPrecip = new Image[PrecipBarCount];
        private TMP_Text conditionsNote;

        private RectTransform roseMean;
        private RectTransform roseMeanTail;
        private RectTransform roseGust;
        private RectTransform roseVeer;
        private RectTransform roseVeerRim;
        private TMP_Text roseMeanText;
        private TMP_Text roseGustText;
        private TMP_Text roseVeerText;
        private TMP_Text roseShearText;
        private TMP_Text roseShearNote;

        private TMP_Text forecastNote;
        private TMP_Text forecastEmpty;
        private TMP_Text radarNote;

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

            bannerTier = null;
            bannerDetail = null;
            bannerHazard = null;
            bannerRail = null;
            Array.Clear(bannerBar, 0, bannerBar.Length);
            Array.Clear(obsValue, 0, obsValue.Length);
            Array.Clear(obsKey, 0, obsKey.Length);
            Array.Clear(obsNote, 0, obsNote.Length);
            Array.Clear(obsPip, 0, obsPip.Length);
            Array.Clear(obsPrecip, 0, obsPrecip.Length);
            conditionsNote = null;

            roseMean = null;
            roseMeanTail = null;
            roseGust = null;
            roseVeer = null;
            roseVeerRim = null;
            roseMeanText = null;
            roseGustText = null;
            roseVeerText = null;
            roseShearText = null;
            roseShearNote = null;

            forecastNote = null;
            forecastEmpty = null;
            radarNote = null;

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
            if (!settings.Enabled.Value)
            {
                // Releasing instead of latching: re-enabling during the same mission rebuilds
                // the panel, exactly as the Events screen behaves.
                if (screen != null) ResetForScene();
                return;
            }
            if (!GameAccess.MfdAvailable) { failed = true; return; }

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

            // One page, so no tab bar: the radar lives inside the environment page.
            shell = AvScreen.Build(
                content, MfdSlots.Weather,
                Array.Empty<string>(),
                null,
                ChipCount, Width, height, _ => nextRefresh = 0f);
            GapDataBar(shell);

            ResolvePalette();

            radar = new WeatherRadarPage(settings);
            BuildPage(shell.CreatePage(0, "EnvironmentPage"));

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

            shell.SetPage(0);
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
        /// The shared data bar sizes its id tag with a character estimate and starts the state
        /// line exactly at that estimate, so a tracked tag overruns its own box and the state
        /// line runs under it. Measure the tag the bar actually built — the label sharing the
        /// bar's row and sitting left of the state line — and start the state line clear of it.
        /// </summary>
        private static void GapDataBar(AvScreen screen)
        {
            if (screen == null || screen.Content == null || screen.DataBar == null || screen.DataBar.State == null)
                return;

            var state = (RectTransform)screen.DataBar.State.transform;
            Transform row = state.parent;
            if (row == null) return;

            TMP_Text tag = null;
            for (int i = 0; i < row.childCount; i++)
            {
                TMP_Text label = row.GetChild(i).GetComponent<TMP_Text>();
                if (label == null || ReferenceEquals(label, screen.DataBar.State)) continue;
                var rect = label.rectTransform;
                if (rect.anchoredPosition.x >= state.anchoredPosition.x) continue;
                if (Mathf.Abs(rect.anchoredPosition.y - state.anchoredPosition.y) > 1f) continue;
                tag = label;
                break;
            }
            if (tag == null) return;

            float shift = tag.rectTransform.anchoredPosition.x + tag.preferredWidth + 8f - state.anchoredPosition.x;
            if (shift <= 0f) return;
            state.anchoredPosition = new Vector2(state.anchoredPosition.x + shift, state.anchoredPosition.y);
            state.sizeDelta = new Vector2(Mathf.Max(0f, state.sizeDelta.x - shift), state.sizeDelta.y);
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
                AvStyle chip = AvStyleHost.Style("chip " + StormReadout.ChipClass(warning));
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

        /// <summary>
        /// The one page, laid out on the panel's grid from the top down. Fixed furniture is
        /// measured first, then the wind rose, the radar map and the forecast row count share
        /// what is left; a short bay drops forecast rows before it touches the radar.
        /// </summary>
        private void BuildPage(GameObject page)
        {
            Rect body = shell.Body;
            var pageRect = (RectTransform)page.transform;

            bool host = manager != null && manager.HostAuthority;
            int configured = settings != null
                ? Mathf.Clamp(settings.ForecastSteps.Value, 1, WeatherForecast.MaxEntries)
                : WeatherForecast.DefaultSteps;
            float controlBlock = host
                ? HeaderHeight + ControlHeight + TrackGap + TrackHeight + TrackLabelHeight
                : 0f;
            float metricBlock = MetricRowHeight * ObsRows + AvTokens.Space1 * (ObsRows - 1);
            int gapCount = host ? 4 : 3;
            float forecastBlock = HeaderHeight + LegendHeight;

            float fixedHeight = HeaderHeight + BannerHeight + AvTokens.Space1 + metricBlock
                              + HeaderHeight
                              + forecastBlock
                              + HeaderHeight + WeatherRadarPage.ChromeHeight
                              + controlBlock;

            float pool = body.height - fixedHeight - gapCount * SectionGap;
            float mapHeight = Mathf.Clamp(pool * MapShare, MapBase, MapMax);
            rowCapacity = Mathf.Clamp(
                Mathf.FloorToInt((pool - mapHeight - RoseBase) / WeatherRadarPage.RowPitch), 0, configured);

            // Not one forecast row fits beside everything else. A header over an empty block
            // reads as a broken panel, so the section is not built at all and the radar and the
            // wind rose take its room: the page drops forecast rows before it touches the map.
            bool showForecast = rowCapacity > 0;
            if (!showForecast)
            {
                gapCount -= 1;
                fixedHeight -= forecastBlock;
                pool = body.height - fixedHeight - gapCount * SectionGap;
                mapHeight = Mathf.Clamp(pool * MapShare, MapBase, MapMax);
            }

            float roseSize = Mathf.Clamp(pool - mapHeight - rowCapacity * WeatherRadarPage.RowPitch, RoseBase, RoseMax);
            float residual = pool - mapHeight - rowCapacity * WeatherRadarPage.RowPitch - roseSize;
            float gap = SectionGap;

            // A bay taller than the sections need: the picture takes the slack first, then the
            // rose, then the gaps evenly. The map takes the last of it, so the page ends on the
            // glass instead of leaving a strip of nothing under the radar.
            float grow = Mathf.Min(residual, MapMax - mapHeight);
            mapHeight += grow;
            residual -= grow;
            grow = Mathf.Min(residual, RoseMax - roseSize);
            roseSize += grow;
            residual -= grow;
            if (residual > 0f)
            {
                grow = Mathf.Min(residual / gapCount, SectionGapMax - SectionGap);
                gap += grow;
                residual -= grow * gapCount;
                if (residual > 0f) mapHeight += residual;
            }

            float contentHeight = fixedHeight + gapCount * gap + mapHeight
                                + rowCapacity * WeatherRadarPage.RowPitch + roseSize;

            RectTransform parent = AvScreen.Scroll(pageRect, body, contentHeight, out Rect area);
            float x = area.x + AvScreen.SpineInset;
            float width = area.width - AvScreen.SpineInset;
            AvStyled.Spine(parent, new Rect(area.x, area.y, 3f, contentHeight));

            float y = area.y;
            conditionsNote = SectionNote(parent, x, y, width, "AT YOUR POSITION");
            y = SectionHeader(parent, x, y, width, "CONDITIONS", null, band: false);
            BuildBanner(parent, x, y, width);
            y -= BannerHeight + AvTokens.Space1;

            float column = (width - MetricGap * (ObsColumns - 1)) / ObsColumns;
            for (int i = 0; i < ObsCount; i++)
            {
                float cx = x + i % ObsColumns * (column + MetricGap);
                float cy = y - i / ObsColumns * (MetricRowHeight + AvTokens.Space1);
                BuildObsCell(parent, cx, cy, column, i);
            }
            y -= metricBlock + gap;

            // The header note says the one thing the cells do not; the four readings name
            // themselves, so the old "MEAN · GUST · VEER · SHEAR" note was every label twice.
            y = SectionHeader(parent, x, y, width, "WIND", "WIND FROM · AT YOUR POSITION", band: false);
            BuildRose(parent, x, y, width, roseSize);
            y -= roseSize + gap;

            if (showForecast)
            {
                forecastNote = SectionNote(parent, x, y, width, "IDENTICAL ON EVERY PEER");
                y = SectionHeader(parent, x, y, width, "FORECAST", null, band: false);
                BuildForecastLegend(parent, x, y, width);
                y -= LegendHeight;

                forecastRows.Clear();
                for (int i = 0; i < rowCapacity; i++)
                {
                    forecastRows.Add(ForecastRow.Build(parent, x, y - i * WeatherRadarPage.RowPitch, width));
                }

                // When no row can be shown, the reserved row block would read as a hole in the
                // page. One line sitting on the middle of it says why, which is the difference
                // between "no forecast" and "the panel is broken".
                forecastEmpty = AvStyled.Label(
                    parent, new Rect(x, y, width, rowCapacity * WeatherRadarPage.RowPitch),
                    "", "section-title-note");
                SetActive(forecastEmpty.gameObject, false);
                y -= rowCapacity * WeatherRadarPage.RowPitch + gap;
            }

            float radarHeaderY = y;
            y = SectionHeader(parent, x, y, width, "RADAR", null, band: true);
            radarNote = SectionNote(parent, x, radarHeaderY, width, "");
            y = radar.Build(parent, radarNote, x, y, width, mapHeight);
            y -= gap;

            if (host) BuildControl(parent, x, y, width);
        }

        private static TMP_Text SectionNote(RectTransform parent, float x, float y, float width, string text)
        {
            float titleWidth = width * 0.5f;
            return AvStyled.Label(parent, new Rect(x + titleWidth, y, width - titleWidth, 14f), text,
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
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
        /// word and its bars, then the cell and the hazard in words. A held sky shows the held
        /// regime, not the storm cells, because the cells are gated by the schedule's air mass
        /// and would contradict the forced sky.
        /// </summary>
        private void BuildBanner(RectTransform parent, float x, float y, float width)
        {
            AvStyled.Box(parent, new Rect(x, y, width, BannerHeight), "section band");
            bannerRail = AvKit.Rule(parent, new Rect(x, y, 3f, BannerHeight), AvTheme.RailInert);
            bannerTier = AvStyled.Label(parent, new Rect(x + 12f, y, width - 12f, 22f), "", "page-title");

            float barX = x + width - (BarCount * BarWidth + (BarCount - 1) * BarGap) - 2f;
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

        /// <summary>
        /// One observation cell, exactly one 30px row: the key on the first line and the reading
        /// on the second, with the unit or the secondary reading right-aligned on that same
        /// reading line. All six cells therefore share every baseline, and a reading with two
        /// units keeps the primary in the value and the secondary in the note — "4587" with
        /// "FT · 1398 M AGL" — instead of a value that has to be cut to fit.
        /// </summary>
        private void BuildObsCell(RectTransform parent, float x, float y, float width, int index)
        {
            const float pad = 10f;
            const float valueTop = MetricKeyHeight;
            const float valueHeight = MetricRowHeight - MetricKeyHeight;

            AvStyled.Box(parent, new Rect(x, y, width, MetricRowHeight), "metric");
            obsKey[index] = AvStyled.Label(
                parent, new Rect(x + pad, y - 1f, width - pad * 2f, MetricKeyHeight), ObsKeys[index], "kv-key");

            TMP_Text value = AvStyled.Label(
                parent, new Rect(x + pad, y - valueTop, width - pad * 2f, valueHeight), "", "row-name");
            // A readout is not a label: the sheet's tracked capitals pushed "35°C / 24°C" past
            // its cell once the game's own (wider) MFD font was on it. Untracked, shrinking only
            // if it must, and never ellipsised.
            value.fontSizeMin = AvTokens.FontMicro;
            value.fontSizeMax = AvTokens.FontBody;
            value.enableAutoSizing = true;
            value.characterSpacing = 0f;
            obsValue[index] = value;

            obsNote[index] = AvStyled.Label(
                parent, new Rect(x + pad, y - valueTop, width - pad * 2f, valueHeight), "",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);

            if (index == ObsCategory)
            {
                float pipX = x + width - pad - (PipCount * PipSize + (PipCount - 1) * PipGap);
                for (int i = 0; i < PipCount; i++)
                {
                    obsPip[i] = AvKit.Panel(parent,
                        new Rect(pipX + i * (PipSize + PipGap), y - valueTop - (valueHeight - PipSize) * 0.5f,
                            PipSize, PipSize),
                        AvTheme.RailInert, AvSprites.Led);
                }
            }
            else if (index == ObsPrecipitation)
            {
                float iconBlock = PrecipBarCount * PrecipBarWidth + (PrecipBarCount - 1) * 4f;
                float barX = x + width - pad - iconBlock - 2f;
                for (int i = 0; i < PrecipBarCount; i++)
                {
                    obsPrecip[i] = AvKit.Rule(parent,
                        new Rect(barX + i * (PrecipBarWidth + 4f),
                            y - valueTop - (valueHeight - PrecipBarHeight) * 0.5f,
                            PrecipBarWidth, PrecipBarHeight),
                        AvTheme.RailInfo);
                    obsPrecip[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -20f);
                }
            }
        }

        /// <summary>
        /// The wind rose. A compass ring with cardinal letters, the gust needle longest, the mean
        /// needle over a tail so it reads as a vector, and the veered heading marked on the rim —
        /// all three rotate from the hub at refresh.
        /// </summary>
        private void BuildRose(RectTransform parent, float x, float y, float width, float size)
        {
            var roseObject = new GameObject("WindRose", typeof(RectTransform), typeof(Image));
            var rose = roseObject.GetComponent<RectTransform>();
            rose.SetParent(parent, false);
            AvKit.Place(rose, new Rect(x, y, size, size));

            Image ground = roseObject.GetComponent<Image>();
            ground.sprite = AvSprites.Card;
            ground.type = Image.Type.Sliced;
            ground.color = AvTheme.Unity(AvTokens.SurfaceInert);
            ground.raycastTarget = false;

            AvKit.Outline(rose, new Rect(0f, 0f, size, size), AvTheme.Hairline);
            float centre = size * 0.5f;
            Color hairline = AvTheme.Unity(AvTokens.Hairline.WithAlpha(0.5f));
            AvKit.Rule(rose, new Rect(centre - 0.5f, 0f, 1f, size), hairline);
            AvKit.Rule(rose, new Rect(0f, -centre, size, 1f), hairline);

            AvStyled.Label(rose, new Rect(centre - 10f, 1f, 20f, 12f), "N", "section-title-note",
                align: TextAlignmentOptions.Center);
            AvStyled.Label(rose, new Rect(size - 12f, -centre + 6f, 11f, 12f), "E", "section-title-note",
                align: TextAlignmentOptions.Center);
            AvStyled.Label(rose, new Rect(centre - 10f, -size + 12f, 20f, 12f), "S", "section-title-note",
                align: TextAlignmentOptions.Center);
            AvStyled.Label(rose, new Rect(1f, -centre + 6f, 11f, 12f), "W", "section-title-note",
                align: TextAlignmentOptions.Center);

            // The rim tick rides a pivot at the hub, so one rotation marks the arriving heading.
            roseVeerRim = Pivot(rose);
            AvKit.Rule(roseVeerRim, new Rect(centre - 1f, size - 9f, 2f, 8f), AvTheme.RailInfo);
            roseVeerRim.gameObject.SetActive(false);

            roseVeer = Needle(rose, size * 0.30f, 1.5f, AvTheme.RailInfo);
            roseGust = Needle(rose, size * 0.78f, 2f, AvTheme.RailCaution);
            roseMeanTail = Needle(rose, size * 0.18f, 2f, AvTheme.Accent);
            roseMean = Needle(rose, size * 0.56f, 2.5f, AvTheme.Accent);
            AvKit.Panel(rose, new Rect(centre - 2f, -centre + 2f, 4f, 4f), AvTheme.Accent);

            // The readings sit beside the rose as two rows of two kv pairs, on the same 30px
            // rhythm as every other reading row: one shared label column and one shared value
            // column, and the block is centred on the rose so the pair cannot overhang it.
            float numericX = x + size + AvTokens.Space3;
            float numericWidth = width - size - AvTokens.Space3;
            float column = (numericWidth - AvTokens.Space2 * (WindColumns - 1)) / WindColumns;
            float gridTop = y - (size - WindRows * MetricRowHeight) * 0.5f;
            roseMeanText = WindCell(parent, numericX, gridTop, column, 0, 0, "MEAN", 0f, out _);
            roseGustText = WindCell(parent, numericX, gridTop, column, 1, 0, "GUST", 0f, out _);
            roseVeerText = WindCell(parent, numericX, gridTop, column, 0, 1, "VEER", 0f, out _);
            roseShearText = WindCell(parent, numericX, gridTop, column, 1, 1, "SHEAR", 56f, out roseShearNote);
        }

        /// <summary>
        /// One wind reading: its name in the shared label column and its value in the shared
        /// value column. The value is returned so the refresh can write it without touching
        /// the label.
        /// </summary>
        private static TMP_Text WindCell(
            RectTransform parent, float x, float y, float width, int column, int row, string label,
            float qualifierWidth, out TMP_Text qualifier)
        {
            float cx = x + column * (width + AvTokens.Space2);
            float cy = y - row * MetricRowHeight;
            AvStyled.Label(parent, new Rect(cx, cy, WindLabelWidth, MetricRowHeight), label, "kv-key");
            // A qualifier word gets its own right-aligned note, so the value never shares a box
            // with it and "0.85  MODERATE" cannot ellipsise at the rose's largest size.
            qualifier = qualifierWidth > 0f
                ? AvStyled.Label(parent,
                    new Rect(cx + width - qualifierWidth, cy, qualifierWidth, MetricRowHeight), "",
                    "section-title-note", align: TextAlignmentOptions.MidlineRight)
                : null;
            float valueWidth = width - WindLabelWidth -
                (qualifierWidth > 0f ? qualifierWidth + AvTokens.Space1 : 0f);
            return AvStyled.Label(parent,
                new Rect(cx + WindLabelWidth, cy, valueWidth, MetricRowHeight), "", "kv-value");
        }

        private static RectTransform Pivot(RectTransform parent)
        {
            var go = new GameObject("Pivot", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
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

        /// <summary>
        /// The forecast's own column keys, on the echo table's grid so a column edge lands on
        /// the same x down the page. The unit appears here once; the rows never repeat it.
        /// </summary>
        private void BuildForecastLegend(RectTransform parent, float x, float y, float width)
        {
            float[] edges = WeatherRadarPage.TableColumnEdges;
            float inset = x + WeatherRadarPage.TableInset;
            float c0 = inset + edges[0];
            float c1 = inset + edges[1];
            float c2 = inset + edges[2];
            float c3 = inset + edges[3];

            AvStyled.Label(parent, new Rect(c0, y, c1 - c0, LegendHeight), "AGE", "section-title-note");
            AvStyled.Label(parent, new Rect(c1, y, c2 - c1, LegendHeight), "SKY", "section-title-note");
            AvStyled.Label(parent, new Rect(c2, y, c3 - c2, LegendHeight), "TREND %", "section-title-note",
                align: TextAlignmentOptions.MidlineRight);
            AvStyled.Label(parent, new Rect(c3, y, x + width - c3, LegendHeight), "WIND · DIR SPEED (M/S)",
                "section-title-note", align: TextAlignmentOptions.MidlineRight);
        }

        private void BuildControl(RectTransform parent, float x, float y, float width)
        {
            y = SectionHeader(parent, x, y, width, "CONTROL", "HOST ONLY", band: false);

            scheduleHover = RowHover(parent, new Rect(x, y, width, ControlHeight), ScheduleOnTooltip);
            AvStyled.Label(parent, new Rect(x, y, 78f, ControlHeight), "SCHEDULE", "kv-key");
            controlClock = AvStyled.Label(parent, new Rect(x + 78f, y, 72f, ControlHeight), "", "kv-value",
                align: TextAlignmentOptions.MidlineLeft);
            controlNext = AvStyled.Label(
                parent, new Rect(x + 150f, y, width - 150f - ToggleWidth - 6f, ControlHeight),
                "", "kv-value", align: TextAlignmentOptions.MidlineRight);
            scheduleButton = AvStyled.Button(
                parent, new Rect(x + width - ToggleWidth, y, ToggleWidth, ControlHeight),
                "ON", "btn", ToggleSchedule);
            scheduleButton.WithTooltip(ScheduleOnTooltip);
            y -= ControlHeight + TrackGap;

            var trackObject = new GameObject("ScheduleTrack", typeof(RectTransform), typeof(Image));
            var track = trackObject.GetComponent<RectTransform>();
            track.SetParent(parent, false);
            AvKit.Place(track, new Rect(x, y, width, TrackHeight));
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
                trackTicks[i] = AvKit.Rule(track, new Rect(0f, 0f, 1f, 10f), AvTheme.RailInert);
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

            WeatherSnapshot snapshot = WeatherView.Capture(manager, 0);
            WeatherForecast forecast = WeatherView.Forecast(manager);
            bool available = snapshot.Available;
            WeatherState live = snapshot.Live;
            Atmosphere atmosphere = snapshot.Atmosphere;
            WeatherFront front = snapshot.Front;
            StormWarning warning = snapshot.Warning;
            bool forced = WeatherView.OverrideActive(manager);

            // Exactly two status chips: who owns the sky, and what is steering it.
            bool host = manager.HostAuthority;
            shell.DataBar.SetChip(0, host ? "HOST" : "CLIENT", host ? "live" : "inert");
            shell.DataBar.SetChip(1,
                forced ? "OVERRIDE" : snapshot.Overridden ? "SETTLING" : available ? "SCHEDULE" : "INERT",
                forced ? "warn" : snapshot.Overridden ? "info" : available ? "live" : "inert");

            // The state line names the same sky the banner and the observation read, so the
            // header can never call a storm clear.
            text.Length = 0;
            if (forced)
            {
                text.Append("OVERRIDE — ");
                text.Append(WeatherReadout.Regime(live.Regime));
            }
            else if (available)
            {
                text.Append(WeatherReadout.Regime(live.Regime));
                text.Append(" — ");
                text.Append(WarningWord(warning));
            }
            else
            {
                text.Append("NO READOUT");
            }
            shell.DataBar.State.text = text.ToString();

            RefreshBanner(snapshot, atmosphere, warning, live, forced);
            RefreshObservation(snapshot, atmosphere, live, available, forced);
            RefreshRose(snapshot, atmosphere, front, live);
            RefreshForecast(snapshot, forecast, forced);
            RefreshControl(snapshot, forecast, front, live, forced);
            RefreshRadar(snapshot);

            // The hazard already owns the top of the page, so the strip stays quiet: a host
            // action or an armed map gesture, an override notice, else the mission clock.
            string echo = Time.unscaledTime < actionEchoUntil ? actionEcho : null;
            text.Length = 0;
            text.Append("MISSION T+");
            text.Append(WeatherReadout.Clock(available ? snapshot.MissionTime : manager.MissionTime));
            shell.WriteStatus(
                available ? null : "NO READOUT — NO WEATHER SCHEDULE ON THIS MISSION",
                echo ?? MapPicker.Prompt,
                forced ? "OVERRIDE — SCHEDULE RESUMES WHEN RELEASED" : text.ToString());
        }

        private static string WarningWord(StormWarning warning) =>
            warning == StormWarning.None ? "NO WARNING" : StormReadout.Warning(warning);

        /// <summary>
        /// A reading with its unit removed, for a cell whose note already carries that unit:
        /// "4587 FT" becomes "4587". An unknown or unexpected shape is handed back untouched.
        /// </summary>
        private static string Bare(string reading, string unit)
        {
            if (string.IsNullOrEmpty(reading) || !reading.EndsWith(unit, StringComparison.Ordinal)) return reading;
            return reading.Substring(0, reading.Length - unit.Length);
        }

        private void RefreshBanner(
            WeatherSnapshot snapshot, Atmosphere atmosphere, StormWarning warning, WeatherState live, bool forced)
        {
            if (forced)
            {
                int rung = Mathf.Clamp(WeatherReadout.Severity(live.Regime) - 1, 0, RampRungs - 1);
                int bars = WeatherReadout.Severity(live.Regime);
                bannerTier.text = WeatherReadout.Regime(live.Regime);
                bannerTier.color = regimeInk[rung];
                bannerRail.color = regimeRail[rung];
                for (int i = 0; i < BarCount; i++)
                {
                    bannerBar[i].color = i < bars ? regimeRail[rung] : AvTheme.RailInert;
                }
            }
            else
            {
                int tier = Mathf.Clamp((int)warning, 0, RampRungs - 1);
                // "NO WARNING" beside a hazard line that reads EXPECT LIGHTNING is the banner
                // arguing with itself. No cell in range is what that state is, and it is the
                // same phrase the cockpit HUD uses.
                bannerTier.text = !snapshot.Available ? "NO READOUT"
                    : warning == StormWarning.None ? "NO STORM IN RANGE"
                    : WarningWord(warning);
                bannerTier.color = tierInk[tier];
                bannerRail.color = tierFill[tier];
                for (int i = 0; i < BarCount; i++)
                {
                    bannerBar[i].color = i < tier ? tierFill[tier] : AvTheme.RailInert;
                }
            }

            text.Length = 0;
            if (forced)
            {
                text.Append("SKY HELD BY OVERRIDE");
            }
            else if (warning != StormWarning.None && snapshot.Available)
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
            else if (atmosphere.Available && !snapshot.Overridden)
            {
                text.Append(atmosphere.IsSevereConvection ? "CONVECTIVE" : "STABLE AIR");
                text.Append("   ·   ");
                text.Append(WeatherReadout.Regime(live.Regime));
            }
            else if (snapshot.Available)
            {
                text.Append("WORLD SETTLING — ");
                text.Append(WeatherReadout.Regime(live.Regime));
            }
            bannerDetail.text = text.ToString();
            bannerHazard.text = Hazard(snapshot);
        }

        /// <summary>
        /// The current conditions. World readings — cloud base, rain, deck occlusion, the live
        /// regime — are always shown. The schedule's physics is an observation only while the
        /// schedule owns the sky; under an override it stays visible but marked
        /// <c>· SCHED</c>, so a held sky never reads as something it is not.
        /// </summary>
        private void RefreshObservation(
            WeatherSnapshot snapshot, Atmosphere atmosphere, WeatherState live, bool available, bool forced)
        {
            bool hasAtmosphere = available && atmosphere.Available;

            // The flight category, or the live regime when the schedule no longer owns the sky.
            // The category's own pips ride the reading line, so the word and the ramp agree.
            string skyNote = "";
            if (hasAtmosphere && !forced)
            {
                int rung = Mathf.Clamp(WeatherReadout.FlightSeverity(atmosphere.Category), 0, RampRungs - 1);
                obsValue[ObsCategory].text = Atmospheres.Label(atmosphere.Category);
                obsValue[ObsCategory].color = flightInk[rung];
                for (int i = 0; i < PipCount; i++)
                {
                    obsPip[i].color = i <= rung ? flightFill[rung] : AvTheme.RailInert;
                }
            }
            else if (available)
            {
                int rung = Mathf.Clamp(WeatherReadout.Severity(live.Regime) - 1, 0, RampRungs - 1);
                int bars = WeatherReadout.Severity(live.Regime);
                obsValue[ObsCategory].text = WeatherReadout.Regime(live.Regime);
                obsValue[ObsCategory].color = regimeInk[rung];
                skyNote = hasAtmosphere ? "OVERRIDE" : "";
                for (int i = 0; i < PipCount; i++)
                {
                    obsPip[i].color = i < bars ? regimeRail[rung] : AvTheme.RailInert;
                }
            }
            else
            {
                obsValue[ObsCategory].text = WeatherReadout.Unknown;
                obsValue[ObsCategory].color = AvTheme.Disabled;
                for (int i = 0; i < PipCount; i++) obsPip[i].color = AvTheme.RailInert;
            }
            obsNote[ObsCategory].text = skyNote;

            // Ceiling: the altimeter's feet are the primary reading and the metric height is the
            // caption, so one cell carries both units without either being cut.
            float baseMetres = live.CloudBase;
            if (float.IsNaN(baseMetres) && hasAtmosphere) baseMetres = atmosphere.Lcl;
            bool hasBase = available && !float.IsNaN(baseMetres);
            string feet = hasBase ? WeatherReadout.Feet(baseMetres) : WeatherReadout.Unknown;
            obsValue[ObsCloudBase].text = Bare(feet, " FT");
            obsNote[ObsCloudBase].text = hasBase ? "FT · " + WeatherReadout.MetersAgL(baseMetres) : "";

            obsValue[ObsVisibility].text = hasAtmosphere
                ? Bare(WeatherReadout.Kilometres(atmosphere.Visibility), " KM") : WeatherReadout.Unknown;
            obsNote[ObsVisibility].text = hasAtmosphere ? (forced ? "KM · SCHED" : "KM") : "";

            text.Length = 0;
            if (hasAtmosphere)
            {
                text.Append(WeatherReadout.Decimal(atmosphere.TemperatureC, 0));
                text.Append(" / ");
                text.Append(WeatherReadout.Decimal(atmosphere.DewpointC, 0));
            }
            else
            {
                text.Append(WeatherReadout.Unknown);
            }
            obsValue[ObsTemperature].text = text.ToString();
            obsNote[ObsTemperature].text = hasAtmosphere ? (forced ? "°C · SCHED" : "°C") : "";

            PrecipitationKind precipitation = PrecipitationAt(snapshot, atmosphere, hasAtmosphere, forced);
            obsValue[ObsPrecipitation].text = Atmospheres.Label(precipitation);
            obsNote[ObsPrecipitation].text = "";

            obsValue[ObsOcclusion].text = available
                ? WeatherReadout.Percent01(snapshot.CloudOcclusion) : WeatherReadout.Unknown;
            obsNote[ObsOcclusion].text = available ? OcclusionKey(snapshot.CloudOcclusion, true) : "";

            int drops = Mathf.Clamp((int)precipitation, 0, PrecipBarCount);
            Color dropInk = precipitation == PrecipitationKind.Hail ? AvTheme.RailCaution : AvTheme.RailInfo;
            for (int i = 0; i < PrecipBarCount; i++)
            {
                obsPrecip[i].color = dropInk;
                SetActive(obsPrecip[i].gameObject, i < drops);
            }
            if (conditionsNote != null)
            {
                conditionsNote.text = forced ? "OVERRIDE — SCHED ITEMS MARKED" : "AT YOUR POSITION";
            }
        }

        /// <summary>
        /// What is falling at the reader. The atmosphere's own kind while the schedule owns the
        /// sky; otherwise the live rain the cells and the front are actually producing, which is
        /// the same number the canopy rain reads.
        /// </summary>
        private static PrecipitationKind PrecipitationAt(
            WeatherSnapshot snapshot, Atmosphere atmosphere, bool hasAtmosphere, bool forced)
        {
            if (hasAtmosphere && !forced) return atmosphere.Precipitation;
            if (snapshot.RainIntensity <= PrecipitationVisible) return PrecipitationKind.None;
            return snapshot.RainIntensity >= 0.25f ? PrecipitationKind.Showers : PrecipitationKind.Drizzle;
        }

        /// <summary>
        /// The deck reading is the reader's own occlusion from <c>LevelInfo.GetCloudOcclusion</c>:
        /// zero below half cloud cover, all of it under the deck. Naming the side of the deck
        /// keeps a full reading beside a high cloud base physically coherent.
        /// </summary>
        private static string OcclusionKey(float occlusion, bool available)
        {
            if (!available || float.IsNaN(occlusion) || occlusion >= 0.5f) return "DECK ABOVE";
            return occlusion >= 0.05f ? "PART DECK" : "CLEAR ABOVE";
        }

        /// <summary>
        /// One rose for every wind reading: the synced mean with its tail, the gust, the veer the
        /// arriving air mass carries and the shear. Mean and gust share a heading, so the gust
        /// needle simply reaches further; the veer is marked on the rim at its own heading.
        /// </summary>
        private void RefreshRose(WeatherSnapshot snapshot, Atmosphere atmosphere, WeatherFront front, WeatherState live)
        {
            bool hasAtmosphere = snapshot.Available && atmosphere.Available;
            float heading = live.WindHeading;

            // The dial follows the pilot's convention: the needle tip points at the bearing the
            // wind comes FROM, and the tail lies along the direction it is travelling.
            PlaceNeedle(roseMean, WeatherReadout.WindFrom(heading));
            PlaceNeedle(roseMeanTail, heading);
            PlaceNeedle(roseGust, WeatherReadout.WindFrom(heading));
            SetActive(roseMean.gameObject, snapshot.Available);
            SetActive(roseMeanTail.gameObject, snapshot.Available);
            SetActive(roseGust.gameObject, hasAtmosphere && atmosphere.GustSpeed > live.WindSpeed + 0.5f);

            // BehindHeading is the wind the boundary leaves in its wake, which is the one the
            // reader ends up in once it has passed.
            float arriving = front.Present ? front.BehindHeading : heading;
            PlaceNeedle(roseVeer, WeatherReadout.WindFrom(arriving));
            SetActive(roseVeer.gameObject, front.Present);
            if (roseVeerRim != null)
            {
                roseVeerRim.localRotation = Quaternion.Euler(0f, 0f, -WeatherState.WrapHeading(WeatherReadout.WindFrom(arriving)));
                SetActive(roseVeerRim.gameObject, front.Present);
            }

            // The label is a fixed column beside the rose, so the value is only the reading;
            // a name printed again in the value would be every label twice.
            roseMeanText.text = snapshot.Available
                ? WeatherReadout.Wind(live.WindSpeed, live.WindHeading)
                : WeatherReadout.Unknown;

            text.Length = 0;
            if (hasAtmosphere)
            {
                // The unit is the mean's, one row over; the gust carries its delta instead.
                text.Append(WeatherReadout.Decimal(atmosphere.GustSpeed, 1));
                text.Append("  (");
                text.Append(WeatherReadout.SignedDecimal(atmosphere.GustSpeed - live.WindSpeed, 1));
                text.Append(')');
            }
            else
            {
                text.Append(WeatherReadout.Unknown);
            }
            roseGustText.text = text.ToString();

            // The front kind is named once on the radar's boundary line and in the banner; the
            // veer here is only how far the arriving air turns the wind.
            roseVeerText.text = front.Present
                ? WeatherReadout.Veer(heading, arriving)
                : WeatherReadout.Unknown;

            roseShearText.text = hasAtmosphere
                ? WeatherReadout.Decimal(atmosphere.Shear, 2)
                : WeatherReadout.Unknown;
            if (roseShearNote != null)
            {
                roseShearNote.text = hasAtmosphere
                    ? WeatherReadout.ShearLabel(atmosphere.Shear)
                    : string.Empty;
            }
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

        /// <summary>
        /// The forecast rows. A held sky suspends them: the schedule's forward sample is not what
        /// the next hours will bring while an override owns the world, and showing it beside a
        /// forced sky is exactly the contradiction this panel no longer prints.
        /// </summary>
        private void RefreshForecast(WeatherSnapshot snapshot, WeatherForecast forecast, bool forced)
        {
            if (forecastNote != null)
            {
                forecastNote.text = !snapshot.Available ? "NO SCHEDULE ON THIS MISSION"
                    : forced ? "HELD BY OVERRIDE — NOT SHOWN"
                    : "IDENTICAL ON EVERY PEER";
            }

            bool hasForecast = !forced && forecast != null && forecast.Count > 0;
            int shown = hasForecast ? Mathf.Min(rowCapacity, forecast.Count) : 0;

            if (forecastEmpty != null)
            {
                forecastEmpty.text = !snapshot.Available ? "NO READOUT — NO WEATHER SCHEDULE ON THIS MISSION"
                    : forced ? "SCHEDULE HELD BY OVERRIDE — FORECAST NOT SHOWN"
                    : "NO FORECAST";
                SetActive(forecastEmpty.gameObject, shown == 0);
            }
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

                // The mark owns the dead band and the number follows the same decision, so a
                // level row reads "0" and can never print a signed zero. The unit is the column
                // header's, and the sign says the direction the colour repeats. The mark's own
                // glyph is not printed: the cockpit font has no triangles.
                string mark = WeatherReadout.TrendMark(entry.ConditionsDelta, WeatherReadout.TrendDeadband);
                row.Trend.text = mark == "=" ? "0"
                    : WeatherReadout.SignedDecimal(entry.ConditionsDelta * 100f, 0);
                row.Trend.color = mark == "▲" ? AvTheme.RailCaution
                                : mark == "▼" ? AvTheme.RailInfo
                                : AvTheme.Dim;

                text.Length = 0;
                text.Append(WeatherReadout.Compass16(WeatherReadout.WindFrom(entry.State.WindHeading)));
                text.Append(' ');
                text.Append(WeatherReadout.Decimal(entry.State.WindSpeed, 1));
                text.Append(" (");
                text.Append(WeatherReadout.SignedDecimal(entry.WindDelta, 1));
                text.Append(')');
                row.Wind.text = text.ToString();
            }
        }

        /// <summary>
        /// The host's one row of control, and the schedule it shows: the mission clock, the next
        /// transition, the front's approach drawn along the forecast horizon, and a tick per
        /// transition, tinted by that transition's severity. A held sky shows the hold instead of
        /// a schedule that will not run.
        /// </summary>
        private void RefreshControl(
            WeatherSnapshot snapshot, WeatherForecast forecast, WeatherFront front, WeatherState live, bool forced)
        {
            if (controlClock == null) return;

            text.Length = 0;
            text.Append("T+");
            text.Append(WeatherReadout.Clock(snapshot.Available ? snapshot.MissionTime : manager.MissionTime));
            controlClock.text = text.ToString();

            bool hasForecast = !forced && forecast != null && forecast.Count > 0;
            if (forced)
            {
                text.Length = 0;
                text.Append("HELD — ");
                text.Append(WeatherReadout.Regime(live.Regime));
                controlNext.text = text.ToString();
            }
            else if (hasForecast)
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

            bool scheduleOn = !WeatherView.OverrideActive(manager);
            string tooltip = scheduleOn ? ScheduleOnTooltip : ScheduleOffTooltip;
            scheduleButton.SetText(scheduleOn ? "ON" : "OFF");
            // Latched, not painted: the sheet's latched button is a translucent wash, and the
            // word ON/OFF carries the state without a solid accent plate.
            scheduleButton.SetLatched(scheduleOn);
            scheduleButton.WithTooltip(tooltip);
            if (scheduleHover != null) scheduleHover.SetText(tooltip);

            float horizon = hasForecast
                ? Mathf.Max(WeatherForecast.MinStepSeconds,
                            forecast[forecast.Count - 1].AtSeconds - snapshot.MissionTime)
                : 0f;

            var track = marker.parent as RectTransform;
            float trackWidth = track != null ? track.sizeDelta.x : 0f;
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

            trackLeft.text = forced ? "SCHEDULE HELD" : hasForecast ? "NOW" : "NO FORECAST";
            text.Length = 0;
            text.Append("T+");
            text.Append(WeatherReadout.Clock(horizon));
            trackRight.text = hasForecast ? text.ToString() : WeatherReadout.Unknown;

            // The front fill grows towards the marker as the boundary closes, so the clock and
            // the picture of the approach always agree.
            float fill = 0f;
            bool frontAhead = false;
            if (!forced && front.Present && horizon > 0f)
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

        private void RefreshRadar(WeatherSnapshot snapshot)
        {
            if (radar == null) return;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return;
            Vector3 position = cameras.transform.position;
            radar.Refresh(snapshot, manager, position.x, position.z);
        }

        /// <summary>The reader's world position, the same reading the radar section is handed.</summary>
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

            if (WeatherView.OverrideActive(manager))
            {
                manager.ReleaseOverrides();
                Echo("SCHEDULE — ON");
            }
            else
            {
                WeatherSnapshot snapshot = WeatherView.Capture(manager, 0);
                if (!snapshot.Available)
                {
                    Echo("NO READOUT — SCHEDULE UNCHANGED");
                }
                else
                {
                    manager.ForceRegime(snapshot.Model.Regime);
                    Echo("SCHEDULE — OFF, HOLDING " + WeatherReadout.Regime(snapshot.Model.Regime));
                }
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
            if (!snapshot.Available) return "NO WEATHER READOUT";
            if (snapshot.Live.IsSevere) return "EXPECT LIGHTNING";
            if (snapshot.CloudOcclusion > 0.5f) return "IR SEEKERS DEGRADED";
            if (snapshot.LocalWindSpeed > 15f) return "GUSTY WINDS";
            return "NO WEATHER HAZARD";
        }

        private sealed class ForecastRow
        {
            /// <summary>A row is one 16px table line, a shade tighter than the reading rhythm.</summary>
            private const float LabelHeight = 14f;
            private const float ChipHeight = 14f;

            public GameObject Root;
            public TMP_Text Age;
            public Image Chip;
            public TMP_Text ChipText;
            public TMP_Text Trend;
            public TMP_Text Wind;

            public static ForecastRow Build(RectTransform parent, float x, float y, float width)
            {
                var row = new ForecastRow();
                var root = new GameObject("ForecastRow", typeof(RectTransform));
                row.Root = root;
                var rect = (RectTransform)root.transform;
                rect.SetParent(parent, false);
                AvKit.Place(rect, new Rect(x, y, width, WeatherRadarPage.RowPitch));

                // The echo table's own column edges, so AGE / SKY / TREND / WIND land on the
                // same x as KIND / TOP / WARN / VECTOR + RNG·BRG: one column grid for both.
                float[] edges = WeatherRadarPage.TableColumnEdges;
                float inset = WeatherRadarPage.TableInset;
                float ageX = inset + edges[0];
                float skyX = inset + edges[1];
                float trendX = inset + edges[2];
                float windX = inset + edges[3];

                float labelTop = -(WeatherRadarPage.RowPitch - LabelHeight) * 0.5f;
                float chipTop = -(WeatherRadarPage.RowPitch - ChipHeight) * 0.5f;

                row.Age = AvStyled.Label(rect, new Rect(ageX, labelTop, skyX - ageX, LabelHeight), "", "kv-key");

                var chipRect = new Rect(skyX, chipTop, trendX - skyX, ChipHeight);
                // Neutral frame at build: the refresh tints the fill and the text with the
                // row's own severity, and a baked "live" frame would stay green under a red
                // storm chip.
                row.Chip = AvStyled.Box(rect, chipRect, "chip");
                row.ChipText = AvStyled.Label(rect, chipRect, "", "chip",
                    align: TextAlignmentOptions.Center);

                row.Trend = AvStyled.Label(rect, new Rect(trendX, labelTop, windX - trendX, LabelHeight),
                    "", "kv-value");
                // The column takes the table's remaining width, so the reading is guaranteed to
                // fit; overflow rather than ellipsis, which could cut the gust delta away.
                row.Wind = AvStyled.Label(
                    rect, new Rect(windX, labelTop, width - windX, LabelHeight), "", "kv-value");
                row.Wind.enableWordWrapping = false;
                row.Wind.overflowMode = TextOverflowModes.Overflow;
                return row;
            }

            public void SetVisible(bool on) => SetActive(Root, on);
        }
    }
}
