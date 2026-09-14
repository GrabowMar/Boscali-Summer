using BepInEx.Configuration;

namespace BoscaliSummer.Features.Support.Configuration
{
    internal sealed class SupportSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ReconEnabled { get; }
        public ConfigEntry<bool> FortifyEnabled { get; }
        public ConfigEntry<bool> ArtilleryEnabled { get; }
        public ConfigEntry<bool> EmpEnabled { get; }
        public ConfigEntry<bool> FlareBarrageEnabled { get; }
        public ConfigEntry<bool> CyberEnabled { get; }
        public ConfigEntry<bool> ShowOnTacticalMap { get; }

        public ConfigEntry<int> MaximumSatellites { get; }
        public ConfigEntry<float> SatelliteReconCost { get; }
        public ConfigEntry<float> SatelliteStrikeCost { get; }
        public ConfigEntry<float> SatelliteEwCost { get; }
        public ConfigEntry<float> SatelliteRecallRefund { get; }

        public ConfigEntry<float> CostMultiplier { get; }
        public ConfigEntry<float> ReconCost { get; }
        public ConfigEntry<float> ReconRange { get; }
        public ConfigEntry<float> FortifyCost { get; }
        public ConfigEntry<float> ArtilleryCost { get; }
        public ConfigEntry<float> EmpCost { get; }
        public ConfigEntry<float> EmpRadius { get; }
        public ConfigEntry<float> FlareBarrageCost { get; }
        public ConfigEntry<float> FlareBarrageRadius { get; }
        public ConfigEntry<int> FlareBarrageCount { get; }
        public ConfigEntry<float> FlareBarrageDuration { get; }

        public ConfigEntry<float> MaximumRange { get; }
        public ConfigEntry<float> RequestCooldown { get; }
        public ConfigEntry<float> ReconRadius { get; }

        public ConfigEntry<string> ArtilleryDefinitionKey { get; }

