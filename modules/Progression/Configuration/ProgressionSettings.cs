using BepInEx.Configuration;

namespace BoscaliSummer.Features.Progression.Configuration
{
    internal sealed class ProgressionSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<int> ScorePerPoint { get; }
        public ConfigEntry<int> MaximumPoints { get; }
        public ConfigEntry<float> PerkStrength { get; }

        public ProgressionSettings(ConfigFile config)
        {
            Enabled = config.Bind("Progression", "Enabled", true,
                "Enable the session-scoped Boscali perk board. Turning this off skips the whole " +
                "feature - its Harmony patches, network handlers and the OPS page - and also " +
                "disables Support, which depends on it. " +
                "Host-authoritative: on a server, only the host's value applies.");
            ScorePerPoint = config.Bind("Progression", "ScorePerPoint", 500,
                new ConfigDescription(
                    "Mission score earned per perk point. Lower is a faster board: 250 gives a " +
                    "full board in a busy sortie, 1000 makes the last perks a long-mission " +
                    "reward. This reads the vanilla per-player score; Nuclear Option's rank " +
                    "thresholds, aircraft requirements and weapon access are never modified. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<int>(50, 10000)));
            MaximumPoints = config.Bind("Progression", "MaximumPoints", 6,
                new ConfigDescription(
                    "Most perk points one player can earn in a mission. The board holds nine " +
                    "perks costing twelve points in total, so this is the real balance dial: " +
                    "6 forces a specialisation, 12 lets one pilot take everything. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<int>(1, 20)));
            PerkStrength = config.Bind("Progression", "PerkStrength", 1f,
                new ConfigDescription(
                    "Scales how strong every passive perk is, without editing the board. 1.0 is " +
                    "the shipped balance (for example 8% lower fuel burn, +15% combat pay); 0.5 " +
                    "halves each bonus, 0 makes passives cosmetic, 2.0 doubles them. Perks that " +
                    "authorise a support action are unaffected - they are on or off. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<float>(0f, 2f)));
        }
    }
}
