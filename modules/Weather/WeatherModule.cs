using System;
using BoscaliSummer.Modules.Weather.Networking;
using BoscaliSummer.Modules.Weather.Presentation;
using BoscaliSummer.Modules.Weather.Runtime;
using BoscaliSummer.Modules.Weather.Visuals;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Core.Modules;
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

        public Type[] PatchTypes => new[] { typeof(Patches.CloudSeekerPatch) };

        public void Install(ModuleContext context)
        {
            WeatherNet network = context.AddComponent<WeatherNet>();
            WeatherManager manager = context.AddSceneService<WeatherManager>(64);
            manager.Configure(context.Settings.Weather, network, context.Logger);
            context.AddService<IFlightEnvironmentView>(manager);
            manager.RegisterClientEffects(context);
            context.AddService<IWeatherView>(manager);
            context.AddService<IWeatherDirector>(manager);
            network.Configure(manager);

            WeatherMfdPanel panel = context.AddSceneService<WeatherMfdPanel>(65);
            panel.Configure(context.Settings.Weather, manager, context.Logger);

            OceanDressing ocean = context.AddSceneService<OceanDressing>(66);
            ocean.Configure(context.Settings.Weather, manager, context.Logger);
            context.AddClientEffect(ocean);

            ClientSettingToggle rain = context.AddClientSetting("RAIN", "RAIN FX",
                "Falling rain, canopy effects and rain haze. Terrain wetness has its own switch. Applies now.",
                context.Settings.Weather.RainVisualsEnabled);
            context.AddClientSetting("RAIN", "CANOPY DROPLETS",
                "Droplets beading and running on the canopy glass. Applies now.",
                context.Settings.Weather.CanopyRainEnabled, ClientPreset.Light, rain);
            context.AddClientSetting("RAIN", "TERRAIN WET PASS",
                "Wet, darker ground near you in rain: extra nearby-terrain draw passes. Applies now.",
                context.Settings.Weather.TerrainRainEnabled, ClientPreset.Heavy);
            context.AddClientLevel("RAIN", "RAIN DENSITY", "How many falling-rain streaks you see. The particle budget stays fixed.",
                context.Settings.Weather.RainDensity, .25f, 2f, .25f, requires: rain);
            context.AddClientSetting("RAIN", "RAIN SOUND",
                "Rain, canopy impact and distance-delayed thunder through the game's effects volume. Applies now.",
                context.Settings.Weather.RainAudioEnabled);
            context.AddClientSetting("SKY", "REDUCED FLASHES",
                "Suppress visible lightning and storm flashes; independently enabled storm audio remains available.",
                context.Settings.Weather.ReducedFlashes);
            context.AddClientSetting("SKY", "CINEMATIC CLOUDS",
                "Volumetric clouds for the current weather state: fronts, cumulus and storm towers. " +
                "Falls back to native clouds if the shader is unavailable. Client-local; applies now.",
                context.Settings.Weather.CinematicCloudsEnabled, ClientPreset.Light);
            context.AddClientSetting("SKY", "SUN SHAFTS",
                "Beams of light around the sun, cut by clouds, terrain and aircraft. Client-local; applies now.",
                context.Settings.Weather.SunShaftsEnabled, ClientPreset.Light);
            context.AddClientSetting("SKY", "AURORA & SPRITES",
                "Aurora on clear nights; red sprites and blue jets above distant night storms. Client-local; applies now.",
                context.Settings.Weather.SkyPhenomenaEnabled, ClientPreset.Light);
            context.AddClientSetting("SKY", "DARK NIGHTS",
                "Darker, moonlit blue-grey nights: a new moon is truly dark, a full moon lights the ground. Client-local; applies now.",
                context.Settings.Weather.DarkNightsEnabled);
            context.AddClientSetting("SEA", "OCEAN WAVES",
                "Waves that follow the wind and build under storms, with whitecaps, gale streaks and surf. " +
                "Off restores the game's own water. Client-local; applies now.",
                context.Settings.Weather.OceanWavesEnabled, ClientPreset.Light);
            context.AddClientSetting("SEA", "SEA SPRAY",
                "Spray behind aircraft flying low and fast over water. Client-local; applies now.",
                context.Settings.Weather.SeaSprayEnabled);

            WeatherSettings weather = context.Settings.Weather;
            context.AddHostSettings(new HostSettingsTable("WEATHER", HostSettingsPage.Effects)
                .Toggle(1, weather.TapeMoves, "CHANGING WEATHER",
                    "Weather systems cross the map over mission time. Off holds the sky where it is.")
                .Number(2, weather.Pace, "WEATHER PACE",
                    "How fast weather systems cross the map, 1 slow to 6 fast. A stronger mission wind speeds them up.",
                    1, v => "PACE " + v)
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
