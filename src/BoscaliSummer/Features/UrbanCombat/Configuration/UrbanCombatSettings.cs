using BepInEx.Configuration;

namespace BoscaliSummer.Features.UrbanCombat.Configuration
{
    internal sealed class UrbanCombatSettings
    {
        public readonly ConfigEntry<bool> GarrisonsEnabled;
        public readonly ConfigEntry<int> GarrisonsPerZone;
        public readonly ConfigEntry<int> TroopsPerDeploy;

        public UrbanCombatSettings(ConfigFile config)
        {
            GarrisonsEnabled = config.Bind("Garrisons", "Enabled", true,
                "Turn a few civilian buildings near owned airbases into defensive positions. " +
                "Also required by the Zone Fortification support action. " +
                "Host-authoritative: on a server, only the host's value applies.");
            GarrisonsPerZone = config.Bind("Garrisons", "BuildingsPerZone", 3,
                new ConfigDescription(
                    "Occupied civilian buildings per controlled zone. A successful Zone " +
                    "Fortification request adds one above the zone's current count. 0 leaves " +
                    "zones undefended without disabling the feature. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<int>(0, 6)));
            TroopsPerDeploy = config.Bind("Air Assault", "InfantryPerInsertion", 8,
                new ConfigDescription(
                    "Visual infantry deployed per air-assault insertion. Vanilla defense " +
                    "emplacements provide the authoritative combat behavior.",
                    new AcceptableValueRange<int>(2, 12)));
        }
    }
}
