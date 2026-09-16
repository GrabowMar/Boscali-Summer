using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Configuration
{
    /// <summary>
    /// The schedule itself is deliberately not configurable: every peer derives the same sky
    /// from the mission identity, so a host-side knob would desync an unmodified client's
    /// forecast. Only the local presentation and the debug tools are settings.
    /// </summary>
    internal sealed class WeatherSettings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<int> ForecastSteps;
        public readonly ConfigEntry<float> ForecastStepMinutes;
        public readonly ConfigEntry<bool> DebugControls;
        public readonly ConfigEntry<KeyCode> DebugKey;
        public readonly ConfigEntry<bool> DebugKeyRequiresCtrl;
        public readonly ConfigEntry<bool> RainEffects;
        public readonly ConfigEntry<bool> RainOnCanopy;
        public readonly ConfigEntry<bool> RainAudio;
        public readonly ConfigEntry<float> RainEffectDensity;
        public readonly ConfigEntry<bool> Hud;
        public readonly ConfigEntry<bool> Supercells;
        public readonly ConfigEntry<float> SupercellDetail;
        public readonly ConfigEntry<int> RadarRangeKm;
        public readonly ConfigEntry<bool> ReplaceVanillaClouds;
        public readonly ConfigEntry<float> CloudSortFudge;

        public WeatherSettings(ConfigFile config)
        {
            Enabled = config.Bind("Weather", "Enabled", true,
                "Drive the mission's sky from the dynamic weather schedule and add the WEA " +
                "environment panel. The host owns the weather for everyone; a client with this " +
                "off still flies through the host's weather, it just hides the panel and the " +
                "debug tools.");

            ForecastSteps = config.Bind("Weather", "ForecastSteps", 8,
                new ConfigDescription(
                    "How many forecast entries the WEA panel lists. Client-local; it changes " +
                    "nothing in the world.",
                    new AcceptableValueRange<int>(2, 12)));

            ForecastStepMinutes = config.Bind("Weather", "ForecastStepMinutes", 3f,
                new ConfigDescription(
                    "Minutes between forecast entries. Client-local.",
                    new AcceptableValueRange<float>(0.5f, 30f)));

            DebugControls = config.Bind("Weather", "DebugControls", false,
                "Show the weather debug overlay. Only the host can change the weather; on a " +
                "client the overlay is read-only.");

            DebugKey = config.Bind("Weather", "DebugKey", KeyCode.F11,
                "Key that shows and hides the weather debug overlay.");

            DebugKeyRequiresCtrl = config.Bind("Weather", "DebugKeyRequiresCtrl", true,
                "Require Ctrl to be held with the debug key, so the overlay cannot be opened " +
                "by accident during a flight.");

            RainEffects = config.Bind("Weather", "RainEffects", true,
                "Render falling rain when you are inside a storm cell.");

            RainOnCanopy = config.Bind("Weather", "RainOnCanopy", true,
                "Draw rain streaking across the cockpit canopy.");

            RainAudio = config.Bind("Weather", "RainAudio", true,
                "Synthesised rain and storm wind audio. The game has no rain sounds; these are " +
                "generated in memory, never loaded from disk.");

            RainEffectDensity = config.Bind("Weather", "RainEffectDensity", 1f,
                new ConfigDescription(
                    "Scales rain particle counts and canopy overlay strength. 0 turns the " +
                    "visuals off.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Hud = config.Bind("Weather", "Hud", true,
                "Show the cockpit weather HUD: warning tier, storm range and bearing, wind and " +
                "cloud base.");

            Supercells = config.Bind("Weather", "Supercells", true,
                "Render storm cell cloud towers and anvils. The host owns the sky; this only " +
                "changes what you see.");

            SupercellDetail = config.Bind("Weather", "SupercellDetail", 0.6f,
                new ConfigDescription(
                    "Scales how many cloud puffs each storm tower emits. Lower it if a " +
                    "supercell costs you frames.",
                    new AcceptableValueRange<float>(0f, 1f)));

            RadarRangeKm = config.Bind("Weather", "RadarRangeKm", 40,
                new ConfigDescription(
                    "Initial range of the WEA radar scope in kilometres. Cycled on the scope " +
                    "itself.",
                    new AcceptableValueRange<int>(10, 200)));

            ReplaceVanillaClouds = config.Bind("Weather", "ReplaceVanillaClouds", false,
                "Take the vanilla local cloud deck away so the storm cells own the sky: the " +
                "masked cloud plane and the near puff layer stop drawing, while vanilla keeps " +
                "driving the sun and moon cloud cookies, the cloud occlusion, the fog and the " +
                "distant horizon band. Off, you fly under the vanilla deck with the cells " +
                "towering through it. Compare both before settling: this one is unverified.");

            CloudSortFudge = config.Bind("Weather", "CloudSortFudge", -100f,
                new ConfigDescription(
                    "Sort bias between a storm cell and the vanilla cloud deck. Unity draws " +
                    "LOWER values in front, so a negative number puts the cell in front of the " +
                    "deck and a positive one buries it behind. Raise it towards 0 if the cells " +
                    "look too flat against the deck; push it negative if a storm disappears " +
                    "behind the base layer.",
                    new AcceptableValueRange<float>(-5000f, 5000f)));
        }
    }
}