        public SupportSettings(ConfigFile config)
        {
            // Every value in this section is decided by the host. A client's copy only
            // affects what its own OPS page predicts before the host answers.
            Enabled = config.Bind("Support", "Enabled", true,
                "Enable the Boscali support board. Turning this off skips the whole feature, " +
                "including its network handlers and the SUPPORT page. " +
                "Host-authoritative: on a server, only the host's value applies.");

            ReconEnabled = config.Bind("Support", "ReconSweep", true,
                "Satellite scan: reserve one faction scan for the next orbital coverage window. Requires an aircraft at request time. " +
                "Spawns nothing.");
            FortifyEnabled = config.Bind("Support", "Fortification", true,
                "Reinforce a friendly controlled zone. Requires the Garrisons feature; the " +
                "request is refused, and nothing is charged, when it cannot place defenders.");
            ArtilleryEnabled = config.Bind("Support", "RodFromGod", true,
                "Orbital kinetic strike: one high-velocity projectile onto the mark. Uses the " +
                "FireMissionDefinitionKey missile.");
            EmpEnabled = config.Bind("Support", "EmpShock", true,
                "EMP shock: a high-altitude airburst. The prompt pulse upsets electronics; the " +
                "geomagnetic disturbance jams radars across a wide area, friendly and hostile " +
                "alike. Uses the FireMissionDefinitionKey missile as a delivery visual.");
            FlareBarrageEnabled = config.Bind("Support", "FlareBarrage", true,
                "Flare barrage: launches an airburst countermeasure missile that disperses a cluster of " +
                "intense pyrotechnic flares, seducing and misguiding all IR-seeking missiles in the area.");
            CyberEnabled = config.Bind("Support", "CyberOperations", true,
                "Enable the CYBER page: infrastructure investment and the information-warfare " +
                "operations it unlocks (ping sweep, track uplink, radar blackout, ghost shield, " +
                "spoof contacts). Host-authoritative.");
            ShowOnTacticalMap = config.Bind("Support", "ShowOnTacticalMap", true,
                "Show ability range circles, tactical vector icons, satellite tracks and active " +
                "strike waypoints on the tactical theater map.");

            MaximumSatellites = config.Bind("Support", "MaximumSatellites", 4,
                new ConfigDescription(
                    "How many satellites one faction may keep on orbit. Host-authoritative.",
                    new AcceptableValueRange<int>(1, 4)));
            SatelliteReconCost = config.Bind("Support", "SatelliteReconCost", 900f,
                new ConfigDescription(
                    "Allocation to launch a reconnaissance satellite, before CostMultiplier. " +
                    "One-time purchase; moving it later costs fuel, not allocation.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            SatelliteStrikeCost = config.Bind("Support", "SatelliteStrikeCost", 1100f,
                new ConfigDescription(
                    "Allocation to launch a strike satellite (Rod from God coverage), before CostMultiplier.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            SatelliteEwCost = config.Bind("Support", "SatelliteEwCost", 1100f,
                new ConfigDescription(
                    "Allocation to launch an electronic-warfare satellite (EMP coverage), before CostMultiplier.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            SatelliteRecallRefund = config.Bind("Support", "SatelliteRecallRefund", 0.4f,
                new ConfigDescription(
                    "Fraction of the launch cost refunded when a satellite is recalled to free a slot. " +
                    "Host-authoritative; refunds use the amount actually charged.",
                    new AcceptableValueRange<float>(0f, 1f)));

            CostMultiplier = config.Bind("Support", "CostMultiplier", 1f,
                new ConfigDescription(
                    "Scales every support cost at once. 1.0 charges roughly what the effect is " +
                    "worth and stays balanced when the game rebalances. Raise it to make support " +
                    "a real sacrifice, drop it toward 0 for a sandbox. " +
                    "Host-authoritative: a client's value only changes what its own page predicts.",
                    new AcceptableValueRange<float>(0f, 10f)));
            ReconCost = config.Bind("Support", "ReconCost", 600f,
                new ConfigDescription(
                    "Allocation reserved for one satellite scan (refunded on cancellation), before CostMultiplier and " +
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
                    "Allocation charged for one Rod from God strike, before CostMultiplier and " +
                    "the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            EmpCost = config.Bind("Support", "EmpShockCost", 1500f,
                new ConfigDescription(
                    "Allocation charged for one EMP shock, before CostMultiplier and the " +
                    "Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            EmpRadius = config.Bind("Support", "EmpShockRadiusMeters", 12000f,
                new ConfigDescription(
                    "Radius around the mark whose radars are jammed by the geomagnetic phase of " +
                    "an EMP shock. Affects friendly and hostile units alike.",
                    new AcceptableValueRange<float>(1000f, 60000f)));
            FlareBarrageCost = config.Bind("Support", "FlareBarrageCost", 500f,
                new ConfigDescription(
                    "Allocation charged for one Flare Barrage countermeasure rocket, before " +
                    "CostMultiplier and the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            FlareBarrageRadius = config.Bind("Support", "FlareBarrageRadiusMeters", 4000f,
                new ConfigDescription(
                    "Radius around the mark in which IR missiles are seduced and misguided.",
                    new AcceptableValueRange<float>(500f, 15000f)));
            FlareBarrageCount = config.Bind("Support", "FlareBarrageCount", 36,
                new ConfigDescription(
                    "Number of authentic pyrotechnic flares dispersed in the initial airburst wave.",
                    new AcceptableValueRange<int>(12, 64)));
            FlareBarrageDuration = config.Bind("Support", "FlareBarrageDurationSeconds", 15f,
                new ConfigDescription(
                    "Total duration in seconds of the continuous flare countermeasure barrage.",
                    new AcceptableValueRange<float>(5f, 45f)));

            MaximumRange = config.Bind("Support", "MaximumRangeMeters", 30000f,
                new ConfigDescription(
                    "Furthest a designated grid may be from your aircraft for an action that " +
                    "delivers something physical - strikes. You must be in an aircraft to request one.",
                    new AcceptableValueRange<float>(1000f, 200000f)));
            ReconRange = config.Bind("Support", "ReconRangeMeters", 120000f,
                new ConfigDescription(
                    "Furthest a designated grid may be for a satellite scan. Recon asks HQ " +
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

            ArtilleryDefinitionKey = config.Bind("Support", "FireMissionDefinitionKey", string.Empty,
                "Exact jsonKey of the missile used by Rod from God and EMP shock. Empty auto-picks " +
                "a non-nuclear vanilla missile. Only non-nuclear missiles with a yield of 200 or " +
                "less are accepted. Check the startup log for the definitions this game build loaded.");

        }
    }
}
