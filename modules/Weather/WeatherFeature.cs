using System;
using BoscaliSummer.Features.Weather.Networking;
using BoscaliSummer.Features.Weather.Presentation;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Weather
{
    /// <summary>
    /// Dynamic weather and environment: smooth regime transitions with procedural rain,
    /// canopy water, terrain wetting and lightning, plus a deterministic synoptic field of
    /// regimes, fronts and storm cells derived on every peer from the host's weather key.
    /// The host drives vanilla's synced sky channels; the ENV bezel screen briefs both.
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
            WeatherKeyNet keyNetwork = context.AddComponent<WeatherKeyNet>();
            WeatherManager manager = context.AddSceneService<WeatherManager>(64);
            SynopticWeather synoptic = context.AddSceneService<SynopticWeather>(66);
            manager.Configure(context.Settings.Weather, network, synoptic, context.Logger);
            synoptic.Configure(context.Settings.Weather, keyNetwork, context.Logger);
            network.Configure(manager);

            WeatherMfdPanel panel = context.AddSceneService<WeatherMfdPanel>(65);
            panel.Configure(context.Settings.Weather, manager, synoptic, context.Logger);

            WeatherDebugOverlay debug = context.AddSceneService<WeatherDebugOverlay>(68);
            debug.Configure(context.Settings.Weather, synoptic);

            WeatherHud hud = context.AddSceneService<WeatherHud>(69);
            hud.Configure(context.Settings.Weather, synoptic, context.Logger);

            // Everything below only draws or sounds; none of it writes weather state.
            RainField rain = context.AddSceneService<RainField>(74);
            rain.Configure(context.Settings.Weather, synoptic, context.Logger);

            context.AddHostSettings(new HostSettingsTable("WEATHER & ENVIRONMENT")
                .Toggle(1, context.Settings.Weather.DynamicWeatherEnabled, "DYNAMIC WEATHER",
                    "Allow the server to smoothly shift weather regimes and ceilings over mission time.")
                .Number(2, context.Settings.Weather.TransitionIntervalMinutes, "TRANSITION GAP",
                    "Average mission minutes between weather transitions.",
                    1.0f, v => $"{v:F0} MIN")
                .Number(3, context.Settings.Weather.WindVariability, "WIND VARIABILITY",
                    "How strongly wind shifts direction during weather transitions.",
                    0.1f, v => v.ToString("P0")));

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
