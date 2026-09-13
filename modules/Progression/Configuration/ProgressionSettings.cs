using BepInEx.Configuration;

namespace BoscaliSummer.Features.Progression.Configuration
{
    internal sealed class ProgressionSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<int> ScorePerPoint { get; }
        public ConfigEntry<int> MaximumPoints { get; }
        public ConfigEntry<float> PerkStrength { get; }
        public ConfigEntry<string> SquadronName { get; }
        public ConfigEntry<string> Emblem { get; }
        public ConfigEntry<string> EmblemFile { get; }
        public ConfigEntry<string> PilotProfile { get; }

        public ProgressionSettings(ConfigFile config)
        {
            Enabled = config.Bind("Progression", "Enabled", true,
                "Enable the session-scoped Boscali perk board. Turning this off skips the whole " +
                "feature - its Harmony patches, network handlers and the SQD page - and also " +
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

            SquadronName = config.Bind("Squadron", "Name", "BOSCALI SUMMER",
                "Local squadron name shown on the SQD pilot dossier. Cosmetic and client-local; " +
                "it is never sent to other players.");
            Emblem = config.Bind("Squadron", "Emblem", Runtime.EmblemDesign.DefaultText,
                "Local procedural emblem, encoded as shape.charge.palette. Edit it in SQD's " +
                "STUDIO tab. Cosmetic and client-local.");
            EmblemFile = config.Bind("Squadron", "EmblemFile", "",
                "Optional PNG path used instead of the procedural emblem. Only files inside " +
                "BepInEx/config/BoscaliSummer/Emblems are offered; the mod never downloads or " +
                "bundles art. Cosmetic and client-local.");
            PilotProfile = config.Bind("Squadron", "PilotProfile", "",
                "Callsign of a Wing Command custom pilot used as your local SQD profile " +
                "(name, callsign, background and portrait). Leave blank to show the " +
                "host-generated squadron record.");
        }
    }
}
