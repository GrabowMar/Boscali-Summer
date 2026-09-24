using BepInEx.Configuration;

namespace BoscaliSummer.Features.UrbanCombat.Configuration
{
    internal sealed class UrbanCombatSettings
    {
        public readonly ConfigEntry<bool> GarrisonsEnabled;
        public readonly ConfigEntry<int> GarrisonsPerZone;
        public readonly ConfigEntry<int> TroopsPerDeploy;
        public readonly ConfigEntry<bool> SiegeEnabled;
        public readonly ConfigEntry<float> SiegeDefenseScale;
        public readonly ConfigEntry<bool> SiegeArmorBonus;
        public readonly ConfigEntry<bool> AmbienceEnabled;
        public readonly ConfigEntry<float> AmbienceVolume;

        public UrbanCombatSettings(ConfigFile config)
        {
            GarrisonsEnabled = config.Bind("Garrisons", "Enabled", true,
                "Turn a few civilian buildings near owned airbases into defensive positions. " +
                "Also required by the Zone Fortification support action. " +
                "Host-authoritative: on a server, only the host's value applies.");
            GarrisonsPerZone = config.Bind("Garrisons", "BuildingsPerZone", 2,
                new ConfigDescription(
                    "Occupied civilian buildings per controlled zone. Urban zones add up to three " +
                    "above this while siege is on; a successful Zone Fortification request adds one " +
                    "above the zone's current count. 0 leaves zones undefended without disabling " +
                    "the feature. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<int>(0, 6)));
            TroopsPerDeploy = config.Bind("Air Assault", "InfantryPerInsertion", 8,
                new ConfigDescription(
                    "Infantry deployed per transport paradrop, in one stick out the cargo access. Capped at the Ibis " +
                    "fast-rope squad size of eight. Vanilla defense emplacements provide the authoritative combat behavior.",
                    new AcceptableValueRange<int>(2, 8)));
            SiegeEnabled = config.Bind("Garrisons", "UrbanSiege", true,
                "Cities resist capture: urban zones gain capture defense from their size and intact rooftop nests, " +
                "and control cannot drain past the strongpoint floor until the nests fall. Requires zone garrisons. " +
                "Host-authoritative: on a server, only the host's value applies.");
            SiegeDefenseScale = config.Bind("Garrisons", "UrbanDefenseScale", 1f,
                new ConfigDescription(
                    "Overall urban siege strength. 0 is vanilla capture pacing everywhere; higher values slow city " +
                    "captures further and raise the strongpoint floor. Requires urban siege. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<float>(0f, 3f)));
            SiegeArmorBonus = config.Bind("Garrisons", "UrbanArmorBonus", true,
                "Ground vehicles lead urban assaults: their capture strength rises inside towns, cities and metros, " +
                "so infantry alone takes a city only slowly. Requires urban siege. " +
                "Host-authoritative: on a server, only the host's value applies.");
            AmbienceEnabled = config.Bind("Garrisons", "UrbanAmbience", true,
                "Air-raid siren near cities, heard from the camera like any other positional sound. " +
                "Client-local: each player's own value applies, and headless servers stay silent.");
            AmbienceVolume = config.Bind("Garrisons", "UrbanAmbienceVolume", 0.7f,
                new ConfigDescription(
                    "Master volume for the urban siren. 0 is silent. " +
                    "Client-local: each player's own value applies.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }
    }
}
