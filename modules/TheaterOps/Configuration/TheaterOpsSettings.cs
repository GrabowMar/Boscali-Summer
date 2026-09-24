using BepInEx.Configuration;

namespace BoscaliSummer.Features.TheaterOps.Configuration
{
    /// <summary>
    /// Theater priority and offensive pacing. Vanilla supplies the vehicles; the optional
    /// frontline route gives eligible depot ground AI staged destinations through its normal
    /// objective query.
    /// </summary>
    internal sealed class TheaterOpsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> MapMarkerEnabled { get; }
        public ConfigEntry<bool> FrontlineTacticsEnabled { get; }

        // ---- Offensive planner ------------------------------------------------------------

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
                "Run the theater priority: friendly AI reinforcement delivery and movement with no " +
                "better order favour the host-chosen objective.");
            MapMarkerEnabled = config.Bind(section, "MapMarkerEnabled", true,
                "Draw the local faction's main effort as a diamond on the vanilla map. Client-local presentation.");
            FrontlineTacticsEnabled = config.Bind(section, "FrontlineTacticsEnabled", true,
                "Stage newly depot-spawned AI ground vehicles at the front, spread them into a line, " +
                "and let offensive groups advance after assembling. Vanilla supply and player orders remain authoritative.");

            OperationOverheadCost = config.Bind(section, "OperationOverheadCost", 25f,
                "Faction-pool cost of the staff work behind one offensive (millions). Non-refundable " +
                "only in the sense that it is escrowed with the waves: whatever is unspent returns at the end.");
            OperationWaveBudget = config.Bind(section, "OperationWaveBudget", 45f,
                "Faction-pool escrow of one wave slot (millions). Each delivered wave buys the heaviest " +
                "convoy group the slot's escrow covers.");
            OperationMusterSeconds = config.Bind(section, "OperationMusterSeconds", 75f,
                "Seconds the staff takes to gather the forces before planning begins.");
            OperationPlanSeconds = config.Bind(section, "OperationPlanSeconds", 45f,
                "Seconds the staff takes to plan the assault. Friendly staff cohesion shortens it.");
            OperationLaunchDelaySeconds = config.Bind(section, "OperationLaunchDelaySeconds", 15f,
                "Seconds between naming the objective and H-hour. The offensive launches itself; the " +
                "commander can bring it forward.");
            OperationWaveSeconds = config.Bind(section, "OperationWaveSeconds", 35f,
                "Seconds between delivered supply waves during an assault.");
            OperationWaveRetrySeconds = config.Bind(section, "OperationWaveRetrySeconds", 8f,
                "Seconds before retrying a wave that found no convoy group ready to move.");
            OperationHoldSeconds = config.Bind(section, "OperationHoldSeconds", 90f,
                "Seconds a push waits once its last funded wave is on the road, for the commander to " +
                "fund another or let the offensive report.");
            OperationAssaultSeconds = config.Bind(section, "OperationAssaultSeconds", 600f,
                "Hard ceiling on a launched offensive before it reports and refunds its unspent escrow.");
        }
    }
}
