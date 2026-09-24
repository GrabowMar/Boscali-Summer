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
        public ConfigEntry<bool> MtiEnabled { get; }
        public ConfigEntry<bool> ElintEnabled { get; }
        public ConfigEntry<bool> FlareBarrageEnabled { get; }
        public ConfigEntry<bool> CyberEnabled { get; }
        public ConfigEntry<bool> EwEnabled { get; }
        public ConfigEntry<bool> SpecOpsEnabled { get; }
        public ConfigEntry<bool> ShowOnTacticalMap { get; }
        public ConfigEntry<bool> ReduceMotion { get; }

        public ConfigEntry<float> PlatformCostScale { get; }
        public ConfigEntry<float> PlatformJettisonRefund { get; }
        public ConfigEntry<float> PlatformInsertionSeconds { get; }
        public ConfigEntry<float> PlatformDockingSeconds { get; }
        public ConfigEntry<bool> PlatformDebrisEvents { get; }
        public ConfigEntry<float> OrbitGapScale { get; }
        public ConfigEntry<float> SarSceneRadius { get; }
        public ConfigEntry<float> ElintCost { get; }
        public ConfigEntry<float> MtiCost { get; }
        public ConfigEntry<float> ElintRadius { get; }

        public ConfigEntry<float> CostMultiplier { get; }
        public ConfigEntry<float> ReconCost { get; }
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

        public ConfigEntry<float> CyberUpgradeCostScale { get; }
        public ConfigEntry<float> CyberCampaignIntensity { get; }
        public ConfigEntry<float> CyberReach { get; }
        public ConfigEntry<float> SpecOpsCostScale { get; }

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
                "Radar scan: an orbital station with a spy imager, overhead, images a scene and the host " +
                "reveals stationary ground contacts in it. Spawns nothing.");
            FortifyEnabled = config.Bind("Support", "Fortification", true,
                "Reinforce a friendly controlled zone. Requires the Garrisons feature; the " +
                "request is refused, and nothing is charged, when it cannot place defenders.");
            ArtilleryEnabled = config.Bind("Support", "RodFromGod", true,
                "Orbital kinetic strike from the station's rod magazine: one high-velocity projectile onto " +
                "the mark, scattered by orbit band. Uses the FireMissionDefinitionKey missile.");
            EmpEnabled = config.Bind("Support", "EmpShock", true,
                "EMP shock: a high-altitude airburst. The prompt pulse upsets electronics; the " +
                "geomagnetic disturbance jams radars across a wide area, friendly and hostile " +
                "alike. Needs the station's EMP emitter overhead. Uses the FireMissionDefinitionKey missile " +
                "as a delivery visual.");
            ElintEnabled = config.Bind("Support", "ElintSweep", true,
                "ELINT sweep: an orbital station with a SIGINT array, overhead, locates enemy ground and ship " +
                "radars that are emitting near the mark. Spawns nothing.");
            MtiEnabled = config.Bind("Support", "MtiSweep", true,
                "MTI sweep: an orbital station with a spy imager, overhead, tracks moving enemy ground " +
                "contacts near the mark. Shares the radar scan tasking. Spawns nothing.");
            FlareBarrageEnabled = config.Bind("Support", "FlareBarrage", true,
                "Flare barrage: launches an airburst countermeasure missile that disperses a cluster of " +
                "intense pyrotechnic flares, seducing and misguiding all IR-seeking missiles in the area.");
            CyberEnabled = config.Bind("Support", "CyberOperations", true,
                "Enable OPS CYBER operations: doctrine investment and the offensive operations " +
                "it unlocks. Host-authoritative.");
            EwEnabled = config.Bind("Support", "ElectronicWarfare", true,
                "Enable the OPS CYBER spectrum-defence network: airbase infrastructure that comes up by itself " +
                "(Cyber Command and gateways on owned airbases), field sites (early-warning radar, jammer, SIGINT, " +
                "relay), the adversary campaign against it and the console. Emitting jammers back radar blackout, " +
                "ghost shield and spoof contacts. Host-authoritative.");
            SpecOpsEnabled = config.Bind("Support", "SpecialOperations", true,
                "Enable OPS SPEC OPS: the detachment's four teams, missions to real map objectives (recon, " +
                "air-defence sabotage, seizing buildings) and the SPOT and SUPPRESS abilities their posts grant. " +
                "Host-authoritative.");
            ShowOnTacticalMap = config.Bind("Support", "ShowOnTacticalMap", true,
                "Show ability range circles, tactical vector icons, orbital station ground tracks and " +
                "active strike waypoints on the tactical theater map.");
            ReduceMotion = config.Bind("Support", "ReduceMotion", false,
                "Client-local. OPS windows and rooms open on their final frame, with no motion. " +
                "Does not change host rules, prices or what a peer sees.");

            PlatformCostScale = config.Bind("Support", "PlatformCostScale", 1f,
                new ConfigDescription(
                    "Scales every orbital station launch (core, modules, cargo; module price plus launch vehicle), " +
                    "before CostMultiplier. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 10f)));
            PlatformJettisonRefund = config.Bind("Support", "PlatformJettisonRefund", 0.4f,
                new ConfigDescription(
                    "Fraction of what was paid refunded when a module is jettisoned; jettisoning the core deorbits " +
                    "the station and refunds this share of everything. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 1f)));
            PlatformInsertionSeconds = config.Bind("Support", "PlatformInsertionSeconds", 45f,
                new ConfigDescription(
                    "Seconds from core liftoff until the platform holds its fixed theatre position. " +
                    "Host-authoritative.",
                    new AcceptableValueRange<float>(10f, 600f)));
            PlatformDockingSeconds = config.Bind("Support", "PlatformDockingSeconds", 20f,
                new ConfigDescription(
                    "Seconds from a module or cargo liftoff to docking. Host-authoritative.",
                    new AcceptableValueRange<float>(5f, 300f)));
            PlatformDebrisEvents = config.Bind("Support", "PlatformDebrisEvents", true,
                "Every 6-10 minutes a micrometeoroid strike hits a random station module: shielded modules " +
                "deflect it, others go offline for 45 s. Host-authoritative.");
            OrbitGapScale = config.Bind("Support", "OrbitGapScale", 1f,
                new ConfigDescription(
                    "Legacy compatibility setting. Fixed-position stations no longer use pass gaps; this value has no gameplay effect.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            SarSceneRadius = config.Bind("Support", "SarSceneRadiusMeters", 1000f,
                new ConfigDescription(
                    "Half-width of a radar scan scene at MID orbit (x0.8 LOW, x1.4 HIGH, x1.35 with a relay). " +
                    "Stationary ground contacts inside it are revealed; movers faster than 4 m/s smear and are not.",
                    new AcceptableValueRange<float>(400f, 4000f)));
            ElintCost = config.Bind("Support", "ElintSweepCost", 400f,
                new ConfigDescription(
                    "Allocation charged for one ELINT sweep, before CostMultiplier and the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            ElintRadius = config.Bind("Support", "ElintSweepRadiusMeters", 8000f,
                new ConfigDescription(
                    "Radius around the mark searched for emitting enemy radars at MID orbit (scaled like the " +
                    "radar scan).",
                    new AcceptableValueRange<float>(1000f, 40000f)));
            MtiCost = config.Bind("Support", "MtiSweepCost", 500f,
                new ConfigDescription(
                    "Allocation charged for one MTI sweep, before CostMultiplier and the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));

            CostMultiplier = config.Bind("Support", "CostMultiplier", 1f,
                new ConfigDescription(
                    "Scales every support cost at once. 1.0 charges roughly what the effect is " +
                    "worth and stays balanced when the game rebalances. Raise it to make support " +
                    "a real sacrifice, drop it toward 0 for a sandbox. " +
                    "Host-authoritative: a client's value only changes what its own page predicts.",
                    new AcceptableValueRange<float>(0f, 10f)));
            ReconCost = config.Bind("Support", "ReconCost", 600f,
                new ConfigDescription(
                    "Allocation charged for one radar scan, before CostMultiplier and " +
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
            RequestCooldown = config.Bind("Support", "RequestCooldownSeconds", 30f,
                new ConfigDescription(
                    "Cooldown after an accepted request, per player and shared across all actions. " +
                    "The OPS page counts it down on the request button.",
                    new AcceptableValueRange<float>(5f, 600f)));

            CyberUpgradeCostScale = config.Bind("Support", "CyberUpgradeCostScale", 1f,
                new ConfigDescription(
                    "Scales the price of every CYBER network upgrade (radius 800/1300/1900, reach 700/1200/1800, " +
                    "yield 900/1400/2000, trace 1000/1500/2200), before CostMultiplier. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 10f)));
            CyberCampaignIntensity = config.Bind("Support", "CyberCampaignIntensity", 0.75f,
                new ConfigDescription(
                    "How hard the simulated adversary works against each faction's CYBER network: scales heat " +
                    "build-up and how often probes, intrusions and jamming raids arrive. 0 switches the campaign " +
                    "off; enemy players' operations are still heard. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 4f)));
            CyberReach = config.Bind("Support", "CyberReachMeters", 30000f,
                new ConfigDescription(
                    "Base network reach: how far from one of the faction's online nodes a location may be for a " +
                    "breach to be authorised. Upgrades raise it by 25% a level. Host-authoritative.",
                    new AcceptableValueRange<float>(2000f, 120000f)));

            SpecOpsCostScale = config.Bind("Support", "SpecOpsCostScale", 1f,
                new ConfigDescription(
                    "Scales every SPEC OPS price (raise a team 1000; recon 400, sabotage 700, seize 900; SPOT 250, " +
                    "SUPPRESS 500), before CostMultiplier. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 10f)));

            ArtilleryDefinitionKey = config.Bind("Support", "FireMissionDefinitionKey", string.Empty,
                "Exact jsonKey of the missile used by Rod from God and EMP shock. Empty auto-picks " +
                "a non-nuclear vanilla missile. Only non-nuclear missiles with a yield of 200 or " +
                "less are accepted. Check the startup log for the definitions this game build loaded.");

        }
    }
}
