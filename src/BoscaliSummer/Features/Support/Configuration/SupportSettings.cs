using BepInEx.Configuration;

namespace BoscaliSummer.Features.Support.Configuration
{
    internal sealed class SupportSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> VehicleAirdropsEnabled { get; }
        public ConfigEntry<bool> FortificationEnabled { get; }
        public ConfigEntry<bool> ArtilleryEnabled { get; }
        public ConfigEntry<float> VehicleAirdropCost { get; }
        public ConfigEntry<float> FortificationCost { get; }
        public ConfigEntry<float> ArtilleryCost { get; }
        public ConfigEntry<float> RequestCooldown { get; }
        public ConfigEntry<string> VehicleDefinitionKey { get; }
        public ConfigEntry<string> ArtilleryDefinitionKey { get; }

        public SupportSettings(ConfigFile config)
        {
            Enabled = config.Bind("Support", "Enabled", true,
                "Enable the server-authoritative Boscali support menu.");
            VehicleAirdropsEnabled = config.Bind("Support", "VehicleAirdrops", true,
                "Allow unlocked players to requisition native-parachute vehicles.");
            FortificationEnabled = config.Bind("Support", "Fortification", true,
                "Allow unlocked players to reinforce a friendly controlled zone.");
            ArtilleryEnabled = config.Bind("Support", "Artillery", false,
                "Experimental: allow low-yield vanilla ordnance fire missions after in-game verification.");
            VehicleAirdropCost = config.Bind("Support", "VehicleAirdropCost", 12f,
                new ConfigDescription("Allocation cost of a vehicle airdrop.", new AcceptableValueRange<float>(0f, 1000f)));
            FortificationCost = config.Bind("Support", "FortificationCost", 10f,
                new ConfigDescription("Allocation cost of reinforcing a controlled zone.", new AcceptableValueRange<float>(0f, 1000f)));
            ArtilleryCost = config.Bind("Support", "ArtilleryCost", 8f,
                new ConfigDescription("Allocation cost of one bounded artillery mission.", new AcceptableValueRange<float>(0f, 1000f)));
            RequestCooldown = config.Bind("Support", "RequestCooldownSeconds", 30f,
                new ConfigDescription("Per-player cooldown after an accepted support request.", new AcceptableValueRange<float>(5f, 600f)));
            VehicleDefinitionKey = config.Bind("Support", "VehicleDefinitionKey", string.Empty,
                "Optional exact Encyclopedia vehicle jsonKey. Empty selects the first verified airdrop-capable ground vehicle.");
            ArtilleryDefinitionKey = config.Bind("Support", "ArtilleryDefinitionKey", string.Empty,
                "Optional exact low-yield missile jsonKey used by the experimental artillery action.");
        }
    }
}
