using BepInEx.Configuration;

namespace BoscaliSummer.Features.Progression.Configuration
{
    internal sealed class ProgressionSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<int> ScorePerPoint { get; }
        public ConfigEntry<int> MaximumPoints { get; }

        public ProgressionSettings(ConfigFile config)
        {
            Enabled = config.Bind("Progression", "Enabled", true,
                "Enable the session-scoped Boscali perk board. Disabled skips the whole feature, " +
                "including its Harmony patches and network handlers.");
            ScorePerPoint = config.Bind("Progression", "ScorePerPoint", 500,
                new ConfigDescription(
                    "Mission score needed for each perk point. Score is the vanilla per-player score; " +
                    "vanilla rank thresholds and unlocks are never modified.",
                    new AcceptableValueRange<int>(50, 10000)));
            MaximumPoints = config.Bind("Progression", "MaximumPoints", 6,
                new ConfigDescription("Most perk points one player can earn in a mission.",
                    new AcceptableValueRange<int>(1, 20)));
        }
    }
}
