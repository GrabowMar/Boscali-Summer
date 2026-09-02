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
        public ConfigEntry<float> ReconRange { get; }
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
            // Every value in this section is decided by the host. A client's copy only
            // affects what its own OPS page predicts before the host answers.
            Enabled = config.Bind("Support", "Enabled", true,
                "Enable the Boscali support board. Turning this off skips the whole feature, " +
                "including its network handlers and the SUPPORT page. " +
                "Host-authoritative: on a server, only the host's value applies.");

            AirdropEnabled = config.Bind("Support", "VehicleAirdrop", true,
                "Parachute an armoured vehicle onto a designated grid. Consumes faction stock.");
            AirDefenceDropEnabled = config.Bind("Support", "AirDefenceAirdrop", true,
                "Parachute a mobile air-defence vehicle onto a designated grid. Consumes faction stock.");
            ConvoyEnabled = config.Bind("Support", "GroundConvoy", true,
                "Requisition a ground convoy at the friendly airbase nearest the mark. " +
                "Consumes faction stock per vehicle.");
            ReconEnabled = config.Bind("Support", "ReconSweep", true,
                "Reveal hostile units around a designated grid for the whole faction. " +
                "Spawns nothing and consumes no stock.");
            FortifyEnabled = config.Bind("Support", "Fortification", true,
                "Reinforce a friendly controlled zone. Requires the Garrisons feature; the " +
                "request is refused, and nothing is charged, when it cannot place defenders.");
            ArtilleryEnabled = config.Bind("Support", "Artillery", false,
                "Experimental low-yield ordnance fire missions. Does nothing until " +
                "FireMissionDefinitionKey names a non-nuclear missile with a yield of 200 or less.");

            CostMultiplier = config.Bind("Support", "CostMultiplier", 1f,
                new ConfigDescription(
                    "Scales every support cost at once. Airdrops and convoys are priced from the " +
                    "vanilla unit value, so 1.0 charges roughly what the requisitioned units are " +
                    "worth and stays balanced when the game rebalances. Raise it to make support " +
                    "a real sacrifice, drop it toward 0 for a sandbox. " +
                    "Host-authoritative: a client's value only changes what its own page predicts.",
                    new AcceptableValueRange<float>(0f, 10f)));
            ReconCost = config.Bind("Support", "ReconCost", 600f,
                new ConfigDescription(
                    "Allocation charged for one reconnaissance sweep, before CostMultiplier and " +
                    "the Logistics Officer perk. Recon spawns nothing, so it is priced flat.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            FortifyCost = config.Bind("Support", "ZoneFortificationCost", 1200f,
                new ConfigDescription(
                    "Allocation charged for reinforcing a controlled zone, before CostMultiplier " +
                    "and the Logistics Officer perk. Charged only once the host has verified it " +
                    "can actually place defenders.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            ArtilleryCost = config.Bind("Support", "FireMissionCost", 900f,
                new ConfigDescription(
                    "Allocation charged for one artillery fire mission, before CostMultiplier and " +
                    "the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));

            MaximumRange = config.Bind("Support", "MaximumRangeMeters", 30000f,
                new ConfigDescription(
                    "Furthest a designated grid may be from your aircraft for an action that " +
                    "delivers something physical - airdrops, convoys and fire missions. You must " +
                    "be in an aircraft to request one.",
                    new AcceptableValueRange<float>(1000f, 200000f)));
            ReconRange = config.Bind("Support", "ReconRangeMeters", 120000f,
                new ConfigDescription(
                    "Furthest a designated grid may be for a reconnaissance sweep. Recon asks HQ " +
                    "to look somewhere rather than delivering anything, so it reaches across the " +
                    "map rather than being held to the delivery range above.",
                    new AcceptableValueRange<float>(1000f, 400000f)));
            RequestCooldown = config.Bind("Support", "RequestCooldownSeconds", 30f,
                new ConfigDescription(
                    "Cooldown after an accepted request, per player and shared across all actions. " +
                    "The OPS page counts it down on the request button.",
                    new AcceptableValueRange<float>(5f, 600f)));
            ReconRadius = config.Bind("Support", "ReconRadiusMeters", 6000f,
                new ConfigDescription(
                    "Radius around the mark searched for hostile units. At most 48 contacts are " +
                    "revealed per sweep, so a very large radius reveals a sparser picture rather " +
                    "than more of it.",
                    new AcceptableValueRange<float>(500f, 20000f)));
            ConvoyVehicles = config.Bind("Support", "ConvoyVehicles", 3,
                new ConfigDescription(
                    "Vehicles in one requisitioned ground convoy. Each one costs its vanilla unit " +
                    "value and one point of faction stock, so this scales the price too.",
                    new AcceptableValueRange<int>(1, 6)));

            // Leave these empty unless you want a specific unit. Empty picks by vanilla role
            // and prefers something the faction actually has in stock; naming a key pins the
            // choice, and a request fails with "no stock at HQ" once that unit runs out.
            VehicleDefinitionKey = config.Bind("Support", "VehicleDefinitionKey", string.Empty,
                "Exact Encyclopedia jsonKey for the armour airdrop. Empty picks a parachute-capable " +
                "anti-surface vehicle the faction has in stock.");
            AirDefenceDefinitionKey = config.Bind("Support", "AirDefenceDefinitionKey", string.Empty,
                "Exact Encyclopedia jsonKey for the air-defence airdrop. Empty picks a " +
                "parachute-capable anti-air vehicle the faction has in stock.");
            ConvoyDefinitionKey = config.Bind("Support", "ConvoyDefinitionKey", string.Empty,
                "Exact Encyclopedia jsonKey for convoy vehicles. Empty picks an anti-surface ground " +
                "vehicle the faction has enough of.");
            ArtilleryDefinitionKey = config.Bind("Support", "FireMissionDefinitionKey", string.Empty,
                "Exact jsonKey of the missile used by fire missions. Required for Artillery to do " +
                "anything, and rejected unless the missile is non-nuclear with a yield of 200 or " +
                "less. Check the startup log for the definitions this game build loaded.");
        }
    }
}
