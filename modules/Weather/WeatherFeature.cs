using System;
using BoscaliSummer.Features.Weather.Presentation;
using BoscaliSummer.Features.Weather.Runtime;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Weather
{
    /// <summary>
    /// Dynamic weather: a deterministic front schedule the host drives the vanilla sky from,
    /// a deterministic storm field that gives that weather a place in the world, the supercell
    /// and rain renderers that read it, the <c>WEA</c> environment/radar screen, the cockpit
    /// weather HUD, and the opt-in debug overlay.
    ///
    /// No Harmony patches. Every value this module changes goes through a public
    /// <c>LevelInfo</c> setter into an existing Mirage sync var, and everything spatial is
    /// derived from the mission identity and the mission clock, so vanilla already carries it
    /// all and late joiners are immediately correct.
    /// </summary>
    internal sealed class WeatherFeature : IModFeature
    {
        private static readonly FeatureMetadata Feature =
            new FeatureMetadata("weather", "Dynamic weather and environment");

        public FeatureMetadata Metadata => Feature;

        public Type[] PatchTypes => Array.Empty<Type>();

        public void Install(FeatureContext context)
        {
            WeatherManager manager = context.AddSceneService<WeatherManager>(64);
            manager.Configure(context.Settings.Weather, context.Logger);

            WeatherMfdPanel panel = context.AddSceneService<WeatherMfdPanel>(65);
            panel.Configure(context.Settings.Weather, manager);

            WeatherDebugOverlay overlay = context.AddSceneService<WeatherDebugOverlay>(66);
            overlay.Configure(context.Settings.Weather, manager);

            // Every renderer below only draws. None of them writes weather state or networking.
            SupercellRenderer supercells = context.AddSceneService<SupercellRenderer>(67);
            supercells.Configure(context.Settings.Weather, manager, context.Logger);

            WeatherRain rain = context.AddSceneService<WeatherRain>(68);
            rain.Configure(context.Settings.Weather, manager, context.Logger);

            WeatherHud hud = context.AddSceneService<WeatherHud>(69);
            hud.Configure(context.Settings.Weather, manager, context.Logger);
        }
    }
}
