using BepInEx.Configuration;

namespace BoscaliSummer.Features.HighCommand.Configuration
{
    /// <summary>
    /// Chain-of-command tuning. The feature is LARP plus economy: cohesion never mutates
    /// vanilla AI, spawn or damage behaviour, it only decides what a faction is paid and
    /// what the staff console tells the player.
    /// </summary>
    internal sealed class HighCommandSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> EconomyEnabled { get; }
        public ConfigEntry<int> StipendIntervalSeconds { get; }
        public ConfigEntry<int> StipendBaseAmount { get; }
        public ConfigEntry<int> MaximumStipends { get; }
        public ConfigEntry<int> BountyBaseFunds { get; }
        public ConfigEntry<int> BountyComponentFunds { get; }
        public ConfigEntry<int> BountyTheaterFunds { get; }
        public ConfigEntry<bool> TransfersEnabled { get; }
        public ConfigEntry<int> TransferMinSeconds { get; }
        public ConfigEntry<int> TransferMaxSeconds { get; }
        public ConfigEntry<int> DisruptionSeconds { get; }
        public ConfigEntry<int> PostRespawnSeconds { get; }
        public ConfigEntry<bool> MapMarkersEnabled { get; }

        public HighCommandSettings(ConfigFile config)
        {
            const string section = "HighCommand";
            Enabled = config.Bind(section, "Enabled", true,
                "Run the chain-of-command layer: generated staff, command posts, VIP convoys, bounties and stipends.");
            EconomyEnabled = config.Bind(section, "EconomyEnabled", true,
                "Pay survival stipends and kill bounties through the vanilla faction account. Off keeps the page informational.");
            StipendIntervalSeconds = config.Bind(section, "StipendIntervalSeconds", 120,
                new ConfigDescription("Seconds between survival stipend payments. 0 disables the stipend.",
                    new AcceptableValueRange<int>(0, 600)));
            StipendBaseAmount = config.Bind(section, "StipendBaseAmount", 200,
                new ConfigDescription("Funds paid per live command weight unit per stipend, scaled by cohesion.",
                    new AcceptableValueRange<int>(0, 2000)));
            MaximumStipends = config.Bind(section, "MaximumStipends", 10,
                new ConfigDescription("Stipends one faction can collect in a single mission.",
                    new AcceptableValueRange<int>(0, 40)));
            BountyBaseFunds = config.Bind(section, "BountyBaseFunds", 1500,
                new ConfigDescription("Funds for killing an enemy base commander.",
                    new AcceptableValueRange<int>(0, 20000)));
            BountyComponentFunds = config.Bind(section, "BountyComponentFunds", 3000,
                new ConfigDescription("Funds for killing an enemy component commander.",
                    new AcceptableValueRange<int>(0, 30000)));
            BountyTheaterFunds = config.Bind(section, "BountyTheaterFunds", 6000,
                new ConfigDescription("Funds for killing an enemy theater commander.",
                    new AcceptableValueRange<int>(0, 60000)));
            TransfersEnabled = config.Bind(section, "TransfersEnabled", true,
                "Commanders occasionally travel between friendly bases in ground convoys. Intercepting the lead vehicle kills them.");
            TransferMinSeconds = config.Bind(section, "TransferMinSeconds", 180,
                new ConfigDescription("Minimum seconds between automatic VIP transfers for one faction.",
                    new AcceptableValueRange<int>(30, 1800)));
            TransferMaxSeconds = config.Bind(section, "TransferMaxSeconds", 420,
                new ConfigDescription("Maximum seconds between automatic VIP transfers for one faction.",
                    new AcceptableValueRange<int>(60, 3600)));
            DisruptionSeconds = config.Bind(section, "DisruptionSeconds", 120,
                new ConfigDescription("How long a successor runs the post at reduced cohesion after a commander dies.",
                    new AcceptableValueRange<int>(0, 600)));
            PostRespawnSeconds = config.Bind(section, "PostRespawnSeconds", 60,
                new ConfigDescription("Delay before a killed commander's post is re-established.",
                    new AcceptableValueRange<int>(10, 600)));
            MapMarkersEnabled = config.Bind(section, "MapMarkersEnabled", true,
                "Draw your posts and confirmed enemy posts as diamonds on the map. Off hides the layer only; the page still lists every post.");
        }
    }
}
