using System;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Features.Weather.Presentation;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Weather
{
    /// <summary>
    /// Dynamic weather: a deterministic field of regimes, fronts and storm cells derived on
    /// every peer from the host's weather key and the synced mission clock. The host drives
    /// vanilla's synced sky channels from it; every peer places vanilla's clouds, draws rain,
    /// canopy water, curtains and lightning from the same field.
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

            WeatherMfdPanel panel = context.AddSceneService<WeatherMfdPanel>(65);
            panel.Configure(context.Settings.Weather, manager);

            WeatherDebugOverlay debug = context.AddSceneService<WeatherDebugOverlay>(66);
            debug.Configure(context.Settings.Weather, manager);

            WeatherHud hud = context.AddSceneService<WeatherHud>(67);
            hud.Configure(context.Settings.Weather, manager, context.Logger);

            // Everything below only draws or sounds; none of it writes weather state.
            RainField rain = context.AddSceneService<RainField>(68);
            rain.Configure(context.Settings.Weather, manager, context.Logger);

            context.AddHostSettings(new HostSettingsTable("WEATHER")
                .Toggle(1, context.Settings.Weather.DynamicWeather, "DYNAMIC WEATHER",
                    "The sky moves through regimes on its own. Off holds the starting regime.")
                .Number(2, context.Settings.Weather.StartRegime, "STARTING SKY",
                    "The regime a mission opens with. AUTO reads the mission's own weather.", 1, StartRegimeText)
                .Toggle(3, context.Settings.Weather.StormTurbulence, "STORM TURBULENCE",
                    "Updrafts, downdrafts, gust fronts and turbulence near storm cells.")
                .Toggle(4, context.Settings.Weather.SensorEffects, "WEATHER VS SENSORS",
                    "Rain and haze limit visual spotting; heavy rain makes IR seekers easier to decoy.")
                .Toggle(5, context.Settings.Weather.LightningHazard, "LIGHTNING HAZARD",
                    "Rare strikes on aircraft deep in a mature core: a flash and an instrument flicker, never damage."));
        }

        private static string StartRegimeText(int value)
        {
            if (value <= 0) return "AUTO";
            return Domain.RegimeTable.Name(Domain.RegimeTable.Clamp(value - 1));
        }
    }
}
