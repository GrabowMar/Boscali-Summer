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
    /// Read-only battlefield environment briefing on the maximised map. Kit v2 (AvConsole):
    /// header chips/metrics carry the live top-line numbers, WEATHER holds the current-conditions
    /// card, vertical profile and the 60-minute outlook table, SKY &amp; AIR holds the environmental
    /// watch, solar/lunar ephemeris, wind and local-air readouts.
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
        private AvConsole console;
        private AvChip[] chips;
        private AvMetric[] metrics;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;

        // Forecast page widgets
        private AvReadout heroReadout;
        private AvRow heroAdvisory;
        private AvRow heroReading;
        private AvSection profileSection;
        private AvRow profileCloudRow;
        private AvRow profileAltRow;
        private AvRow profileLocalRow;
        private readonly List<ForecastRowPart> timelineRows = new List<ForecastRowPart>(6);

        // Environment page widgets
        private AvRow watchLight;
        private AvRow watchDeck;
        private AvRow watchWind;
        private AvReadout solarReadout;
        private AvRow solarEventsRow;
        private AvRow lunarPhaseRow;
        private AvRow lunarIllumRow;
        private AvRow lunarGuidanceRow;
        private AvRow windRow;
        private AvRow windTurbulenceRow;
        private AvRow windAdvisoryRow;
        private AvGauge densityGauge;
        private AvRow soundRow;
        private AvRow altRow;

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
            console = null;
            chips = null;
            metrics = null;
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
            var root = new GameObject("BoscaliWeather.Screen", typeof(RectTransform));
            screenRoot = root;
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = ResolvePanelHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            ClampPanelIntoCanvas(rootRect);

            console = AvConsole.Build(rootRect, MfdSlots.Weather, "BATTLEFIELD ENVIRONMENT", 2, Width, height);
            chips = console.Chips(ChipCount);
            metrics = console.Metrics("COVER", "BASE", "WIND", "DENSITY");
            console.Tabs((AvIcon.Cloud, "WEATHER"), (AvIcon.Wind, "SKY & AIR"));
            console.PageChanged += _ => nextRefresh = 0f;

            BuildForecastPage(console.Page(TabForecast));
            BuildEnvironmentPage(console.Page(TabEnvironment));

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Weather;
            result.displayPanel = root;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                UnityEngine.Object.Destroy(root);
                console = null;
                return null;
            }

            console.Finish();
            console.Ticker.Add(-1, AvTickRate.Fast, TickRefresh);
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

        // Equivalent of the v1 kit's screen height resolver: the bezel bay measures taller than
        // the panel's own floor, so the height is read from the slot rather than hard-coded.
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

        // Equivalent of the v1 kit's canvas-clamp helper: nudges a panel wholly inside its
        // canvas once its size is final, so a wide bezel screen never clips off-edge.
        private static void ClampPanelIntoCanvas(RectTransform panel, float margin = 8f)
        {
            if (panel == null) return;
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null || panel.parent == null) return;

            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
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

        // ---- Page Builders ---------------------------------------------------------------

        private void BuildForecastPage(AvFlow p)
        {
            p.Section(AvIcon.Cloud, "CURRENT CONDITIONS", "LIVE WEATHER");
            heroReadout = p.Add(new AvReadout(p.Content));
            heroAdvisory = p.Add(new AvRow(p.Content));
            heroReading = p.Add(new AvRow(p.Content));

            profileSection = p.Section(AvIcon.LayersSubtract, "VERTICAL PROFILE", "");
            profileCloudRow = p.Add(new AvRow(p.Content));
            profileAltRow = p.Add(new AvRow(p.Content));
            profileLocalRow = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Clock, "NEXT 60 MINUTES", "MODEL OUTLOOK");
            int[] offsets = Domain.WeatherForecast.DefaultOffsetsMinutes;
            timelineRows.Clear();
            for (int i = 0; i < offsets.Length; i++)
                timelineRows.Add(p.Add(new ForecastRowPart(p.Content, "Forecast " + i)));
        }

        private void BuildEnvironmentPage(AvFlow p)
        {
            p.Section(AvIcon.InfoCircle, "FLIGHT CONDITIONS", "ENVIRONMENTAL WATCH");
            watchLight = p.Add(new AvRow(p.Content));
            watchDeck = p.Add(new AvRow(p.Content));
            watchWind = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Circle, "DAYLIGHT", "");
            solarReadout = p.Add(new AvReadout(p.Content));
            solarEventsRow = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Star, "MOONLIGHT", "");
            lunarPhaseRow = p.Add(new AvRow(p.Content));
            lunarIllumRow = p.Add(new AvRow(p.Content));
            lunarGuidanceRow = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Wind, "WIND & TURBULENCE", "");
            windRow = p.Add(new AvRow(p.Content));
            windTurbulenceRow = p.Add(new AvRow(p.Content));
            windAdvisoryRow = p.Add(new AvRow(p.Content));

            p.Section(AvIcon.Gauge, "AIR AT CAMERA ALTITUDE", "");
            densityGauge = p.Add(new AvGauge(p.Content, "DENSITY", AvGaugeShape.Bar));
            soundRow = p.Add(new AvRow(p.Content));
            altRow = p.Add(new AvRow(p.Content));
        }

        // ---- Refresh ---------------------------------------------------------------------

        private void TickRefresh()
        {
            if (screen == null || console == null) return;
            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            if (!visible) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f; // 4 Hz refresh
            Refresh();
        }

        private void Refresh()
        {
            if (console == null || weather == null) return;
            LevelInfo level = LevelInfo.i;
            if (level == null)
            {
                console.SetTitle("METOC / NO MISSION");
                chips[0].Set("WX OFFLINE", AvState.Info);
                chips[1].Set("NO MISSION", AvState.Info);
                console.Footer.Set("Battlefield environment unavailable.", AvState.Inert);
                return;
            }

            // Top metrics row
            float cond = weather.CurrentConditions;
            RegimeSnapshot regime = weather.CurrentRegime;
            AvState regimeState = RegimeState(regime.Type);
            metrics[0].Set(AvNum.Percent(cond), regime.Code, cond, regimeState);

            WeatherField localField = weather.Field;
            WeatherPoint localPoint = weather.LocalWeather;
            float cloudShift = localField != null && localField.IsBuilt
                ? weather.CurrentCloudHeight - localField.Regional().CloudBase : 0f;
            float deck = localField != null && localField.IsBuilt
                ? localPoint.CloudBase + cloudShift : weather.CurrentCloudHeight;
            float cloudTop = localField != null && localField.IsBuilt
                ? localPoint.CloudTop + cloudShift : deck + 1500f;
            cloudTop = Mathf.Max(deck + 500f, cloudTop);
            bool lowDeck = deck < 1600f;
            metrics[1].Set(AvNum.Fixed(deck, 0), "M", Mathf.Clamp01(deck / 4000f), lowDeck ? AvState.Caution : AvState.Ready);

            Domain.WeatherForecast.FormatWind(weather.CurrentWindVelocity.x, weather.CurrentWindVelocity.z, out float kts, out int towards, out int from);
            bool strongWind = kts > 25f;
            metrics[2].Set(AvNum.Fixed(kts, 0), "KT " + AvNum.Fixed(from, 0) + "°", Mathf.Clamp01(kts / 40f), strongWind ? AvState.Caution : AvState.Ready);

            Camera camera = Camera.main;
            Vector3 samplePos = camera != null ? camera.transform.position : Vector3.zero;
            float airDensity = camera != null ? LevelInfo.GetAirDensity(samplePos.y) : 0f;
            metrics[3].Set(camera != null ? AvNum.Percent(airDensity) : "—", "SL", Mathf.Clamp01(airDensity), AvState.Ready);

            // Chips
            console.SetTitle("METOC / BATTLEFIELD");
            chips[0].Set("WX " + regime.Code, regime.Type == WeatherRegimeType.Storm ? AvState.Danger : AvState.Ready);
            WeatherField stateField = weather.Field;
            bool dynamicField = stateField != null && stateField.Key.Dynamic;
            float missionNow = NetworkSceneSingleton<MissionManager>.i != null
                ? NetworkSceneSingleton<MissionManager>.i.MissionTime : 0f;
            float nextIn = dynamicField ? Mathf.Max(0f, stateField.Timeline.NextChangeAt - missionNow) : 0f;
            string countdown = AvNum.Clock(nextIn);
            chips[1].Set(dynamicField ? "NEXT " + countdown : "HELD WEATHER", AvState.Info);

            // Active Tab Content
            if (console.CurrentPage == TabForecast)
            {
                RefreshForecastTab(cond, regime, deck, cloudTop, kts, samplePos, airDensity, camera != null);
            }
            else if (console.CurrentPage == TabEnvironment)
            {
                RefreshEnvironmentTab(level, kts, towards);
            }

            if (weather.IsManualOverride)
            {
                console.Footer.Set("Held by weather console (Ctrl+O) — " + regime.Name + ".", AvState.Info);
            }
            else if (dynamicField)
            {
                TimelineState timeline = stateField.Timeline;
                string now = RegimeSnapshot.FromType(timeline.To).Name;
                string next = RegimeSnapshot.FromType(timeline.Next).Name;
                string status = timeline.Blend < 1f
                    ? "Changing to " + now + " (" + AvNum.Percent(timeline.Blend) + ")."
                    : timeline.Next == timeline.To
                        ? now + " holds — next step in " + countdown + "."
                        : now + " — " + next + " in " + countdown + ".";
                console.Footer.Set(status, AvState.Info);
            }
            else
            {
                console.Footer.Set("Held weather — mission conditions.", AvState.Info);
            }
        }

        private void RefreshForecastTab(
            float cond,
            RegimeSnapshot regime,
            float deck,
            float cloudTop,
            float windKts,
            Vector3 samplePos,
            float airDensity,
            bool hasCamera)
        {
            heroReadout.Set(regime.Code, regime.Name, AvNum.Percent(cond) + " COVER");
            heroAdvisory.Set("ADVISORY", regime.TacticalBriefing, "", AvState.Info);

            float rain = weather.LocalRainIntensity;
            heroReading.Set("READING", "",
                "D " + AvNum.Fixed(deck, 0) + "M  W " + AvNum.Fixed(windKts, 0) + "KT  R " + AvNum.Percent(rain),
                AvState.Info);

            // Airspace profile / stratification
            float ownAlt = samplePos.y;
            string status; AvState statusState;
            if (!hasCamera) { status = "CAMERA UNAVAILABLE"; statusState = AvState.Inert; }
            else if (ownAlt < deck - 50f) { status = "BELOW CLOUD BASE"; statusState = AvState.Info; }
            else if (ownAlt < cloudTop - 50f) { status = "IN CLOUD LAYER"; statusState = AvState.Caution; }
            else { status = "ABOVE CLOUD TOP"; statusState = AvState.Ready; }
            profileSection.SetCaption(status);

            profileCloudRow.Set("CLOUD LAYER", "", AvNum.Fixed(deck, 0) + "–" + AvNum.Fixed(cloudTop, 0) + " M", statusState);
            profileAltRow.Set("CAMERA ALTITUDE", "", hasCamera ? AvNum.Signed(ownAlt, 0) + " M" : "—", statusState);

            float soundSpeed = LevelInfo.GetSpeedOfSound(ownAlt);
            profileLocalRow.Set("LOCAL AIR", "Wind " + AvNum.Fixed(windKts, 0) + " kt, turbulence " + AvNum.Fixed(weather.CurrentTurbulence, 2) + ".",
                hasCamera ? AvNum.Percent(airDensity) + " SL  " + AvNum.Fixed(soundSpeed, 0) + " M/S" : "—", AvState.Info);

            // Timeline rows
            ForecastStep[] steps = weather.GetForecastTimeline();
            int[] offsets = Domain.WeatherForecast.DefaultOffsetsMinutes;
            if (steps != null)
            {
                for (int i = 0; i < steps.Length && i < timelineRows.Count; i++)
                {
                    ForecastStep s = steps[i];
                    bool now = i == 0;
                    string timeText = now ? "NOW" : "+" + (i < offsets.Length ? offsets[i] : 0);
                    bool storm = s.Regime.Type == WeatherRegimeType.Storm;
                    AvState rowState = storm ? AvState.Danger
                        : s.RainProbability > .2f ? AvState.Caution
                        : RegimeState(s.Regime.Type);
                    timelineRows[i].Set(timeText, now, s.Regime.Type, s.Regime.Code, s.Conditions, s.CloudDeckMetres, s.RainProbability, rowState);
                }
            }
        }

        private void RefreshEnvironmentTab(LevelInfo level, float kts, int towardsDeg)
        {
            // Environmental watch
            SolarData solar = weather.GetSolarData();
            float deck = weather.CurrentCloudHeight;
            bool lowLight = solar.ElevationDegrees <= -6f;
            bool lowDeck = deck < 1600f;
            bool strongWind = kts > 30f;

            watchLight.Set("LIGHT", "", solar.ElevationDegrees > 0f ? "DAYLIGHT" : lowLight ? "NIGHT" : "TWILIGHT",
                lowLight ? AvState.Caution : AvState.Info);
            watchDeck.Set("CLOUD DECK", "", (lowDeck ? "LOW " : "") + AvNum.Fixed(deck, 0) + " M", lowDeck ? AvState.Caution : AvState.Info);
            watchWind.Set("WIND", "", (strongWind ? "HIGH " : "") + AvNum.Fixed(kts, 0) + " KT", strongWind ? AvState.Caution : AvState.Info);

            // Solar ephemeris
            string solarState = solar.PolarDay ? "POLAR DAY"
                : solar.PolarNight ? "POLAR NIGHT"
                : solar.ElevationDegrees > 0f ? "DAYLIGHT"
                : solar.ElevationDegrees > -6f ? "CIVIL TWILIGHT"
                : "NIGHT";
            solarReadout.Set(AvNum.Signed(solar.ElevationDegrees, 1) + "°", "ELEVATION",
                "AZ " + AvNum.Fixed(solar.AzimuthDegrees, 0) + "°  T " + AvNum.Fixed(level.timeOfDay, 1) + "H");
            string events = solar.PolarDay ? "Continuous polar daylight, no sunset."
                : solar.PolarNight ? "Continuous polar night, no sunrise."
                : solar.NextEventName + " in " + AvNum.Fixed(solar.TimeToNextEventMinutes, 0) + " min.";
            solarEventsRow.Set("EVENTS", events, solarState, AvState.Info);

            // Lunar ephemeris
            LunarData lunar = weather.GetLunarData();
            lunarPhaseRow.Set("PHASE", "", lunar.PhaseName + "  " + AvNum.Percent(lunar.IlluminationFraction) + " LIT", AvState.Info);
            lunarIllumRow.Set("GLOW", lunar.IsMoonless ? "Low natural light." : "Moonlit.",
                AvNum.Percent(lunar.MoonlightIntensity), AvState.Info);
            lunarGuidanceRow.Set("GUIDANCE", lunar.IsMoonless
                ? "Low natural light — visual identification may be harder."
                : "Moonlight present — check cloud cover for visibility.", "", AvState.Info);

            // Wind dynamics
            Domain.WeatherForecast.FormatWind(weather.CurrentWindVelocity.x, weather.CurrentWindVelocity.z, out _, out _, out int fromDeg);
            windRow.Set("WIND", "", AvNum.Fixed(kts, 0) + " KT  FROM " + AvNum.Fixed(fromDeg, 0) + "°  TO " + AvNum.Fixed(towardsDeg, 0) + "°",
                kts > 25f ? AvState.Caution : AvState.Info);
            windTurbulenceRow.Set("TURBULENCE", "Mission wind.", AvNum.Fixed(weather.CurrentTurbulence, 2), AvState.Info);
            windAdvisoryRow.Set("ADVISORY", kts > 30f
                ? "Strong wind — expect drift and turbulence."
                : "Normal wind — watch for drift.", "", kts > 30f ? AvState.Caution : AvState.Info);

            // Atmosphere & performance
            Camera camera = Camera.main;
            float rho = camera != null ? LevelInfo.GetAirDensity(camera.transform.position.y) : 0f;
            float sos = camera != null ? LevelInfo.GetSpeedOfSound(camera.transform.position.y) : 0f;
            densityGauge.Set(rho, camera != null ? AvNum.Percent(rho) : "—", rho < .6f ? AvState.Caution : AvState.Ready);
            soundRow.Set("SPEED OF SOUND", "", camera != null ? AvNum.Fixed(sos, 0) + " M/S" : "—", AvState.Info);
            altRow.Set("SAMPLED ALTITUDE", "", camera != null ? AvNum.Signed(camera.transform.position.y, 0) + " M" : "—", AvState.Info);
        }

        private static AvState RegimeState(WeatherRegimeType type)
        {
            switch (type)
            {
                case WeatherRegimeType.Clear:
                case WeatherRegimeType.Fair:
                    return AvState.Info;
                case WeatherRegimeType.Scattered:
                    return AvState.Ready;
                case WeatherRegimeType.Broken:
                case WeatherRegimeType.Overcast:
                case WeatherRegimeType.RainSquall:
                    return AvState.Caution;
                case WeatherRegimeType.Storm:
                    return AvState.Danger;
                default:
                    return AvState.Info;
            }
        }

        /// <summary>
        /// One 60-minute outlook row: state rail, time, the regime pictogram (a genuine per-row
        /// data glyph, kept procedural per the kit v2 rollout brief), code badge, cover, cloud
        /// base and a rain bar. Hosts <see cref="WeatherGlyph"/> inside a kit v2 <see cref="AvPart"/>.
        /// </summary>
        private sealed class ForecastRowPart : AvPart
        {
            private readonly Image rail, rainTrack, rainFill;
            private readonly TMP_Text time, badge, cover, deckLabel, rainText;
            private readonly WeatherGlyph glyph;
            private AvState state = AvState.Info;
            private float rainFrac;

            public ForecastRowPart(RectTransform parent, string name)
            {
                Rect = AvLay.Child(parent, name);
                rail = AvLay.Solid(Rect, "Rail", Color.clear);
                time = AvText.Make(Rect, "Time", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineLeft);
                glyph = WeatherGlyph.Create(Rect, new Rect(48f, 3f, 24f, 24f));
                badge = AvText.Make(Rect, "Badge", AvTextRole.Micro, "", TextAlignmentOptions.Center);
                cover = AvText.Make(Rect, "Cover", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                deckLabel = AvText.Make(Rect, "Deck", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                rainTrack = AvLay.Solid(Rect, "RainTrack", Color.clear);
                rainFill = AvLay.Solid(Rect, "RainFill", Color.clear);
                rainText = AvText.Make(Rect, "RainText", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                Restyle();
            }

            public void Set(string timeText, bool now, WeatherRegimeType regime, string code,
                float coverFrac, float deckMetres, float rainProbability, AvState rowState)
            {
                time.text = timeText;
                time.fontStyle = now ? FontStyles.Bold : FontStyles.Normal;
                glyph.SetKind(regime);
                badge.text = code ?? "";
                cover.text = AvNum.Percent(coverFrac);
                deckLabel.text = AvNum.Fixed(deckMetres, 0) + " M";
                rainFrac = Mathf.Clamp01(rainProbability);
                rainText.text = rainProbability <= 0.05f ? "— DRY —" : "RAIN " + AvNum.Percent(rainProbability);
                state = rowState;
                Restyle();
                Place(lastSlot);
            }

            private AvSlot lastSlot;

            public override float Measure(float width) => 30f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                lastSlot = s;
                AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
                AvLay.Place(time.rectTransform, 8f, 0f, 38f, s.H);
                AvLay.Place(glyph.rectTransform, 48f, 3f, 24f, 24f);
                AvLay.Place(badge.rectTransform, 78f, (s.H - 15f) * 0.5f, 38f, 15f);
                AvLay.Place(cover.rectTransform, 122f, 0f, 40f, s.H);
                AvLay.Place(deckLabel.rectTransform, 168f, 0f, 62f, s.H);
                float barX = 240f, barW = Mathf.Max(36f, s.W - barX - 96f);
                AvLay.Place(rainTrack.rectTransform, barX, (s.H - 6f) * 0.5f, barW, 6f);
                AvLay.Place(rainFill.rectTransform, barX, (s.H - 6f) * 0.5f, barW * rainFrac, 6f);
                AvLay.Place(rainText.rectTransform, barX + barW + 6f, 0f, Mathf.Max(1f, s.W - barX - barW - 10f), s.H);
            }

            public override void Restyle()
            {
                AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
                rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
                time.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
                Color badgeColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.Dim);
                badge.color = badgeColor;
                glyph.color = badgeColor;
                cover.color = deckLabel.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value").Color, AvTheme.TextPrimary);
                rainTrack.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
                rainFill.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-fill " + AvStates.Class(state)).Background, AvTheme.Accent);
                rainText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            }
        }
    }
}
