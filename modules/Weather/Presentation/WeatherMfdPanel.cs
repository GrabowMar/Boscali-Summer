using System;
using System.Collections.Generic;
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
    /// "WEA" — the environment panel. It states what the sky is doing now, what the
    /// deterministic schedule holds for the next stretch of the mission, and — on the host —
    /// the controls that can hold the sky or hand it back. A client reads the same forecast
    /// because every peer derives it from the mission clock alone, and the panel says so
    /// instead of offering controls it cannot use.
    /// </summary>
    internal sealed class WeatherMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.25f;

        private const int ChipCount = 4;
        private const int PageEnvironment = 0;
        private const int PageRadar = 1;

        private const float RowHeight = 16f;
        private const float RowPitch = 18f;
        /// <summary>Scroll content height for the radar page: scope, controls and three echo rows.</summary>
        private const float RadarPageHeight = 560f;
        private const float ControlHeight = 24f;
        private const float ControlPitch = 28f;
        private const float ControlGap = 6f;
        private const float ToggleWidth = 78f;

        private const int KvConditions = 0;
        private const int KvCloudBase = 1;
        private const int KvWindMean = 2;
        private const int KvWindLocal = 3;
        private const int KvTurbulence = 4;
        private const int KvOcclusion = 5;
        private const int KvDaylight = 6;
        private const int KvCount = 7;

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

        private readonly TMP_Text[] kvValues = new TMP_Text[KvCount];
        private readonly List<ForecastRow> forecastRows = new List<ForecastRow>(WeatherForecast.MaxEntries);
        private TMP_Text nextChange;
        private TMP_Text trend;

        private AvButton scheduleButton;
        private AvTooltipTarget scheduleHover;
        private readonly List<AvButton> regimeButtons = new List<AvButton>(WeatherRegimes.Count);
        private readonly List<Action> refreshers = new List<Action>();

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
            nextChange = null;
            trend = null;
            scheduleButton = null;
            scheduleHover = null;
            forecastRows.Clear();
            regimeButtons.Clear();
            refreshers.Clear();
            Array.Clear(kvValues, 0, kvValues.Length);
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

            shell = AvScreen.Build(
                content, MfdSlots.Weather,
                new[] { "ENV", "RADAR" },
                new[]
                {
                    new[] { "SKY", "LIVE" },
                    new[] { "DECK", "LOCAL" },
                    new[] { "WIND", "FIELD" },
                },
                ChipCount, Width, height, _ => nextRefresh = 0f);

            shell.DataBar.State.text = "ENVIRONMENT";

            // The mean-wind reading is a phrase, not a number: at the display size of the
            // other two it would be clipped, so it drops to body type and fits its cell.
            shell.Metrics[2].Value.fontSize = AvTokens.FontBody;

            BuildPage(shell.CreatePage(PageEnvironment, "EnvironmentPage"));
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

        // ---- Page ------------------------------------------------------------------------

        private void BuildPage(GameObject page)
        {
            float contentHeight = 22f + KvCount * RowPitch +
                                  22f + RowPitch + WeatherForecast.MaxEntries * RowPitch +
                                  22f + ControlPitch + 2f * (ControlHeight + ControlGap) + 10f;

            Rect body = shell.Body;
            RectTransform parent = AvScreen.Scroll((RectTransform)page.transform, body, contentHeight, out body);
            float x = body.x + AvScreen.SpineInset;
            float width = body.width - AvScreen.SpineInset;
            float y = body.y;

            AvStyled.Spine(parent, new Rect(body.x, body.y, 3f, body.height));

            y = SectionHeader(parent, x, y, width, "NOW", "LOCAL READOUT", band: false);
            kvValues[KvConditions] = KvRow(parent, x, y, width, "CONDITIONS");
            kvValues[KvCloudBase] = KvRow(parent, x, y - RowPitch, width, "CLOUD BASE");
            kvValues[KvWindMean] = KvRow(parent, x, y - RowPitch * 2f, width, "WIND (MEAN)");
            kvValues[KvWindLocal] = KvRow(parent, x, y - RowPitch * 3f, width, "LOCAL WIND");
            kvValues[KvTurbulence] = KvRow(parent, x, y - RowPitch * 4f, width, "TURBULENCE");
            kvValues[KvOcclusion] = KvRow(parent, x, y - RowPitch * 5f, width, "CLOUD OCCLUSION");
            kvValues[KvDaylight] = KvRow(parent, x, y - RowPitch * 6f, width, "DAYLIGHT");
            y -= KvCount * RowPitch;

            y = SectionHeader(parent, x, y, width, "FORECAST", "DETERMINISTIC SCHEDULE", band: true);
            nextChange = AvStyled.Label(parent, new Rect(x, y, width * 0.62f, RowHeight), "", "kv-key");
            trend = AvStyled.Label(parent, new Rect(x + width * 0.62f, y, width * 0.38f, RowHeight), "", "kv-value");
            y -= RowPitch;
            for (int i = 0; i < WeatherForecast.MaxEntries; i++)
            {
                forecastRows.Add(ForecastRow.Build(parent, x, y - i * RowPitch, width));
            }
            y -= WeatherForecast.MaxEntries * RowPitch;

            BuildControl(parent, x, y, width);
        }

        private void BuildRadarPage(GameObject page)
        {
            Rect body = shell.Body;
            var pageRect = (RectTransform)page.transform;
            RectTransform parent = AvScreen.Scroll(pageRect, body, RadarPageHeight, out body);
            radar = new WeatherRadarPage(parent, body.x + AvScreen.SpineInset, body.y, body.width - AvScreen.SpineInset, settings);
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
            return y - 22f;
        }

        private static TMP_Text KvRow(RectTransform parent, float x, float y, float width, string key)
        {
            AvStyled.Label(parent, new Rect(x, y, width * 0.5f, RowHeight), key, "kv-key");
            return AvStyled.Label(parent, new Rect(x + width * 0.5f, y, width * 0.5f, RowHeight), "", "kv-value");
        }

        private void BuildControl(RectTransform parent, float x, float y, float width)
        {
            bool host = manager != null && manager.HostAuthority;
            y = SectionHeader(parent, x, y, width, "CONTROL", host ? "HOST ONLY" : "READ ONLY", band: false);

            if (!host)
            {
                AvStyled.Label(parent, new Rect(x, y, width, RowHeight), "WEATHER IS HOST-AUTHORITATIVE", "row-name");
                AvStyled.Label(parent, new Rect(x, y - RowPitch, width, RowHeight),
                    "the host owns the sky; this panel is read-only", "row-sub");
                return;
            }

            scheduleHover = RowHover(parent, new Rect(x, y, width, ControlHeight), ScheduleOnTooltip);
            AvStyled.Label(parent, new Rect(x, y, width - ToggleWidth - ControlGap, ControlHeight), "SCHEDULE",
                "row-value", align: TextAlignmentOptions.MidlineLeft);
            scheduleButton = AvStyled.Button(
                parent, new Rect(x + width - ToggleWidth, y, ToggleWidth, ControlHeight), "ON", "btn", ToggleSchedule);
            scheduleButton.WithTooltip(ScheduleOnTooltip);

            float buttonWidth = (width - ControlGap * 2f) / 3f;
            for (int i = 0; i < WeatherRegimes.Count; i++)
            {
                int index = i;
                string label = WeatherRegimes.Label(WeatherRegimes.FromIndex(i));
                float buttonX = x + (i % 3) * (buttonWidth + ControlGap);
                float buttonY = y - ControlPitch - (i / 3) * (ControlHeight + ControlGap);
                AvButton button = AvStyled.Button(parent, new Rect(buttonX, buttonY, buttonWidth, ControlHeight),
                    label, "btn", () => ApplyRegime(index));
                button.WithTooltip("Force " + label + " and hold the sky there until the schedule is released.");
                regimeButtons.Add(button);
            }

            refreshers.Add(RefreshControl);
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

            int severity = available ? WeatherReadout.Severity(live.Regime) : 0;
            shell.DataBar.SetChip(0, available ? WeatherReadout.Regime(live.Regime) : WeatherReadout.Unknown,
                                  available ? SeverityClass(severity) : "inert");

            bool host = manager.HostAuthority;
            shell.DataBar.SetChip(1, host ? "HOST" : "CLIENT", host ? "live" : "inert");

            bool overridden = manager.OverrideActive || snapshot.Overridden;
            shell.DataBar.SetChip(2, overridden ? "OVERRIDE" : "SCHEDULE",
                                  overridden ? "warn" : available ? "live" : "inert");
            shell.DataBar.SetChip(3, WeatherReadout.Clock(snapshot.MissionTime), "inert");

            shell.Metrics[0].Set(
                available ? WeatherReadout.Percent01(live.Conditions) : WeatherReadout.Unknown,
                "CLOUD COVER",
                available ? WeatherRegimes.Clamp01(live.Conditions) : 0f,
                available ? SeverityColor(severity) : AvTheme.RailInert);

            shell.Metrics[1].Set(
                available ? WeatherReadout.Meters(live.CloudBase) : WeatherReadout.Unknown,
                "CLOUD BASE",
                available ? Mathf.Clamp01(live.CloudBase / WeatherModel.MaxCloudBase) : 0f,
                available ? AvTheme.RailInfo : AvTheme.RailInert);

            shell.Metrics[2].Set(
                available ? WeatherReadout.Wind(live.WindSpeed, live.WindHeading) : WeatherReadout.Unknown,
                "MEAN WIND",
                available ? Mathf.Clamp01(live.WindSpeed / 25f) : 0f,
                available ? AvTheme.RailInfo : AvTheme.RailInert);

            kvValues[KvConditions].text = available ? WeatherReadout.Percent01(live.Conditions) : WeatherReadout.Unknown;
            kvValues[KvCloudBase].text = available ? WeatherReadout.Meters(live.CloudBase) : WeatherReadout.Unknown;
            kvValues[KvWindMean].text = available
                ? WeatherReadout.Wind(live.WindSpeed, live.WindHeading) : WeatherReadout.Unknown;
            kvValues[KvWindLocal].text = available
                ? WeatherReadout.Wind(snapshot.LocalWindSpeed, snapshot.LocalWindHeading) : WeatherReadout.Unknown;
            kvValues[KvTurbulence].text = available ? WeatherReadout.Decimal(live.Turbulence, 2) : WeatherReadout.Unknown;
            kvValues[KvOcclusion].text = available
                ? WeatherReadout.Percent01(snapshot.CloudOcclusion) : WeatherReadout.Unknown;
            kvValues[KvDaylight].text = available
                ? WeatherReadout.Percent01(snapshot.DaylightFactor) : WeatherReadout.Unknown;

            RefreshForecast(snapshot, forecast);
            RefreshRadar(snapshot);

            for (int i = 0; i < refreshers.Count; i++) refreshers[i]();

            string echo = Time.unscaledTime < actionEchoUntil ? actionEcho : null;
            shell.WriteStatus(
                available ? WeatherReadout.Regime(live.Regime) + " — " + Hazard(snapshot) : WeatherReadout.Unknown,
                echo ?? MapPicker.Prompt,
                WeatherReadout.Clock(available ? snapshot.MissionTime : manager.MissionTime));
        }

        private void RefreshForecast(WeatherSnapshot snapshot, WeatherForecast forecast)
        {
            bool hasForecast = forecast != null && forecast.Count > 0;
            int capacity = Mathf.Clamp(settings.ForecastSteps.Value, 0, forecastRows.Count);

            if (hasForecast)
            {
                nextChange.text = "NEXT CHANGE " + WeatherReadout.InSeconds(forecast.NextChangeSeconds) + "  " +
                                  WeatherReadout.Regime(forecast.NextRegime);
                trend.text = WeatherReadout.Trend(snapshot.Live.Conditions,
                                                  forecast[forecast.Count - 1].State.Conditions);
            }
            else
            {
                nextChange.text = "NEXT CHANGE " + WeatherReadout.Unknown;
                trend.text = "";
                capacity = 1;
            }

            int shown = hasForecast ? Mathf.Min(capacity, forecast.Count) : Mathf.Min(capacity, 1);
            for (int i = 0; i < forecastRows.Count; i++)
            {
                ForecastRow row = forecastRows[i];
                if (i >= shown)
                {
                    row.SetVisible(false);
                    continue;
                }

                row.SetVisible(true);
                if (!hasForecast)
                {
                    row.Age.text = WeatherReadout.Unknown;
                    row.Detail.text = "AWAITING SCHEDULE";
                    row.Rail.color = AvTheme.RailInert;
                    continue;
                }

                WeatherForecastEntry entry = forecast[i];
                row.Age.text = WeatherReadout.InSeconds(entry.AtSeconds - snapshot.MissionTime);
                row.Detail.text = WeatherReadout.Regime(entry.State.Regime) + "  " +
                                  WeatherReadout.Percent01(entry.State.Conditions) + "  " +
                                  WeatherReadout.Wind(entry.State.WindSpeed, entry.State.WindHeading);
                row.Rail.color = RailColor(entry.State.Regime);
            }
        }

        private void RefreshControl()
        {
            if (manager == null || scheduleButton == null) return;

            bool scheduleOn = !manager.OverrideActive;
            string tooltip = scheduleOn ? ScheduleOnTooltip : ScheduleOffTooltip;
            scheduleButton.SetText(scheduleOn ? "ON" : "OFF");
            scheduleButton.SetLatched(scheduleOn);
            scheduleButton.WithTooltip(tooltip);
            scheduleHover.SetText(tooltip);

            WeatherSnapshot snapshot = manager.Snapshot;
            int target = WeatherRegimes.Index(manager.OverrideActive
                ? snapshot.Model.Regime
                : snapshot.Live.Regime);
            for (int i = 0; i < regimeButtons.Count; i++)
            {
                regimeButtons[i].SetLatched(snapshot.Available && target == i);
            }
        }

        // ---- Actions ---------------------------------------------------------------------

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

        private void ApplyRegime(int index)
        {
            if (manager == null) return;

            WeatherRegime regime = WeatherRegimes.FromIndex(index);
            manager.ForceRegime(regime);
            Echo("OVERRIDE — " + WeatherRegimes.Label(regime));
            nextRefresh = 0f;
        }

        /// <summary>Confirm the action on the status strip for a moment.</summary>
        private void Echo(string text)
        {
            actionEcho = text;
            actionEchoUntil = Time.unscaledTime + 1.6f;
        }

        // ---- Formatting ------------------------------------------------------------------

        private static string Hazard(WeatherSnapshot snapshot)
        {
            if (snapshot.Live.IsSevere) return "SEVERE — EXPECT LIGHTNING";
            if (snapshot.CloudOcclusion > 0.5f) return "IR SEEKERS DEGRADED";
            if (snapshot.LocalWindSpeed > 15f) return "GUSTY SURFACE WINDS";
            return "NO WEATHER HAZARD";
        }

        private static string SeverityClass(int severity) =>
            severity >= 3 ? "danger" : severity >= 2 ? "warn" : "live";

        private static Color SeverityColor(int severity) =>
            severity >= 3 ? AvTheme.RailDanger : severity >= 2 ? AvTheme.RailCaution : AvTheme.RailReady;

        /// <summary>The regime's rail colour, resolved from the same class the readout names.</summary>
        private static Color RailColor(WeatherRegime regime)
        {
            AvStyle style = AvStyleHost.Style(WeatherReadout.RailClass(regime));
            return AvStyleHost.Resolve(style.Background, AvTheme.RailInert);
        }

        private sealed class ForecastRow
        {
            public Image Rail;
            public TMP_Text Age;
            public TMP_Text Detail;
            private bool visible = true;

            public static ForecastRow Build(RectTransform parent, float x, float y, float width)
            {
                var row = new ForecastRow();
                row.Rail = AvStyled.Rail(parent, new Rect(x, y, 3f, RowHeight), "ready");
                row.Age = AvStyled.Label(parent, new Rect(x + 12f, y, width * 0.3f, RowHeight), "", "kv-key");
                row.Detail = AvStyled.Label(
                    parent, new Rect(x + width * 0.32f, y, width * 0.68f, RowHeight), "", "kv-value");
                return row;
            }

            public void SetVisible(bool on)
            {
                if (visible == on) return;
                visible = on;
                Rail.gameObject.SetActive(on);
                Age.gameObject.SetActive(on);
                Detail.gameObject.SetActive(on);
            }
        }
    }
}
