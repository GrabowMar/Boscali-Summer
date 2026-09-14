using BepInEx.Configuration;

namespace BoscaliSummer.Features.Events.Configuration
{
    /// <summary>
    /// World-event rotation tuning. The host owns rotation and broadcasts one active event
    /// at a time; a client's only configuration effect is on what its own panel predicts.
    /// </summary>
    internal sealed class EventsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<int> RotationGapMinSeconds { get; }
        public ConfigEntry<int> RotationGapMaxSeconds { get; }
        public ConfigEntry<float> EffectStrength { get; }
        public ConfigEntry<int> HistoryLength { get; }

        public EventsSettings(ConfigFile config)
        {
            const string section = "Events";
            Enabled = config.Bind(section, "Enabled", true,
                "Run the rotating world-event director: one curated event at a time with its " +
                "real gameplay modifier, shown on the EVN bezel screen. Host-authoritative.");
            RotationGapMinSeconds = config.Bind(section, "RotationGapMinSeconds", 90,
                new ConfigDescription(
                    "Shortest calm period between events.",
                    new AcceptableValueRange<int>(10, 900)));
            RotationGapMaxSeconds = config.Bind(section, "RotationGapMaxSeconds", 240,
                new ConfigDescription(
                    "Longest calm period between events.",
                    new AcceptableValueRange<int>(10, 1800)));
            EffectStrength = config.Bind(section, "EffectStrength", 1f,
                new ConfigDescription(
                    "Scales every event modifier without editing the catalog. 1.0 is the " +
                    "shipped balance, 0.5 halves each effect, 0 makes events flavor only, " +
                    "2.0 doubles each effect. Host-authoritative: on a server, only the " +
                    "host's value applies.",
                    new AcceptableValueRange<float>(0f, 2f)));
            HistoryLength = config.Bind(section, "HistoryLength", 16,
                new ConfigDescription(
                    "How many finished events the EVN screen keeps and shows this mission.",
                    new AcceptableValueRange<int>(0, 16)));
        }
    }
}
