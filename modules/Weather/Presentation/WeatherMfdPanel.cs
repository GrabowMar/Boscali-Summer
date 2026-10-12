using NOAvionics;
using System;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Runtime;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// Read-only battlefield environment briefing on the maximised map. Kit v2 (AvConsole):
    /// header metrics carry the live top-line numbers, WEATHER holds the current-conditions
    /// card, rings, growing vertical profile, 60-minute outlook and cover trend, SKY &amp; AIR holds the
    /// solar/lunar ephemeris, wind, local-air rings and the daylight and air-density charts.
    /// </summary>
    internal sealed class WeatherMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;

        private WeatherSettings settings;
        private WeatherManager weather;

        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Weather, "ENV", "BoscaliWeather.Screen", preferLeft: false) { Host = true };
        private MFDScreen screen => installer.Screen;
        private AvConsole console;
        private WeatherEnvView view;

        private float nextRefresh;
        private readonly float[] densityCurve = new float[13];


        public void Configure(WeatherSettings config, WeatherManager manager, ManualLogSource log)
        {
            settings = config;
            weather = manager;
            installer.Log = log;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();

            console = null;
            view = null;
            nextRefresh = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (installer.Failed || weather == null || settings == null) return;
            if (!settings.Enabled.Value)
            {
                if (screen != null) ResetForScene();
                return;
            }
            installer.Tick();
        }

        private RectTransform BuildScreen(RectTransform rootRect, float height)
        {
            view = new WeatherEnvView(rootRect, MfdSlots.Weather, Width, height);
            console = view.Console;
            console.PageChanged += _ => nextRefresh = 0f;
            view.Finish();
            console.Ticker.Add(-1, AvTickRate.Fast, TickRefresh);
            return null;
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
                    Footer = "NO MISSION",
                    FooterState = AvState.Inert,
                });
                return;
            }

            float cond = weather.CurrentConditions;
            RegimeSnapshot regime = weather.CurrentRegime;
            WeatherField localField = weather.Field;
            WeatherPoint localPoint = weather.LocalWeather;
            bool built = localField != null && localField.IsBuilt;
            if (built)
                regime = RegimeSnapshot.FromType(localField.Key.SkyPin < SkyLibrary.Count
                    ? SkyLibrary.ClassOf(localField.Key.SkyPin) // a pinned sky shows its nominal class, never the jittered instance's sampled one
                    : SkyMath.ClassOf(localField.RowAt(weather.FieldPosition.x, weather.FieldPosition.y)));
            // A pinned sky shows its own name (spec 8.4); the class code and briefing stay the regime's.
            string skyName = built && localField.Key.SkyPin < SkyLibrary.Count ? SkyLibrary.Get(localField.Key.SkyPin).Name : regime.Name;
            float deck = built ? localPoint.CloudBase : weather.CurrentCloudHeight;
            float cloudTop = built ? localPoint.CloudTop : deck + 1500f;
            cloudTop = Mathf.Max(deck + 500f, cloudTop);

            Domain.WeatherForecast.FormatWind(weather.CurrentWindVelocity.x, weather.CurrentWindVelocity.z,
                out float kts, out int towards, out int from);

            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            float cameraAltitude = camera != null ? (float)camera.transform.GlobalPosition().y : 0f;
            bool hasViewAir = weather.TryGet(out var viewAir);
            SolarData solar = weather.GetSolarData();
            LunarData lunar = weather.GetLunarData();

            // The tape is held until slice 5: there is no step to count down to, so the NEXT cell reads HELD and the footer says whose hold it is.
            string nextText = "HELD";
            float nextFrac = 0f;
            string footer = weather.IsManualOverride ? "HELD // CTRL+O // " + regime.Name : "HELD // TAPE";

            var data = new EnvData
            {
                Title = "METOC / BATTLEFIELD",
                Next = nextText,
                NextFrac = nextFrac,
                Footer = footer,
                FooterState = AvState.Info,
                Regime = regime.Type,
                Code = regime.Code,
                Name = skyName,
                Metar = built ? WeatherMetar.Line(localPoint) : "",
                Briefing = regime.TacticalBriefing,
                Cover = built ? localPoint.Cover : cond,
                Deck = deck,
                Top = cloudTop,
                VisibilityKm = built ? localPoint.VisibilityKm : -1f,
                Rain = weather.LocalRainIntensity,
                Turbulence = weather.CurrentTurbulence,
                WindKts = kts,
                WindFrom = from,
                WindTo = towards,
                HasCamera = camera != null,
                CameraAlt = cameraAltitude,
                AirDensity = camera != null ? LevelInfo.GetAirDensity(cameraAltitude) : 0f,
                SoundSpeed = camera != null ? LevelInfo.GetSpeedOfSound(cameraAltitude) : 0f,
                HasViewAir = hasViewAir,
                ViewCloud = hasViewAir ? viewAir.CloudDensity01 : 0f,
                ViewRain = hasViewAir ? viewAir.Precipitation01 : 0f,
                ViewMoisture = hasViewAir ? viewAir.Condensation01 : 0f,
                ViewTemperature = hasViewAir ? viewAir.TemperatureC : 0f,
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

            for (int i = 0; i < densityCurve.Length; i++) densityCurve[i] = LevelInfo.GetAirDensity(i * 1000f);
            data.DensityByAlt = densityCurve;

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
