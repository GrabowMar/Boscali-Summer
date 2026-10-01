using BepInEx.Configuration;

namespace BoscaliSummer.Features.Support.Configuration
{
    internal sealed class SupportSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> ReconEnabled { get; }
        public ConfigEntry<bool> FortifyEnabled { get; }
        public ConfigEntry<bool> ArtilleryEnabled { get; }
        public ConfigEntry<bool> PrsmEnabled { get; }
        public ConfigEntry<bool> CruiseEnabled { get; }
        public ConfigEntry<bool> EmpEnabled { get; }
        public ConfigEntry<bool> MtiEnabled { get; }
        public ConfigEntry<bool> ElintEnabled { get; }
        public ConfigEntry<bool> FlareBarrageEnabled { get; }
        public ConfigEntry<bool> CyberEnabled { get; }
        public ConfigEntry<bool> EwEnabled { get; }
        public ConfigEntry<bool> SpecOpsEnabled { get; }
        public ConfigEntry<bool> ReduceMotion { get; }

        public ConfigEntry<float> PlatformCostScale { get; }
        public ConfigEntry<float> PlatformJettisonRefund { get; }
        public ConfigEntry<float> PlatformInsertionSeconds { get; }
        public ConfigEntry<float> PlatformDockingSeconds { get; }
        public ConfigEntry<bool> PlatformDebrisEvents { get; }
        public ConfigEntry<float> SarSceneRadius { get; }
        public ConfigEntry<float> ElintCost { get; }
        public ConfigEntry<float> MtiCost { get; }
        public ConfigEntry<float> ElintRadius { get; }

        public ConfigEntry<float> CostMultiplier { get; }
        public ConfigEntry<float> ReconCost { get; }
        public ConfigEntry<float> FortifyCost { get; }
        public ConfigEntry<float> ArtilleryCost { get; }
        public ConfigEntry<float> PrsmCost { get; }
        public ConfigEntry<float> CruiseCost { get; }
        public ConfigEntry<int> CruiseSalvo { get; }
        public ConfigEntry<int> CruiseLiveCap { get; }
        public ConfigEntry<float> IntelFreshSeconds { get; }
        public ConfigEntry<float> IntelGateRadius { get; }
        public ConfigEntry<float> WindowOpenSeconds { get; }
        public ConfigEntry<float> WindowClosedSeconds { get; }
        public ConfigEntry<float> EmpCost { get; }
        public ConfigEntry<float> EmpRadius { get; }
        public ConfigEntry<float> FlareBarrageCost { get; }
        public ConfigEntry<float> FlareBarrageRadius { get; }
        public ConfigEntry<int> FlareBarrageCount { get; }
        public ConfigEntry<float> FlareBarrageDuration { get; }
        public ConfigEntry<float> JtacMarkCost { get; }
        public ConfigEntry<float> JtacMarkDuration { get; }

        public ConfigEntry<float> MaximumRange { get; }
        public ConfigEntry<float> RequestCooldown { get; }

        public ConfigEntry<float> TeamFlareCooldown { get; }
        public ConfigEntry<float> TeamFortifyCooldown { get; }
        public ConfigEntry<float> TeamRelocateCooldown { get; }
        public ConfigEntry<float> TeamIsolateCooldown { get; }
        public ConfigEntry<float> TeamCostPerExtra { get; }
        public ConfigEntry<float> TeamCostCap { get; }
        public ConfigEntry<bool> TeamOwnershipGuards { get; }

        public ConfigEntry<float> CyberUpgradeCostScale { get; }
        public ConfigEntry<float> CyberCampaignIntensity { get; }
        public ConfigEntry<float> CyberReach { get; }
        public ConfigEntry<float> SpecOpsCostScale { get; }

        public ConfigEntry<string> ArtilleryDefinitionKey { get; }
        public ConfigEntry<string> PrsmDefinitionKey { get; }
        public ConfigEntry<string> CruiseDefinitionKey { get; }

