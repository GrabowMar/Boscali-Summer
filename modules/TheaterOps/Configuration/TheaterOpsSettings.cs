using BepInEx.Configuration;

namespace BoscaliSummer.Modules.TheaterOps.Configuration
{
    /// <summary>Faction scope for the inactive legacy director.</summary>
    internal enum TheaterDirectorFactions
    {
        /// <summary>Every faction with a connected player, plus the listen host's own.</summary>
        PlayerFactions,
        AllFactions,

        /// <summary>Only the listen host's faction; a dedicated server runs none.</summary>
        HostFaction,
    }

    /// <summary>
    /// Living Front gates and retained legacy planner settings. Vanilla supplies the vehicles; the optional
    /// frontline route gives eligible depot ground AI staged destinations through its normal
    /// objective query.
    /// </summary>
    internal sealed class TheaterOpsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> FrontlineTacticsEnabled { get; }
        public ConfigEntry<TheaterDirectorFactions> DirectorFactions { get; }

        // Retained for the legacy director's migration code. Living Front does not read these.

        /// <summary>Pool cost of the staff work behind one offensive, in millions.</summary>
        public ConfigEntry<float> OperationOverheadCost { get; }

        /// <summary>Pool escrow of one wave slot, in millions.</summary>
        public ConfigEntry<float> OperationWaveBudget { get; }

        /// <summary>How long the staff takes to gather the forces.</summary>
        public ConfigEntry<float> OperationMusterSeconds { get; }

        /// <summary>How long the staff takes to plan the assault. Friendly staff cohesion shortens it.</summary>
        public ConfigEntry<float> OperationPlanSeconds { get; }

        /// <summary>Countdown between naming the objective and H-hour.</summary>
        public ConfigEntry<float> OperationLaunchDelaySeconds { get; }

        /// <summary>Pause between delivered waves.</summary>
        public ConfigEntry<float> OperationWaveSeconds { get; }

        /// <summary>Pause before retrying a wave when nothing was ready to move.</summary>
        public ConfigEntry<float> OperationWaveRetrySeconds { get; }

        /// <summary>How long a push whose waves are all spent waits for reinforcement.</summary>
        public ConfigEntry<float> OperationHoldSeconds { get; }

        /// <summary>Hard ceiling on a launched offensive before it reports and refunds.</summary>
        public ConfigEntry<float> OperationAssaultSeconds { get; }

        public TheaterOpsSettings(ConfigFile config)
        {
            const string section = "TheaterOps";
            Enabled = config.Bind(section, "Enabled", true,
                "Run Living Front: one host-owned staff per faction offers bounded operations and " +
                "directs nearby autonomous forces. Native production, combat and player orders remain authoritative.");
            FrontlineTacticsEnabled = config.Bind(section, "FrontlineTacticsEnabled", true,
                "Stage newly depot-spawned AI ground vehicles at the front, spread them into a line, " +
                "and let offensive groups advance after assembling. Vanilla supply and player orders remain authoritative.");
            DirectorFactions = config.Bind(section, "DirectorFactions", TheaterDirectorFactions.PlayerFactions,
                "Inactive legacy director setting; Living Front reviews all real factions. PlayerFactions runs every " +
                "faction with at least one connected player (plus the listen host's own), AllFactions runs every " +
                "faction, HostFaction runs only the listen host's side (a dedicated server then runs none).");

            OperationOverheadCost = config.Bind(section, "OperationOverheadCost", 25f,
                "Inactive legacy planner setting. Faction-pool cost of the staff work behind one offensive (millions). Non-refundable " +
                "only in the sense that it is escrowed with the waves: whatever is unspent returns at the end.");
            OperationWaveBudget = config.Bind(section, "OperationWaveBudget", 45f,
                "Inactive legacy planner setting. Faction-pool escrow of one wave slot (millions). Each delivered wave buys the heaviest " +
                "convoy group the slot's escrow covers.");
            OperationMusterSeconds = config.Bind(section, "OperationMusterSeconds", 75f,
                "Inactive legacy planner setting. Seconds the staff takes to gather the forces before planning begins.");
            OperationPlanSeconds = config.Bind(section, "OperationPlanSeconds", 45f,
                "Inactive legacy planner setting. Seconds the staff takes to plan the assault. Friendly staff cohesion shortens it.");
            OperationLaunchDelaySeconds = config.Bind(section, "OperationLaunchDelaySeconds", 15f,
                "Inactive legacy planner setting. Seconds between naming the objective and H-hour. The offensive launches itself; the " +
                "commander can bring it forward.");
            OperationWaveSeconds = config.Bind(section, "OperationWaveSeconds", 35f,
                "Inactive legacy planner setting. Seconds between delivered supply waves during an assault.");
            OperationWaveRetrySeconds = config.Bind(section, "OperationWaveRetrySeconds", 8f,
                "Inactive legacy planner setting. Seconds before retrying a wave that found no convoy group ready to move.");
            OperationHoldSeconds = config.Bind(section, "OperationHoldSeconds", 90f,
                "Inactive legacy planner setting. Seconds a push waits once its last funded wave is on the road, for the commander to " +
                "fund another or let the offensive report.");
            OperationAssaultSeconds = config.Bind(section, "OperationAssaultSeconds", 600f,
                "Inactive legacy planner setting. Hard ceiling on a launched offensive before it reports and refunds its unspent escrow.");
        }
    }
}
