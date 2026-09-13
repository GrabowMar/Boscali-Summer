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
                "Experimental optional contracts: capture, defense, ground/air hunts, patrol, jamming and Ibis ground/rooftop insertions. Accept through MIS > SECONDARY; server-owned money, XP, morale and battlefield rewards. Requires an in-game validation pass. Restart after changing. MIS > SECONDARY requires Command.ExpandedMapUi.");
            RewardMultiplier = config.Bind("DynamicOperations", "RewardMultiplier", 1f,
                new ConfigDescription("Scale mission money and XP. Money uses the normal faction tax; XP is vanilla mission score.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
        }
    }
}
