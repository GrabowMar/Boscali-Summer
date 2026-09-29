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
        private const float Width = AvTokens.PanelWidth;

        private WeatherSettings settings;
        private WeatherManager weather;
        private ManualLogSource logger;

        private GameObject screenRoot;
        private MFDScreen screen;
        private AvConsole console;
        private WeatherEnvView view;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;


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
            view = null;
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

            float height = AvLay.ResolveHeight(
                templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);
            AvLay.ClampIntoCanvas(rootRect);

            view = new WeatherEnvView(rootRect, MfdSlots.Weather, Width, height);
            console = view.Console;
            console.PageChanged += _ => nextRefresh = 0f;

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
                view = null;
                return null;
            }

            view.Finish();
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
            if (view == null || weather == null) return;
            LevelInfo level = LevelInfo.i;
            if (level == null)
            {
                view.Apply(new EnvData
                {
                    HasMission = false,
                    Title = "METOC / NO MISSION",
                    Chip0 = "WX OFFLINE",
                    Chip0State = AvState.Info,
                    Chip1 = "NO MISSION",
                    Footer = "Battlefield environment unavailable.",
                    FooterState = AvState.Inert,
                });
                return;
            }

            float cond = weather.CurrentConditions;
            RegimeSnapshot regime = weather.CurrentRegime;
            WeatherField localField = weather.Field;
            WeatherPoint localPoint = weather.LocalWeather;
            bool built = localField != null && localField.IsBuilt;
            float cloudShift = built ? weather.CurrentCloudHeight - localField.Regional().CloudBase : 0f;
            float deck = built ? localPoint.CloudBase + cloudShift : weather.CurrentCloudHeight;
            float cloudTop = built ? localPoint.CloudTop + cloudShift : deck + 1500f;
            cloudTop = Mathf.Max(deck + 500f, cloudTop);

            Domain.WeatherForecast.FormatWind(weather.CurrentWindVelocity.x, weather.CurrentWindVelocity.z,
                out float kts, out int towards, out int from);

            Camera camera = Camera.main;
            Vector3 samplePos = camera != null ? camera.transform.position : Vector3.zero;
            SolarData solar = weather.GetSolarData();
            LunarData lunar = weather.GetLunarData();

            WeatherField stateField = weather.Field;
            bool dynamicField = stateField != null && stateField.Key.Dynamic;
            float missionNow = NetworkSceneSingleton<MissionManager>.i != null
                ? NetworkSceneSingleton<MissionManager>.i.MissionTime : 0f;
            float nextIn = dynamicField ? Mathf.Max(0f, stateField.Timeline.NextChangeAt - missionNow) : 0f;
            string countdown = AvNum.Clock(nextIn);

            string footer;
            if (weather.IsManualOverride)
                footer = "Held by weather console (Ctrl+O) — " + regime.Name + ".";
            else if (dynamicField)
            {
                TimelineState timeline = stateField.Timeline;
                string now = RegimeSnapshot.FromType(timeline.To).Name;
                string next = RegimeSnapshot.FromType(timeline.Next).Name;
                footer = timeline.Blend < 1f
                    ? "Changing to " + now + " (" + AvNum.Percent(timeline.Blend) + ")."
                    : timeline.Next == timeline.To
                        ? now + " holds — next step in " + countdown + "."
                        : now + " — " + next + " in " + countdown + ".";
            }
            else footer = "Held weather — mission conditions.";

            var data = new EnvData
            {
                Title = "METOC / BATTLEFIELD",
                Chip0 = "WX " + regime.Code,
                Chip0State = regime.Type == WeatherRegimeType.Storm ? AvState.Danger : AvState.Ready,
                Chip1 = dynamicField ? "NEXT " + countdown : "HELD WEATHER",
                Footer = footer,
                FooterState = AvState.Info,
                Regime = regime.Type,
                Code = regime.Code,
                Name = regime.Name,
                Briefing = regime.TacticalBriefing,
                Cover = cond,
                Deck = deck,
                Top = cloudTop,
                VisibilityKm = built ? localPoint.VisibilityKm : -1f,
                Rain = weather.LocalRainIntensity,
                Turbulence = weather.CurrentTurbulence,
                WindKts = kts,
                WindFrom = from,
                WindTo = towards,
                HasCamera = camera != null,
                CameraAlt = samplePos.y,
                AirDensity = camera != null ? LevelInfo.GetAirDensity(samplePos.y) : 0f,
                SoundSpeed = camera != null ? LevelInfo.GetSpeedOfSound(samplePos.y) : 0f,
                SunElevation = solar.ElevationDegrees,
                SunAzimuth = solar.AzimuthDegrees,
                TimeOfDay = level.timeOfDay,
                Sunrise = solar.SunriseHour,
                Sunset = solar.SunsetHour,
                PolarDay = solar.PolarDay,
                PolarNight = solar.PolarNight,
                SunEvent = solar.NextEventName + " in " + AvNum.Fixed(solar.TimeToNextEventMinutes, 0) + " min.",
                MoonPhase = lunar.PhaseName,
                MoonLit = lunar.IlluminationFraction,
                MoonGlow = lunar.MoonlightIntensity,
                MoonWaxing = lunar.PhaseName.StartsWith("Waxing", StringComparison.Ordinal) ||
                             lunar.PhaseName.StartsWith("First", StringComparison.Ordinal),
                Moonless = lunar.IsMoonless,
            };

            ForecastStep[] steps = weather.GetForecastTimeline();
            if (steps != null)
            {
                int[] offsets = Domain.WeatherForecast.DefaultOffsetsMinutes;
                data.Forecast = new EnvForecastRow[steps.Length];
                for (int i = 0; i < steps.Length; i++)
                    data.Forecast[i] = new EnvForecastRow
                    {
                        OffsetMinutes = i < offsets.Length ? offsets[i] : 0,
                        Regime = steps[i].Regime.Type,
                        Code = steps[i].Regime.Code,
                        Cover = steps[i].Conditions,
                        Deck = steps[i].CloudDeckMetres,
                        Rain = steps[i].RainProbability,
                    };
            }

            view.Apply(data);
        }
    }
}
