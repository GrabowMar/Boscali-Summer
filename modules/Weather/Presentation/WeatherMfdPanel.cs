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
        private readonly EnvData data = new EnvData();
        private readonly EnvData noMission = new EnvData
        {
            HasMission = false,
            Title = "METOC / NO MISSION",
            Footer = "NO MISSION",
            FooterState = AvState.Inert,
        };
        private EnvForecastRow[] forecastRows;


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
            data.Forecast = null;
            forecastRows = null;
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
                view.Apply(noMission);
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
            bool hasBrief = weather.TryBriefing(out EnvBriefing brief);
            string skyName = hasBrief ? brief.SkyName : regime.Name;
            float deck = built ? localPoint.CloudBase : weather.CurrentCloudHeight;
            float cloudTop = built ? localPoint.CloudTop : deck + 1500f;
            cloudTop = Mathf.Max(deck + 500f, cloudTop);
            // Spec 8.2: the profile follows the deck the natives follow (the lowest opaque layer), not the low layer alone.
            if (built) { deck = localPoint.NativeDeck; cloudTop = Mathf.Max(deck + 500f, localPoint.NativeDeckTop); }

            Domain.WeatherForecast.FormatWind(weather.CurrentWindVelocity.x, weather.CurrentWindVelocity.z,
                out float kts, out int towards, out int from);

            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            float cameraAltitude = camera != null ? (float)camera.transform.GlobalPosition().y : 0f;
            bool hasViewAir = weather.TryGet(out var viewAir);
            SolarData solar = weather.GetSolarData();
            LunarData lunar = weather.GetLunarData();

            // Spec 8.4: HELD while the tape does not move, else the distance to the next front ahead. The footer says whose hold it is.
            bool held = !hasBrief || brief.Held;
            string nextText = held ? "HELD" : brief.NextKm >= 0f ? AvNum.Fixed(brief.NextKm, 0) + " KM" : "CLEAR";
            float nextFrac = held || brief.NextKm < 0f ? 0f : Mathf.Clamp01(1f - brief.NextKm / 150f);
            string footer = weather.IsManualOverride ? "HELD // CTRL+O // " + regime.Name : held ? "HELD // TAPE" : "TAPE // " + brief.Story.ToUpperInvariant();

            data.Title = "METOC / BATTLEFIELD";
            data.Next = nextText;
            data.NextFrac = nextFrac;
            data.Footer = footer;
            data.FooterState = AvState.Info;
            data.Regime = regime.Type;
            data.Code = regime.Code;
            data.Name = skyName;
            data.Metar = hasBrief ? brief.Metar : (built ? WeatherMetar.Line(localPoint) : "");
            data.Story = hasBrief ? brief.Story : "";
            data.Role = hasBrief ? brief.Role : "";
            data.NextMarker = hasBrief ? brief.NextMarker : "";
            data.ForecastHeld = hasBrief && brief.Held;
            data.AtPosition = hasBrief && brief.AtPosition;
            data.Briefing = regime.TacticalBriefing;
            data.Cover = built ? localPoint.SkyCover : cond;
            data.Deck = deck;
            data.Top = cloudTop;
            data.VisibilityKm = built ? localPoint.VisibilityKm : -1f;
            data.Rain = weather.LocalRainIntensity;
            data.Turbulence = weather.CurrentTurbulence;
            data.WindKts = kts;
            data.WindFrom = from;
            data.WindTo = towards;
            data.HasCamera = camera != null;
            data.CameraAlt = cameraAltitude;
            data.AirDensity = camera != null ? LevelInfo.GetAirDensity(cameraAltitude) : 0f;
            data.SoundSpeed = camera != null ? LevelInfo.GetSpeedOfSound(cameraAltitude) : 0f;
            data.HasViewAir = hasViewAir;
            data.ViewCloud = hasViewAir ? viewAir.CloudDensity01 : 0f;
            data.ViewRain = hasViewAir ? viewAir.Precipitation01 : 0f;
            data.ViewMoisture = hasViewAir ? viewAir.Condensation01 : 0f;
            data.ViewTemperature = hasViewAir ? viewAir.TemperatureC : 0f;
            data.SunElevation = solar.ElevationDegrees;
            data.SunAzimuth = solar.AzimuthDegrees;
            data.TimeOfDay = level.timeOfDay;
            data.Sunrise = solar.SunriseHour;
            data.Sunset = solar.SunsetHour;
            data.PolarDay = solar.PolarDay;
            data.PolarNight = solar.PolarNight;
            data.SunEvent = solar.NextEventName + " in " + AvNum.Fixed(solar.TimeToNextEventMinutes, 0) + " min.";
            data.MoonPhase = lunar.PhaseName;
            data.MoonLit = lunar.IlluminationFraction;
            data.MoonGlow = lunar.MoonlightIntensity;
            data.MoonWaxing = lunar.PhaseName.StartsWith("Waxing", StringComparison.Ordinal) ||
                             lunar.PhaseName.StartsWith("First", StringComparison.Ordinal);
            data.Moonless = lunar.IsMoonless;

            for (int i = 0; i < densityCurve.Length; i++) densityCurve[i] = LevelInfo.GetAirDensity(i * 1000f);
            data.DensityByAlt = densityCurve;

            ForecastStep[] steps = weather.GetForecastTimeline();
            data.Forecast = null;
            if (steps != null && !(hasBrief && brief.Held))
            {
                int[] offsets = Domain.WeatherForecast.DefaultOffsetsMinutes;
                if (forecastRows == null || forecastRows.Length != steps.Length)
                {
                    forecastRows = new EnvForecastRow[steps.Length];
                    for (int i = 0; i < forecastRows.Length; i++) forecastRows[i] = new EnvForecastRow();
                }
                for (int i = 0; i < steps.Length; i++)
                {
                    EnvForecastRow row = forecastRows[i];
                    row.OffsetMinutes = i < offsets.Length ? offsets[i] : 0;
                    row.Regime = steps[i].Regime.Type;
                    row.Code = steps[i].Regime.Code;
                    row.Cover = steps[i].Conditions;
                    row.Deck = steps[i].CloudDeckMetres;
                    row.Rain = steps[i].RainProbability;
                }
                data.Forecast = forecastRows;
            }

            view.Apply(data);
        }
    }
}
