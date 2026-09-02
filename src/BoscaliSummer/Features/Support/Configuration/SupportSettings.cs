using BepInEx.Configuration;

namespace BoscaliSummer.Features.Support.Configuration
{
    internal sealed class SupportSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> AirdropEnabled { get; }
        public ConfigEntry<bool> AirDefenceDropEnabled { get; }
        public ConfigEntry<bool> ConvoyEnabled { get; }
        public ConfigEntry<bool> ReconEnabled { get; }
        public ConfigEntry<bool> FortifyEnabled { get; }
        public ConfigEntry<bool> ArtilleryEnabled { get; }

        public ConfigEntry<float> CostMultiplier { get; }
        public ConfigEntry<float> ReconCost { get; }
        public ConfigEntry<float> FortifyCost { get; }
        public ConfigEntry<float> ArtilleryCost { get; }

        public ConfigEntry<float> MaximumRange { get; }
        public ConfigEntry<float> RequestCooldown { get; }
        public ConfigEntry<float> ReconRadius { get; }
        public ConfigEntry<int> ConvoyVehicles { get; }

        public ConfigEntry<string> VehicleDefinitionKey { get; }
        public ConfigEntry<string> AirDefenceDefinitionKey { get; }
        public ConfigEntry<string> ConvoyDefinitionKey { get; }
        public ConfigEntry<string> ArtilleryDefinitionKey { get; }

        public SupportSettings(ConfigFile config)
        {
            Enabled = config.Bind("Support", "Enabled", true,
                "Enable the server-authoritative Boscali support board. Disabled skips the whole " +
                "feature, including its network handlers and MFD page.");

            AirdropEnabled = config.Bind("Support", "VehicleAirdrop", true,
                "Allow authorised players to parachute an armoured vehicle onto a designated grid.");
            AirDefenceDropEnabled = config.Bind("Support", "AirDefenceAirdrop", true,
                "Allow authorised players to parachute an air-defence vehicle onto a designated grid.");
            ConvoyEnabled = config.Bind("Support", "GroundConvoy", true,
                "Allow authorised players to requisition a ground convoy at their nearest airbase.");
            ReconEnabled = config.Bind("Support", "ReconSweep", true,
                "Allow authorised players to reveal hostile units around a designated grid.");
            FortifyEnabled = config.Bind("Support", "Fortification", true,
                "Allow authorised players to reinforce a friendly controlled zone.");
            ArtilleryEnabled = config.Bind("Support", "Artillery", false,
                "Experimental: low-yield vanilla ordnance fire missions. Requires ArtilleryDefinitionKey.");

            CostMultiplier = config.Bind("Support", "CostMultiplier", 1f,
                new ConfigDescription(
                    "Scales every support cost. Spawning actions are priced from the vanilla unit " +
                    "value, so 1.0 charges roughly what the requisitioned units are worth.",
                    new AcceptableValueRange<float>(0f, 10f)));
            ReconCost = config.Bind("Support", "ReconCost", 600f,
                new ConfigDescription("Base allocation cost of one reconnaissance sweep.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            FortifyCost = config.Bind("Support", "FortificationCost", 1200f,
                new ConfigDescription("Base allocation cost of reinforcing a controlled zone.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            ArtilleryCost = config.Bind("Support", "ArtilleryCost", 900f,
                new ConfigDescription("Base allocation cost of one bounded artillery mission.",
                    new AcceptableValueRange<float>(0f, 20000f)));

            MaximumRange = config.Bind("Support", "MaximumRangeMeters", 30000f,
                new ConfigDescription("Furthest a designated grid may be from the requesting aircraft.",
                    new AcceptableValueRange<float>(1000f, 200000f)));
            RequestCooldown = config.Bind("Support", "RequestCooldownSeconds", 30f,
                new ConfigDescription("Per-player cooldown after an accepted support request.",
                    new AcceptableValueRange<float>(5f, 600f)));
            ReconRadius = config.Bind("Support", "ReconRadiusMeters", 6000f,
                new ConfigDescription("Radius around the designated grid searched for hostile units.",
                    new AcceptableValueRange<float>(500f, 20000f)));
            ConvoyVehicles = config.Bind("Support", "ConvoyVehicles", 3,
                new ConfigDescription("Vehicles in one requisitioned ground convoy.",
                    new AcceptableValueRange<int>(1, 6)));

            VehicleDefinitionKey = config.Bind("Support", "VehicleDefinitionKey", string.Empty,
                "Optional exact Encyclopedia jsonKey for the armour airdrop. Empty selects the first " +
                "parachute-capable anti-surface vehicle.");
            AirDefenceDefinitionKey = config.Bind("Support", "AirDefenceDefinitionKey", string.Empty,
                "Optional exact Encyclopedia jsonKey for the air-defence airdrop. Empty selects the " +
                "first parachute-capable anti-air vehicle.");
            ConvoyDefinitionKey = config.Bind("Support", "ConvoyDefinitionKey", string.Empty,
                "Optional exact Encyclopedia jsonKey for convoy vehicles. Empty selects the first " +
                "anti-surface ground vehicle.");
            ArtilleryDefinitionKey = config.Bind("Support", "ArtilleryDefinitionKey", string.Empty,
                "Exact low-yield missile jsonKey used by the experimental artillery action.");
        }
    }
}
