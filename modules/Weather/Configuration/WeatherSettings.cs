using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Configuration
{
    /// <summary>
    /// Configuration settings for the Weather feature, dynamic transitions, and procedural rain.
    /// </summary>
    internal sealed class WeatherSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> DynamicWeatherEnabled { get; }
        public ConfigEntry<float> StateIntervalMinutes { get; }
        public ConfigEntry<float> StateFadeSeconds { get; }
        public ConfigEntry<float> MinConditions { get; }
        public ConfigEntry<float> MaxConditions { get; }
        public ConfigEntry<float> WindVariability { get; }
        public ConfigEntry<float> TurbulenceMultiplier { get; }

        public ConfigEntry<bool> RainVisualsEnabled { get; }
        public ConfigEntry<bool> RainAudioEnabled { get; }
        public ConfigEntry<bool> CinematicCloudsEnabled { get; }
        public ConfigEntry<bool> CloudHalfResolution { get; }
        public ConfigEntry<bool> CloudTemporalUpdate { get; }
        public ConfigEntry<float> RainDensity { get; }
        public ConfigEntry<bool> CanopyRainEnabled { get; }
        public ConfigEntry<bool> CanopyShaderEnabled { get; }
        public ConfigEntry<bool> TerrainRainEnabled { get; }
        public ConfigEntry<bool> RainAtmosphereEnabled { get; }
        public ConfigEntry<bool> LightningEnabled { get; }
        public ConfigEntry<bool> ReducedFlashes { get; }

        public ConfigEntry<KeyCode> ConsoleKey { get; }
        public ConfigEntry<bool> ConsoleKeyRequiresCtrl { get; }

        public WeatherSettings(ConfigFile config)
        {
            const string section = "Weather";

            Enabled = config.Bind(section, "Enabled", true,
                "Enable the Weather module: the ENV bezel screen, native weather transitions and local rain.");

            DynamicWeatherEnabled = config.Bind(section, "DynamicWeatherEnabled", true,
                "Step the weather between static states over mission time (clear, fair, scattered, " +
                "broken, overcast, rain, storm). Off holds the mission's authored weather.");

            // New keys on purpose: the retired Transition* keys stored minutes for the old
            // moving model, and an old stored value would override the new five-minute default.
            StateIntervalMinutes = config.Bind(section, "StateIntervalMinutes", 5.0f,
                new ConfigDescription(
                    "Mission minutes each weather state holds before the next step (hold, one state " +
                    "better or one state worse).",
                    new AcceptableValueRange<float>(1.0f, 30.0f)));

            StateFadeSeconds = config.Bind(section, "StateFadeSeconds", 60.0f,
                new ConfigDescription(
                    "Seconds a state change fades the sky, fog and wind. Storm cells grow more slowly " +
                    "(up to five minutes) so rain and lightning build in.",
                    new AcceptableValueRange<float>(10.0f, 300.0f)));

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
                "Master for local rain-streak particles, canopy rain and rain haze. " +
                "Terrain wetness has a separate switch. Applies without restarting.");

            RainAudioEnabled = config.Bind(section, "RainAudioEnabled", true,
                "Client-local rain rush and canopy patter through the game's effects mixer. Applies now.");

            CinematicCloudsEnabled = config.Bind(section, "CinematicCloudsEnabled", true,
                "Render fly-through volumetric clouds for the current weather state. " +
                "Falls back to native clouds when the shader is unavailable; applies now.");

            CloudHalfResolution = config.Bind(section, "CloudHalfResolution", true,
                "March the volumetric clouds at half resolution and upsample them along scene depth " +
                "(about four times cheaper on the GPU). Off draws every pixel at full resolution. Applies now.");

            CloudTemporalUpdate = config.Bind(section, "CloudTemporalUpdate", true,
                "With half-resolution clouds: march a quarter of the cloud pixels each frame and reproject " +
                "the rest from the last frame (about four times cheaper again). Applies now.");

            RainDensity = config.Bind(section, "RainDensity", 1.0f,
                new ConfigDescription(
                    "Density multiplier for falling rain streaks. The 2500-particle budget stays fixed.",
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
                "Spatial storm lightning and delayed thunder. Client-local presentation; no aircraft physics changes.");
            ReducedFlashes = config.Bind(section, "ReducedFlashes", false,
                "Suppress visible storm bolts and cloud flashes while independently enabled storm audio remains available.");

            RainAtmosphereEnabled = config.Bind(section, "RainAtmosphereEnabled", true,
                "Thicken and grey the haze and dim ambient light under local rain. Layered on the " +
                "game's own sky each frame and restored when the rain stops.");

            ConsoleKey = config.Bind(section, "ConsoleKey", KeyCode.O,
                "Opens the weather console (host: set and hold states, force storm set-pieces, " +
                "re-roll the cloud layout; clients: read-only). Set to None to disable.");

            ConsoleKeyRequiresCtrl = config.Bind(section, "ConsoleKeyRequiresCtrl", true,
                "Require holding Ctrl with the weather console key (Ctrl+O by default).");

        }
    }
}
