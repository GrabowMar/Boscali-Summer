using System;
using BoscaliSummer.Modules.Weather.Networking;
using BoscaliSummer.Modules.Weather.Presentation;
using BoscaliSummer.Modules.Weather.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Weather
{
    /// <summary>
    /// Dynamic weather and environment: smooth regime transitions with procedural rain,
    /// canopy water, terrain wetting and lightning. The host drives the native sky channels;
    /// the ENV bezel screen reads the same state.
    /// </summary>
    internal sealed class WeatherModule : IModule
    {
        private static readonly ModuleMetadata Module =
            new ModuleMetadata("weather", "Dynamic weather and environment");

        public ModuleMetadata Metadata => Module;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(ModuleContext context)
        {
            WeatherNet network = context.AddComponent<WeatherNet>();
            WeatherManager manager = context.AddSceneService<WeatherManager>(64);
            manager.Configure(context.Settings.Weather, network, context.Logger);
            manager.RegisterClientEffects(context);
            context.AddService<IWeatherView>(manager);
            network.Configure(manager);

            WeatherMfdPanel panel = context.AddSceneService<WeatherMfdPanel>(65);
            panel.Configure(context.Settings.Weather, manager, context.Logger);

            context.AddClientSetting("RAIN VISUALS", "RAIN FX MASTER",
                "Stop falling-rain particles, canopy effects and rain haze. Terrain wetness has its own switch. " +
                "Applies now; no mission or game restart.",
                context.Settings.Weather.RainVisualsEnabled);
            context.AddClientSetting("RAIN VISUALS", "CANOPY DROPLETS",
                "Stop canopy droplet simulation, glass draws and fallback particles. " +
                "Requires RAIN FX MASTER to be on. " +
                "Applies now; no mission or game restart.",
                context.Settings.Weather.CanopyRainEnabled);
            context.AddClientSetting("RAIN VISUALS", "TERRAIN WET PASS",
                "Stop the extra nearby-terrain draw passes and surface checks. " +
                "Applies now; no mission or game restart.",
                context.Settings.Weather.TerrainRainEnabled);
            context.AddClientSetting("RAIN AUDIO", "RAIN SOUND",
                "Rain rush and canopy patter through the game's effects volume. Client-local; applies now.",
                context.Settings.Weather.RainAudioEnabled);
            context.AddClientSetting("SKY", "CINEMATIC CLOUDS",
                "Volumetric clouds for the current weather state: fronts, cumulus and storm towers. " +
                "Falls back to native clouds if the shader is unavailable. Client-local; applies now.",
                context.Settings.Weather.CinematicCloudsEnabled);

            WeatherSettings weather = context.Settings.Weather;
            context.AddHostSettings(new HostSettingsTable("WEATHER", HostSettingsPage.Effects)
                .Toggle(1, weather.DynamicWeatherEnabled, "CHANGING WEATHER",
                    "Step the weather one state at a time over mission time. Off holds the mission's weather.")
                .Number(2, weather.StateIntervalMinutes, "STATE INTERVAL",
                    "Mission minutes each weather state holds before the next step.",
                    1.0f, v => $"{v:F0} MIN")
                .Number(3, weather.StateFadeSeconds, "STATE FADE",
                    "Seconds a change fades the sky, fog and wind. Storm cells always grow over a few minutes.",
                    10f, v => $"{v:F0} S")
                .Number(4, weather.MinConditions, "CLEAREST SKY",
                    "Lowest cloud cover the game's own fog and light may fall to.",
                    0.05f, v => v.ToString("P0"))
                .Number(5, weather.MaxConditions, "STORMIEST SKY",
                    "Highest cloud cover the game's own fog and light may reach. Lower it to keep storms flyable.",
                    0.02f, v => v.ToString("P0"))
                .Number(6, weather.WindVariability, "WIND VARIABILITY",
                    "How strongly each weather state freshens or calms the mission wind.",
                    0.1f, v => v.ToString("P0"))
                .Number(7, weather.TurbulenceMultiplier, "TURBULENCE",
                    "Multiplier on each weather state's turbulence over the mission's own.",
                    0.1f, v => $"{v:F1}x"));
        }
    }
}
