using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Progression.Configuration
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
        public ConfigEntry<Runtime.EmblemPaint> EmblemPaint { get; }
        public ConfigEntry<string> PilotProfile { get; }

        public ProgressionSettings(ConfigFile config)
        {
            Enabled = config.Bind("Progression", "Enabled", true,
                "Enable the session-scoped Boscali perk board. Turning this off skips the whole " +
                "feature - its Harmony patches, network handlers and the SQD page - and also " +
                "disables Support, which depends on it. " +
                "Host-authoritative: on a server, only the host's value applies.");
            ScorePerPoint = config.Bind("Progression", "ScorePerPoint", 250,
                new ConfigDescription(
                    "Score for the first OPS skill pick. Pick n costs n x this, so picks get " +
                    "longer as they get better: 250 gives the first pick in the first minutes and " +
                    "five picks (one lane from grade 1 to its grade-6 capstone) for 3750 score, " +
                    "1000 makes every pick a long-mission reward. This reads the vanilla per-player score; " +
                    "Nuclear Option's rank thresholds, aircraft requirements and weapon access " +
                    "are never modified. Host-authoritative: on a server, only the host's value " +
                    "applies.",
                    new AcceptableValueRange<int>(50, 10000)));
            MaximumPoints = config.Bind("Progression", "MaximumPoints", 7,
                new ConfigDescription(
                    "Most picks one player can earn in a mission. Career picks per pilot life; " +
                    "spend them on SPACE, CYBER or SOF skills. Grade 1 of each is free and " +
                    "every other grade costs one pick, so score's six picks take one lane to its " +
                    "capstone with one to spare. Ace bonus picks go on top up to this ceiling. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<int>(1, 20)));
            PerkStrength = config.Bind("Progression", "PerkStrength", 1f,
                new ConfigDescription(
                    "Retired with the flying passives (the board is OPS skills now); has no effect. " +
                    "Kept so the host-authority settings list keeps its shape.",
                    new AcceptableValueRange<float>(0f, 2f)));

            SquadronName = config.Bind("Squadron", "Name", "BOSCALI SUMMER",
                "Local squadron name shown on the SQD pilot dossier. Cosmetic and client-local; " +
                "it is never sent to other players.");
            Emblem = config.Bind("Squadron", "Emblem", Runtime.EmblemDesign.DefaultText,
                "Local generated insignia, encoded as shape.division.charge.accent.field.second.ink.number " +
                "(the older shape.charge.palette form is still read). Design it in PILOT's STUDIO tab. " +
                "Cosmetic and client-local.");
            EmblemFile = config.Bind("Squadron", "EmblemFile", "",
                "Optional PNG path used instead of the procedural emblem. Only files inside " +
                "BepInEx/config/BoscaliSummer/Emblems are offered; the mod never downloads or " +
                "bundles art. Cosmetic and client-local.");
            EmblemPaint = config.Bind("Squadron", "EmblemPaint", Runtime.EmblemPaint.Colour,
                "Paint insignia on tail fins: your own on your aircraft and your wingmen, and each " +
                "enemy ace wing's crest on that wing. Off, Colour, or LowVis (two greys). A marking is " +
                "baked once into a copy of the livery texture, which is then packed to the livery's " +
                "own format, so it costs nothing per frame and about as much memory as one more " +
                "livery. Cosmetic and client-local.");
            PilotProfile = config.Bind("Squadron", "PilotProfile", "",
                "Callsign of a Wing Command custom pilot used as your local SQD profile " +
                "(name, callsign, background and portrait). Leave blank to show the " +
                "host-generated squadron record.");
        }
    }
}
