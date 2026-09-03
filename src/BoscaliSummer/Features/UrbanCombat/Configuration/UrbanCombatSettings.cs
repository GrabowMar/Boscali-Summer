using BepInEx.Configuration;

namespace BoscaliSummer.Features.UrbanCombat.Configuration
{
    internal sealed class UrbanCombatSettings
    {
        public readonly ConfigEntry<bool> GarrisonsEnabled;
        public readonly ConfigEntry<int> GarrisonsPerZone;
        public readonly ConfigEntry<float> StrongholdHitPoints;
        public readonly ConfigEntry<float> StrongholdPierceArmor;
        public readonly ConfigEntry<float> StrongholdBlastArmor;
        public readonly ConfigEntry<string> StrongholdDefenseType;
        public readonly ConfigEntry<bool> DamageShaderEnabled;
        public readonly ConfigEntry<bool> DamageHeatGlowEnabled;

        public int GarrisonsMinimum => GarrisonsPerZone.Value;
        public int GarrisonsMaximum => GarrisonsPerZone.Value;
        public string GarrisonDefinitionKey => StrongholdDefenseType.Value;

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
            StrongholdHitPoints = config.Bind("Garrisons", "StrongholdHitPoints", 2500f,
                new ConfigDescription(
                    "Total durability of an occupied building stronghold (vanilla civilian buildings have 100 HP). " +
                    "Requires substantial dedicated anti-fortification ordnance to destroy. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<float>(500f, 10000f)));
            StrongholdPierceArmor = config.Bind("Garrisons", "StrongholdPierceArmor", 25f,
                new ConfigDescription(
                    "Pierce damage subtracted before impacting stronghold HP. " +
                    "Small arms and light autocannons cannot penetrate thick reinforced concrete walls.",
                    new AcceptableValueRange<float>(0f, 100f)));
            StrongholdBlastArmor = config.Bind("Garrisons", "StrongholdBlastArmor", 50f,
                new ConfigDescription(
                    "Blast damage subtracted before impacting stronghold HP. " +
                    "Protects internal garrison from near-miss shrapnel and small rocket blasts.",
                    new AcceptableValueRange<float>(0f, 200f)));
            StrongholdDefenseType = config.Bind("Garrisons", "StrongholdDefenseType", "auto",
                "Building definition key for the stronghold defensive armament. " +
                "'auto' chooses pillbox or heavy emplacements over standard sandbag bunkers. " +
                "Or specify an exact unit key such as pillbox, Emplacement1_ATGM, or Emplacement1_MG.");
            DamageShaderEnabled = config.Bind("Garrisons", "DamageShaderEnabled", true,
                "Enable progressive structural damage, surface charring, and soot staining on buildings via URP MaterialPropertyBlocks.");
            DamageHeatGlowEnabled = config.Bind("Garrisons", "DamageHeatGlowEnabled", true,
                "Enable dynamic incandescent thermal heat flash on building surfaces at impact points that cools over time.");
        }
    }
}