        public SupportSettings(ConfigFile config)
        {
            // Every gameplay value in this section is decided by the host; while connected, the
            // host's values are replicated to clients, so an OPS page predicts with them too.
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
            PrsmEnabled = config.Bind("Support", "PrsmStrike", true,
                "PRSM strike: one offboard ballistic missile onto the mark. Needs fresh HQ intel " +
                "at the target, not a station. Uses the PrsmDefinitionKey missile.");
            CruiseEnabled = config.Bind("Support", "CruiseStrike", true,
                "Cruise strike: a bounded salvo of offboard cruise missiles onto the mark, separated " +
                "by the seeker's native formation spacing. Needs fresh HQ intel at the target, not a " +
                "station. Uses the CruiseDefinitionKey missile.");
            EmpEnabled = config.Bind("Support", "EmpShock", true,
                "EMP shock: a high-altitude airburst. The prompt pulse upsets electronics; the " +
                "geomagnetic disturbance jams hostile radars across a wide area while friendly " +
                "units keep theirs. Needs the station's EMP emitter overhead. Uses the " +
                "FireMissionDefinitionKey missile as a delivery visual.");
            ElintEnabled = config.Bind("Support", "ElintSweep", true,
                "ELINT sweep: an orbital station with a SIGINT array, overhead, locates enemy ground and ship " +
                "radars that are emitting near the mark. Spawns nothing.");
            MtiEnabled = config.Bind("Support", "MtiSweep", true,
                "MTI sweep: an orbital station with a spy imager, overhead, tracks moving enemy ground " +
                "contacts near the mark. Shares the radar scan tasking. Spawns nothing.");
            FlareBarrageEnabled = config.Bind("Support", "FlareBarrage", true,
                "Flare barrage: launches an airburst countermeasure missile that disperses a cluster of " +
                "intense pyrotechnic flares, seducing and misguiding hostile IR-seeking missiles in the area. " +
                "Friendly missiles fly through.");
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
            ReduceMotion = config.Bind("Support", "ReduceMotion", false,
                "Client-local. OPS panels paint on their final frame, with no motion. " +
                "Does not change host rules, prices or what a peer sees.");

            PlatformCostScale = config.Bind("Support", "PlatformCostScale", 1f,
                new ConfigDescription(
                    "Scales every orbital station launch (core, modules, cargo; module price plus launch vehicle), " +
                    "before CostMultiplier. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 10f)));
            PlatformJettisonRefund = config.Bind("Support", "PlatformJettisonRefund", 0.4f,
                new ConfigDescription(
                    "Fraction of what was paid, refunded to whoever paid for it, when a module is jettisoned; " +
                    "jettisoning the core deorbits the station and refunds this share of everything. Host-authoritative.",
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
            SarSceneRadius = config.Bind("Support", "SarSceneRadiusMeters", 1000f,
                new ConfigDescription(
                    "Half-width of a radar scan scene (x1.35 while a relay boosts the imager). " +
                    "Stationary ground contacts inside it are revealed; movers faster than 4 m/s smear and are not.",
                    new AcceptableValueRange<float>(400f, 4000f)));
            ElintCost = config.Bind("Support", "ElintSweepCost", 400f,
                new ConfigDescription(
                    "Allocation charged for one ELINT sweep, before CostMultiplier and the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            ElintRadius = config.Bind("Support", "ElintSweepRadiusMeters", 8000f,
                new ConfigDescription(
                    "Radius around the mark searched for emitting enemy radars (x1.35 while a relay boosts " +
                    "the SIGINT array).",
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
                    "Host-authoritative: the host's value applies and is shown on every OPS page.",
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
            PrsmCost = config.Bind("Support", "PrsmStrikeCost", 1100f,
                new ConfigDescription(
                    "Allocation charged for one PRSM strike, before CostMultiplier and " +
                    "the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            CruiseCost = config.Bind("Support", "CruiseStrikeCost", 1600f,
                new ConfigDescription(
                    "Allocation charged for one cruise salvo, before CostMultiplier and " +
                    "the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            CruiseSalvo = config.Bind("Support", "CruiseSalvoSize", 4,
                new ConfigDescription(
                    "Missiles per cruise salvo. Host-authoritative.",
                    new AcceptableValueRange<int>(1, 8)));
            CruiseLiveCap = config.Bind("Support", "CruiseLiveCapPerFaction", 8,
                new ConfigDescription(
                    "Most cruise missiles of one faction alive at once, counted on the HQ registry. " +
                    "A salvo that would pass it is refused. Host-authoritative.",
                    new AcceptableValueRange<int>(1, 32)));
            IntelFreshSeconds = config.Bind("Support", "IntelFreshSeconds", 120f,
                new ConfigDescription(
                    "Seconds an HQ track near the grid stays fresh enough to release a strike. " +
                    "Older intel denies with STALE INTEL naming the sweep. Host-authoritative.",
                    new AcceptableValueRange<float>(10f, 600f)));
            WindowOpenSeconds = config.Bind("Support", "WindowOpenSeconds", 180f,
                new ConfigDescription(
                    "Seconds a tasking window stays open; the heaviest fires and the sweeps release inside it. Host-authoritative.",
                    new AcceptableValueRange<float>(30f, 600f)));
            WindowClosedSeconds = config.Bind("Support", "WindowClosedSeconds", 90f,
                new ConfigDescription(
                    "Seconds between windows. Keep it under IntelFreshSeconds so a sweep can always re-fresh " +
                    "stale intel before the next opening. 0 leaves the window always open. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 600f)));
            IntelGateRadius = config.Bind("Support", "IntelGateRadiusMeters", 1000f,
                new ConfigDescription(
                    "Radius around the grid searched for a fresh HQ track before a strike releases.",
                    new AcceptableValueRange<float>(100f, 10000f)));
            EmpCost = config.Bind("Support", "EmpShockCost", 1500f,
                new ConfigDescription(
                    "Allocation charged for one EMP shock, before CostMultiplier and the " +
                    "Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            EmpRadius = config.Bind("Support", "EmpShockRadiusMeters", 12000f,
                new ConfigDescription(
                    "Radius around the mark whose hostile radars are jammed by the geomagnetic phase of " +
                    "an EMP shock. Friendly units are unaffected.",
                    new AcceptableValueRange<float>(1000f, 60000f)));
            FlareBarrageCost = config.Bind("Support", "FlareBarrageCost", 500f,
                new ConfigDescription(
                    "Allocation charged for one Flare Barrage countermeasure rocket, before " +
                    "CostMultiplier and the Logistics Officer perk.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            FlareBarrageRadius = config.Bind("Support", "FlareBarrageRadiusMeters", 4000f,
                new ConfigDescription(
                    "Radius around the mark in which hostile IR missiles are seduced and misguided.",
                    new AcceptableValueRange<float>(500f, 15000f)));
            FlareBarrageCount = config.Bind("Support", "FlareBarrageCount", 36,
                new ConfigDescription(
                    "Number of authentic pyrotechnic flares dispersed in the initial airburst wave.",
                    new AcceptableValueRange<int>(12, 64)));
            FlareBarrageDuration = config.Bind("Support", "FlareBarrageDurationSeconds", 15f,
                new ConfigDescription(
                    "Total duration in seconds of the continuous flare countermeasure barrage.",
                    new AcceptableValueRange<float>(5f, 45f)));
            JtacMarkCost = config.Bind("Support", "JtacMarkCost", 400f,
                new ConfigDescription(
                    "Allocation charged for one JTAC mark or unlase, before CostMultiplier and " +
                    "the Logistics Officer perk. Recovery costs the same as the mark.",
                    new AcceptableValueRange<float>(0f, 20000f)));
            JtacMarkDuration = config.Bind("Support", "JtacMarkDurationSeconds", 120f,
                new ConfigDescription(
                    "Seconds a JTAC lase lives before the host clears it. Host-authoritative.",
                    new AcceptableValueRange<float>(30f, 300f)));

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

            TeamFlareCooldown = config.Bind("Support", "TeamFlareCooldownSeconds", 90f,
                new ConfigDescription(
                    "Faction-wide cooldown after a flare barrage goes out; the whole faction's " +
                    "rocket team re-tasks. 0 turns the gate off. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 900f)));
            TeamFortifyCooldown = config.Bind("Support", "TeamFortifyCooldownSeconds", 120f,
                new ConfigDescription(
                    "Faction-wide cooldown after a zone fortification goes out. 0 turns the gate off. " +
                    "Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 900f)));
            TeamRelocateCooldown = config.Bind("Support", "TeamRelocateCooldownSeconds", 300f,
                new ConfigDescription(
                    "Faction-wide cooldown after a station relocation burn. 0 turns the gate off. " +
                    "Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 1800f)));
            TeamIsolateCooldown = config.Bind("Support", "TeamIsolateCooldownSeconds", 45f,
                new ConfigDescription(
                    "Faction-wide cooldown after a CYBER console ISOLATE goes out. 0 turns the gate off. " +
                    "Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 900f)));
            TeamCostPerExtra = config.Bind("Support", "TeamCostPerExtraPilot", 0.15f,
                new ConfigDescription(
                    "Consumable abilities (flare barrage, zone fortification) cost this much more for " +
                    "every faction pilot after the first. 0 charges every faction the base price. " +
                    "Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 1f)));
            TeamCostCap = config.Bind("Support", "TeamCostCap", 2f,
                new ConfigDescription(
                    "The faction-size price multiplier stops here. Host-authoritative.",
                    new AcceptableValueRange<float>(1f, 4f)));
            TeamOwnershipGuards = config.Bind("Support", "TeamOwnershipGuards", true,
                "Offensive team assets answer to the pilot who paid for or launched them: a teammate " +
                "may not jettison a module, deorbit a station others paid for, recall a team in the " +
                "field or work a live breach it does not own while the owner is still present. " +
                "Host-authoritative.");

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
                    "Scales every SPEC OPS price (raise a team 1000; recon 400, sabotage 700, seize 900, " +
                    "steal 600; SPOT 250, SKYWATCH 350, EAVESDROP 300, HUNT 650, SUPPRESS 500), before " +
                    "CostMultiplier. Host-authoritative.",
                    new AcceptableValueRange<float>(0f, 10f)));

            ArtilleryDefinitionKey = config.Bind("Support", "FireMissionDefinitionKey", string.Empty,
                "Exact jsonKey of the missile used by Rod from God and EMP shock. Empty auto-picks " +
                "a non-nuclear vanilla missile. Only non-nuclear missiles with a yield of 200 or " +
                "less and a ballistic aimpoint seeker are accepted. Check the startup log for the definitions this game build loaded.");
            PrsmDefinitionKey = config.Bind("Support", "PrsmDefinitionKey", string.Empty,
                "Exact jsonKey of the missile used by the PRSM strike. Empty auto-picks " +
                "a non-nuclear vanilla missile. Only non-nuclear missiles with a yield of 200 or " +
                "less and a ballistic aimpoint seeker are accepted. Check the startup log for the definitions this game build loaded.");
            CruiseDefinitionKey = config.Bind("Support", "CruiseDefinitionKey", string.Empty,
                "Exact jsonKey of the missile used by the cruise strike. Empty auto-picks " +
                "a non-nuclear vanilla missile. Only non-nuclear missiles with a yield of 200 or " +
                "less and a cruise seeker are accepted. The trajectory (dive vs skim) is fixed by the " +
                "chosen definition and read live off the missile; pick a top-attack definition for dive. Check the startup log for the definitions this game build loaded.");

        }
    }
}
