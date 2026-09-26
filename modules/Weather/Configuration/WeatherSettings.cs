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
    /// Configuration settings for the Weather feature, dynamic transitions, and procedural rain.
    /// </summary>
    internal sealed class WeatherSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> DynamicWeatherEnabled { get; }
        public ConfigEntry<float> TransitionIntervalMinutes { get; }
        public ConfigEntry<float> TransitionDurationMinutes { get; }
        public ConfigEntry<float> MinConditions { get; }
        public ConfigEntry<float> MaxConditions { get; }
        public ConfigEntry<float> WindVariability { get; }
        public ConfigEntry<float> TurbulenceMultiplier { get; }

        public ConfigEntry<bool> RainVisualsEnabled { get; }
        public ConfigEntry<bool> RainAudioEnabled { get; }
        public ConfigEntry<float> RainVolume { get; }
        public ConfigEntry<float> RainDensity { get; }
        public ConfigEntry<bool> CanopyRainEnabled { get; }
        public ConfigEntry<bool> CanopyShaderEnabled { get; }
        public ConfigEntry<bool> TerrainRainEnabled { get; }
        public ConfigEntry<bool> RainAtmosphereEnabled { get; }
        public ConfigEntry<bool> LightningEnabled { get; }

        public ConfigEntry<bool> DebugControlsEnabled { get; }
        public ConfigEntry<KeyCode> DebugKey { get; }
        public ConfigEntry<bool> DebugKeyRequiresCtrl { get; }

        // Synoptic sky (deterministic field, host key).
        public ConfigEntry<RainQuality> Quality { get; }
        public ConfigEntry<bool> RainOnCanopy { get; }
        public ConfigEntry<bool> RainAudio { get; }
        public ConfigEntry<bool> Hud { get; }
        public ConfigEntry<float> RadarRangeKm { get; }
        public ConfigEntry<int> ForecastSteps { get; }
        public ConfigEntry<int> ForecastStepMinutes { get; }
        public ConfigEntry<bool> DynamicWeather { get; }
        public ConfigEntry<int> StartRegime { get; }
        public ConfigEntry<bool> StormTurbulence { get; }
        public ConfigEntry<bool> SensorEffects { get; }
        public ConfigEntry<bool> LightningHazard { get; }
        public ConfigEntry<bool> DebugControls { get; }
        public ConfigEntry<float> DebugRainPreview { get; }

        public WeatherSettings(ConfigFile config)
        {
            const string section = "Weather";

            Enabled = config.Bind(section, "Enabled", true,
                "Enable the Weather module: the ENV bezel screen, dynamic weather, and the synoptic sky.");

            DynamicWeatherEnabled = config.Bind(section, "DynamicWeatherEnabled", true,
                "Enable smooth dynamic weather transitions over mission time by gently modulating " +
                "native conditions, cloud ceiling, and wind.");

            TransitionIntervalMinutes = config.Bind(section, "TransitionIntervalMinutes", 4.0f,
                new ConfigDescription(
                    "Average time in mission minutes between weather regime shifts.",
                    new AcceptableValueRange<float>(1.0f, 30.0f)));

            TransitionDurationMinutes = config.Bind(section, "TransitionDurationMinutes", 2.0f,
                new ConfigDescription(
                    "How long a weather transition takes to smoothly blend into the new regime (minutes).",
                    new AcceptableValueRange<float>(0.5f, 10.0f)));

            MinConditions = config.Bind(section, "MinConditions", 0.05f,
                new ConfigDescription(
                    "Minimum weather condition index (0.0 = completely clear).",
                    new AcceptableValueRange<float>(0.0f, 0.40f)));

            MaxConditions = config.Bind(section, "MaxConditions", 0.88f,
                new ConfigDescription(
                    "Maximum weather condition index (0.8+ enables rain and heavy overcast).",
                    new AcceptableValueRange<float>(0.50f, 1.0f)));

            WindVariability = config.Bind(section, "WindVariability", 0.5f,
                new ConfigDescription(
                    "Wind heading variation as weather changes (0 = static, 1 = 45 degrees). Mission wind speed is preserved.",
                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            TurbulenceMultiplier = config.Bind(section, "TurbulenceMultiplier", 1.0f,
                new ConfigDescription(
                    "Multiplier for the mission baseline turbulence; rain presets never amplify it automatically.",
                    new AcceptableValueRange<float>(0.1f, 3.0f)));

            RainVisualsEnabled = config.Bind(section, "RainVisualsEnabled", true,
                "Enable 100% procedural falling rain particle streaks around the aircraft.");

            RainAudioEnabled = config.Bind(section, "RainAudioEnabled", true,
                "Enable synthesized procedural rain noise and cockpit canopy patter audio.");

            RainVolume = config.Bind(section, "RainVolume", 0.75f,
                new ConfigDescription(
                    "Volume multiplier for synthesized procedural rain and canopy audio.",
                    new AcceptableValueRange<float>(0.0f, 1.5f)));

            RainDensity = config.Bind(section, "RainDensity", 1.0f,
                new ConfigDescription(
                    "Density multiplier for falling rain streaks. The 1000-particle budget stays fixed.",
                    new AcceptableValueRange<float>(0.25f, 2.0f)));

            CanopyRainEnabled = config.Bind(section, "CanopyRainEnabled", true,
                "Enable rain droplets on the local aircraft's canopy glass (falls back silently " +
                "when no canopy glass is found).");

            CanopyShaderEnabled = config.Bind(section, "CanopyShaderEnabled", true,
                "Draw rain and airflow runoff on automatically discovered nearby glass submeshes, " +
                "preserving original glass materials. Requires CanopyRainEnabled. Residual drops dry with airspeed. " +
                "Fallback particles require readable glass geometry; otherwise canopy effects stay off.");

            TerrainRainEnabled = config.Bind(section, "TerrainRainEnabled", true,
                "Wet darkening, puddles and sun glints on nearby native terrain, with gradual drying. " +
                "At most eight visible terrain submeshes receive an extra texture-free pass.");

            LightningEnabled = config.Bind(section, "LightningEnabled", true,
                "Lightning flashes and delayed thunder in heavy rain. The flash pulses the vanilla " +
                "sun and is restored exactly when it ends.");

            RainAtmosphereEnabled = config.Bind(section, "RainAtmosphereEnabled", true,
                "Thicken and grey the haze and dim ambient light under local rain. Layered on the " +
                "game's own sky each frame and restored when the rain stops.");

            DebugControlsEnabled = config.Bind(section, "DebugControlsEnabled", true,
                "Enable in-game debug weather shortcuts. The ENV panel remains read-only.");

            DebugKey = config.Bind(section, "DebugKey", KeyCode.F11,
                "Keyboard hotkey to cycle weather regimes (Shift + key cycles rain mode) and open the synoptic debug window. Set to None to disable.");

            DebugKeyRequiresCtrl = config.Bind(section, "DebugKeyRequiresCtrl", false,
                "Require holding Ctrl when pressing the debug weather hotkey.");

            // Synoptic sky: presentation is client-local, the sky rules are host-authoritative.
            Quality = config.Bind(section, "RainQuality", RainQuality.High,
                "How many rain drops are drawn around the aircraft and on the canopy. " +
                "Client-local; lower it if heavy rain costs frames.");
            RainOnCanopy = config.Bind(section, "RainOnCanopy", true,
                "Rain drops that bead, run and blow off the canopy glass. Client-local.");
            RainAudio = config.Bind(section, "RainAudio", true,
                "Rain, canopy-impact and thunder sound. Client-local.");
            Hud = config.Bind(section, "Hud", true,
                "One weather line on the HUD board: nearby cells, heavy rain, turbulence, " +
                "lightning, hail. Client-local.");
            RadarRangeKm = config.Bind(section, "RadarRangeKm", 40f,
                new ConfigDescription("Half-height of the WEA radar picture, in km. Client-local.",
                    new AcceptableValueRange<float>(10f, 200f)));
            ForecastSteps = config.Bind(section, "ForecastSteps", 8,
                new ConfigDescription("Forecast rows on the synoptic page. Client-local.",
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
            DebugRainPreview = config.Bind(section, "DebugRainPreview", -1f,
                new ConfigDescription(
                    "Debug: when 0 or more, draw this rain rate (mm/h) at the camera regardless of the " +
                    "sky. Presentation only and client-local; -1 is off.",
                    new AcceptableValueRange<float>(-1f, 150f)));
        }
    }
}
