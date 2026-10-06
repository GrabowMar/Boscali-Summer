using BepInEx.Configuration;

namespace BoscaliSummer.Modules.DynamicOperations.Configuration
{
    internal sealed class DynamicOperationsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<float> RewardMultiplier { get; }
        public ConfigEntry<float> ContractTeamShare { get; }

        public DynamicOperationsSettings(ConfigFile config)
        {
            Enabled = config.Bind("DynamicOperations", "Enabled", true,
                "Pool of 17 contracts: combat, patrol, jamming, insertion, rescue/return, reconnaissance, strike assessment, supply escort/interdiction, repair cover, jammer hunts, intelligence return and aftermath surveys. Accept through MIS > SECONDARY; server-owned rewards. Restart after changing. MIS > SECONDARY requires Command.ExpandedMapUi.");
            RewardMultiplier = config.Bind("DynamicOperations", "RewardMultiplier", 1f,
                new ConfigDescription("Scale mission money and XP. Money uses the normal faction tax; XP is vanilla mission score.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            ContractTeamShare = config.Bind("DynamicOperations", "ContractTeamShare", 0.25f,
                new ConfigDescription("Fraction of a contract's money and XP each faction pilot earns when a teammate " +
                    "completes it. The completing pilot always earns the full reward, and so does a lone pilot.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }
    }
}
