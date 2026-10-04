using BepInEx.Configuration;

namespace BoscaliSummer.Modules.Support.Configuration
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

        public ConfigEntry<float> SarSceneRadius { get; }
        public ConfigEntry<float> OpticalSceneRadius { get; }
        public ConfigEntry<float> ElintRadius { get; }

        public ConfigEntry<int> CruiseSalvo { get; }
        public ConfigEntry<int> CruiseLiveCap { get; }
        public ConfigEntry<float> IntelFreshSeconds { get; }
        public ConfigEntry<float> IntelGateRadius { get; }
        public ConfigEntry<float> EmpRadius { get; }
        public ConfigEntry<float> FlareBarrageRadius { get; }
        public ConfigEntry<int> FlareBarrageCount { get; }
        public ConfigEntry<float> FlareBarrageDuration { get; }
        public ConfigEntry<float> JtacMarkDuration { get; }

        public ConfigEntry<float> MaximumRange { get; }
        public ConfigEntry<float> RequestCooldown { get; }

        public ConfigEntry<KeyboardShortcut> CallKey1 { get; }
        public ConfigEntry<KeyboardShortcut> CallKey2 { get; }
        public ConfigEntry<KeyboardShortcut> CallKey3 { get; }
        public ConfigEntry<KeyboardShortcut> CallKey4 { get; }
        public ConfigEntry<float> PriceKnob { get; }
        public ConfigEntry<float> EarnKnob { get; }

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
                "Radar scan: images a scene and the host " +
                "reveals stationary ground contacts in it. Spawns nothing.");
            FortifyEnabled = config.Bind("Support", "Fortification", true,
                "Reinforce a friendly controlled zone. Requires the Garrisons feature; the " +
                "request is refused, and nothing is charged, when it cannot place defenders.");
            ArtilleryEnabled = config.Bind("Support", "RodFromGod", true,
                "Orbital kinetic strike: one high-velocity projectile onto " +
                "the mark. Uses the FireMissionDefinitionKey missile.");
            PrsmEnabled = config.Bind("Support", "PrsmStrike", true,
                "PRSM strike: one offboard ballistic missile onto the mark. Needs fresh HQ intel " +
                "at the target. Uses the PrsmDefinitionKey missile.");
            CruiseEnabled = config.Bind("Support", "CruiseStrike", true,
                "Cruise strike: a bounded salvo of offboard cruise missiles onto the mark, separated " +
                "by the seeker's native formation spacing. Needs fresh HQ intel at the target. Uses the CruiseDefinitionKey missile.");
            EmpEnabled = config.Bind("Support", "EmpShock", true,
                "EMP shock: a high-altitude airburst. The prompt pulse upsets electronics; the " +
                "geomagnetic disturbance jams hostile radars across a wide area while friendly " +
                "units keep theirs. Uses the " +
                "FireMissionDefinitionKey missile as a delivery visual.");
            ElintEnabled = config.Bind("Support", "ElintSweep", true,
                "ELINT sweep: locates enemy ground and ship " +
                "radars that are emitting near the mark. Spawns nothing.");
            MtiEnabled = config.Bind("Support", "MtiSweep", true,
                "MTI sweep: tracks moving enemy ground " +
                "contacts near the mark. Shares the radar scan tasking. Spawns nothing.");
            FlareBarrageEnabled = config.Bind("Support", "FlareBarrage", true,
                "Flare barrage: launches an airburst countermeasure missile that disperses a cluster of " +
                "intense pyrotechnic flares, seducing and misguiding hostile IR-seeking missiles in the area. " +
                "Friendly missiles fly through.");

            SarSceneRadius = config.Bind("Support", "SarSceneRadiusMeters", 1000f,
                new ConfigDescription(
                    "Half-width of a radar scan scene. " +
                    "Stationary ground contacts inside it are revealed; movers faster than 4 m/s smear and are not.",
                    new AcceptableValueRange<float>(400f, 4000f)));
            OpticalSceneRadius = config.Bind("Support", "OpticalSceneRadiusMeters", 1000f,
                new ConfigDescription(
                    "Half-width of a clear-day optical camera window. Cloud and rain shrink it (full cover halves it) and " +
                    "the optical bird refuses at night; ground units inside it are revealed to the faction.",
                    new AcceptableValueRange<float>(400f, 4000f)));
            ElintRadius = config.Bind("Support", "ElintSweepRadiusMeters", 8000f,
                new ConfigDescription(
                    "Radius around the mark searched for emitting enemy radars.",
                    new AcceptableValueRange<float>(1000f, 40000f)));
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
            IntelGateRadius = config.Bind("Support", "IntelGateRadiusMeters", 1000f,
                new ConfigDescription(
                    "Radius around the grid searched for a fresh HQ track before a strike releases.",
                    new AcceptableValueRange<float>(100f, 10000f)));
            EmpRadius = config.Bind("Support", "EmpShockRadiusMeters", 12000f,
                new ConfigDescription(
                    "Radius around the mark whose hostile radars are jammed by the geomagnetic phase of " +
                    "an EMP shock. Friendly units are unaffected.",
                    new AcceptableValueRange<float>(1000f, 60000f)));
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

            CallKey1 = BindCallKey(config, 1);
            CallKey2 = BindCallKey(config, 2);
            CallKey3 = BindCallKey(config, 3);
            CallKey4 = BindCallKey(config, 4);
            PriceKnob = config.Bind("Support", "CallPriceScale", 1f,
                new ConfigDescription("Host: scales every CALL price (1 = spec prices).", new AcceptableValueRange<float>(0.25f, 4f)));
            EarnKnob = config.Bind("Support", "CreditEarnScale", 1f,
                new ConfigDescription("Host: scales every CR payout (1 = spec rates).", new AcceptableValueRange<float>(0.25f, 4f)));
        }

        private static ConfigEntry<KeyboardShortcut> BindCallKey(ConfigFile config, int slot) =>
            config.Bind("Support Keys", "CallSlot" + slot, KeyboardShortcut.Empty,
                "Press once to arm favourite CALL " + slot + ", again within 8 s to fire.");
    }
}
