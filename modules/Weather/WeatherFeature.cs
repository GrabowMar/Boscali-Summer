using System;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Features.Weather.Presentation;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Weather
{
    /// <summary>
    /// Dynamic weather manipulation and the ENV Avionics MFD bezel screen.
    /// Modulates native LevelInfo properties and replicates state smoothly in multiplayer.
    /// </summary>
    internal sealed class WeatherFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("weather", "Dynamic weather and ENV briefing panel");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            WeatherNet network = context.AddComponent<WeatherNet>();
            WeatherManager manager = context.AddSceneService<WeatherManager>(64);
            manager.Configure(context.Settings.Weather, network, context.Logger);
            network.Configure(manager);

            WeatherMfdPanel panel = context.AddSceneService<WeatherMfdPanel>(65);
            panel.Configure(context.Settings.Weather, manager, context.Logger);

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
