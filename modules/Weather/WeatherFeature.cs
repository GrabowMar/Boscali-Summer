using System;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Features.Weather.Presentation;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Weather
{
    /// <summary>
    /// Dynamic weather and environment: smooth regime transitions with procedural rain,
    /// canopy water, terrain wetting and lightning. The host drives the native sky channels;
    /// the ENV bezel screen reads the same state.
    /// </summary>
    internal sealed class WeatherFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("weather", "Dynamic weather and environment");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            WeatherNet network = context.AddComponent<WeatherNet>();
            WeatherManager manager = context.AddSceneService<WeatherManager>(64);
            manager.Configure(context.Settings.Weather, network, context.Logger);
            manager.RegisterClientEffects(context);
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
                "World-space clouds follow fronts and storm cells, with a storm deck when needed. " +
                "Falls back to native clouds if the shader is unavailable. Client-local; applies now.",
                context.Settings.Weather.CinematicCloudsEnabled);

            context.AddHostSettings(new HostSettingsTable("WEATHER & ENVIRONMENT")
                .Toggle(1, context.Settings.Weather.DynamicWeatherEnabled, "DYNAMIC WEATHER",
                    "Allow the server to smoothly shift weather regimes and ceilings over mission time.")
                .Number(2, context.Settings.Weather.TransitionIntervalMinutes, "TRANSITION GAP",
                    "Average mission minutes between weather transitions.",
                    1.0f, v => $"{v:F0} MIN")
                .Number(3, context.Settings.Weather.WindVariability, "WIND VARIABILITY",
                    "How strongly wind shifts direction during weather transitions.",
                    0.1f, v => v.ToString("P0")));
        }
    }
}
