using BepInEx.Configuration;

namespace BoscaliSummer.Features.DynamicOperations.Configuration
{
    internal sealed class DynamicOperationsSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<float> RewardMultiplier { get; }

        public DynamicOperationsSettings(ConfigFile config)
        {
            Enabled = config.Bind("DynamicOperations", "Enabled", false,
                "Experimental pool of 17 contracts: combat, patrol, jamming, insertion, rescue/return, reconnaissance, strike assessment, supply escort/interdiction, repair cover, jammer hunts, intelligence return and aftermath surveys. Accept through MIS > SECONDARY; server-owned rewards. In-game validation pending. Restart after changing. MIS > SECONDARY requires Command.ExpandedMapUi.");
            RewardMultiplier = config.Bind("DynamicOperations", "RewardMultiplier", 1f,
                new ConfigDescription("Scale mission money and XP. Money uses the normal faction tax; XP is vanilla mission score.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
        }
    }
}
