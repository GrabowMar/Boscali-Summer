using BepInEx.Configuration;

namespace BoscaliSummer.Features.UrbanCombat.Configuration
{
    internal sealed class UrbanCombatSettings
    {
        public readonly ConfigEntry<bool> GarrisonsEnabled;
        public readonly ConfigEntry<int> GarrisonsPerZone;

        public int GarrisonsMinimum => GarrisonsPerZone.Value;
        public int GarrisonsMaximum => GarrisonsPerZone.Value;
        public string GarrisonDefinitionKey => string.Empty;

        public UrbanCombatSettings(ConfigFile config)
        {
            GarrisonsEnabled = config.Bind("Garrisons", "Enabled", true,
                "Turn a few civilian buildings near owned airbases into defensive positions.");
            GarrisonsPerZone = config.Bind("Garrisons", "BuildingsPerZone", 3,
                new ConfigDescription(
                    "Number of occupied civilian buildings per controlled zone.",
                    new AcceptableValueRange<int>(0, 6)));
        }
    }
}
