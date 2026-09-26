using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Configuration
{
    internal enum RainQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>
    /// Weather settings. Two kinds live here and the difference matters:
    /// <list type="bullet">
    /// <item>presentation (rain look, canopy, sound, HUD, radar, forecast) is client-local — every
    /// player tunes their own screen;</item>
    /// <item>the sky's rules (dynamic weather, starting regime, the three gameplay switches) are
    /// host-authoritative: the host's values travel in the weather key, a client's are ignored in
    /// multiplayer.</item>
    /// </list>
    /// The schedule itself is not configurable: every peer must derive the same sky.
    /// </summary>
    internal sealed class WeatherSettings
    {
        public ConfigEntry<bool> Enabled { get; }

        // Presentation (client-local).
        public ConfigEntry<RainQuality> Quality { get; }
        public ConfigEntry<bool> RainOnCanopy { get; }
        public ConfigEntry<bool> RainAudio { get; }
        public ConfigEntry<float> RainVolume { get; }
        public ConfigEntry<bool> Hud { get; }
        public ConfigEntry<float> RadarRangeKm { get; }
        public ConfigEntry<int> ForecastSteps { get; }
        public ConfigEntry<int> ForecastStepMinutes { get; }

        // Host rules (travel in the key).
        public ConfigEntry<bool> DynamicWeather { get; }
        public ConfigEntry<int> StartRegime { get; }
        public ConfigEntry<bool> StormTurbulence { get; }
        public ConfigEntry<bool> SensorEffects { get; }
        public ConfigEntry<bool> LightningHazard { get; }

        // Debug.
        public ConfigEntry<bool> DebugControls { get; }
        public ConfigEntry<KeyCode> DebugKey { get; }
        public ConfigEntry<bool> DebugKeyRequiresCtrl { get; }
        public ConfigEntry<float> DebugRainPreview { get; }

        public WeatherSettings(ConfigFile config)
        {
            const string section = "Weather";
            Enabled = config.Bind(section, "Enabled", true,
                "Dynamic weather: a deterministic sky of regimes, fronts and storm cells that " +
                "places vanilla's clouds, rain, lightning and wind, the WEA screen with its radar, " +
                "and the weather HUD line. The host decides the sky; every peer derives it.");

            Quality = config.Bind(section, "RainQuality", RainQuality.High,
                "How many rain drops are drawn around the aircraft and on the canopy. " +
                "Client-local; lower it if heavy rain costs frames.");
            RainOnCanopy = config.Bind(section, "RainOnCanopy", true,
                "Rain drops that bead, run and blow off the canopy glass. Client-local.");
            RainAudio = config.Bind(section, "RainAudio", true,
                "Rain, canopy-impact and thunder sound. Client-local.");
            RainVolume = config.Bind(section, "RainVolume", 1f,
                new ConfigDescription("Scales the rain and thunder mix. Client-local.",
                    new AcceptableValueRange<float>(0f, 2f)));
            Hud = config.Bind(section, "Hud", true,
                "One weather line on the HUD board: nearby cells, heavy rain, turbulence, " +
                "lightning, hail. Client-local.");
            RadarRangeKm = config.Bind(section, "RadarRangeKm", 40f,
                new ConfigDescription("Half-height of the WEA radar picture, in km. Client-local.",
                    new AcceptableValueRange<float>(10f, 200f)));
            ForecastSteps = config.Bind(section, "ForecastSteps", 8,
                new ConfigDescription("Forecast rows on the WEA screen. Client-local.",
                    new AcceptableValueRange<int>(2, 12)));
            ForecastStepMinutes = config.Bind(section, "ForecastStepMinutes", 5,
                new ConfigDescription("Minutes between forecast rows. Client-local.",
                    new AcceptableValueRange<int>(1, 30)));

            DynamicWeather = config.Bind(section, "DynamicWeather", true,
                "The sky moves through regimes on its own. Off holds the starting regime. " +
                "Host-authoritative.");
            StartRegime = config.Bind(section, "StartRegime", 0,
                new ConfigDescription(
                    "Sky a mission opens with: 0 = automatic (seeded, usually flyable), 1 CLEAR, " +
                    "2 FAIR, 3 SHOWERS, 4 OVERCAST, 5 FRONTAL, 6 STORMS, 7 SEVERE. Host-authoritative.",
                    new AcceptableValueRange<int>(0, 7)));
            StormTurbulence = config.Bind(section, "StormTurbulence", true,
                "Updrafts, downdrafts, gust fronts and turbulence near storm cells push aircraft " +
                "around. Host-authoritative.");
            SensorEffects = config.Bind(section, "SensorEffects", true,
                "Rain and haze limit how far units can spot each other visually, and heavy rain " +
                "makes infrared seekers easier to decoy. Host-authoritative.");
            LightningHazard = config.Bind(section, "LightningHazard", true,
                "A rare lightning strike on an aircraft deep in a mature storm core: a flash and " +
                "a brief instrument flicker. Never damages. Host-authoritative.");

            DebugControls = config.Bind(section, "DebugControls", false,
                "Show the weather debug window hotkey (host only can change the sky).");
            DebugKey = config.Bind(section, "DebugKey", KeyCode.F11, "Weather debug window key.");
            DebugKeyRequiresCtrl = config.Bind(section, "DebugKeyRequiresCtrl", true,
                "The debug key needs Ctrl held.");
            DebugRainPreview = config.Bind(section, "DebugRainPreview", -1f,
                new ConfigDescription(
                    "Debug: when 0 or more, draw this rain rate (mm/h) at the camera regardless of the " +
                    "sky. Presentation only and client-local; -1 is off.",
                    new AcceptableValueRange<float>(-1f, 150f)));
        }
    }
}
