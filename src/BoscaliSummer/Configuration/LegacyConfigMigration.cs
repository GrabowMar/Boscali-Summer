using BepInEx.Configuration;

namespace BoscaliSummer
{
    internal static class LegacyConfigMigration
    {
        public static void RemoveEntries(ConfigFile config)
        {
            // Bind-then-remove consumes old values from BepInEx's orphan table as well as
            // active entries, so the next save leaves a clean compact config file.
            BindAndRemove(config, "Smoke", "Enabled", true);
            BindAndRemove(config, "Smoke", "Strength", 1f);
            BindAndRemove(config, "Smoke", "Ammo", 8);
            BindAndRemove(config, "Smoke", "CooldownSeconds", 1.25f);
            BindAndRemove(config, "Smoke", "PuffsPerSide", 5);
            BindAndRemove(config, "Smoke", "LifetimeSeconds", 16f);
            BindAndRemove(config, "Smoke", "MaximumRadius", 38f);
            BindAndRemove(config, "Smoke", "LogicalPuffLimit", 96);

            BindAndRemove(config, "Fires", "BulletChance", 0.00075f);
            BindAndRemove(config, "Fires", "ExplosiveChance", 0.025f);
            BindAndRemove(config, "Fires", "MaximumActiveSites", 24);
            BindAndRemove(config, "Fires", "LifetimeSeconds", 90f);
            BindAndRemove(config, "Fires", "MergeRadius", 72f);
            BindAndRemove(config, "Fires", "CellCooldownSeconds", 8f);
            BindAndRemove(config, "Fires", "ScorchRadius", 45f);
            BindAndRemove(config, "Fires", "ScorchRadiusScale", 0.72f);
            BindAndRemove(config, "Fires", "ForestIndexCellSize", 32f);
            BindAndRemove(config, "Fires", "SpreadEnabled", true);
            BindAndRemove(config, "Fires", "SpreadIntervalSeconds", 11f);
            BindAndRemove(config, "Fires", "SpreadDistance", 62f);
            BindAndRemove(config, "Fires", "SpreadGenerations", 2);

            BindAndRemove(config, "Destruction", "MaximumPersistentRuins", 256);
            BindAndRemove(config, "Destruction", "MaximumRuinSmokeVisuals", 24);
            BindAndRemove(config, "Destruction", "MaximumCollapseBursts", 4);
            BindAndRemove(config, "Destruction", "HotRuinSeconds", 120f);
            BindAndRemove(config, "Buildings", "DamagedBelowHitPoints", 58f);
            BindAndRemove(config, "Garrisons", "MinimumPerZone", 2);
            BindAndRemove(config, "Garrisons", "MaximumPerZone", 4);
            BindAndRemove(config, "Garrisons", "DefenseDefinitionKey", string.Empty);
            BindAndRemove(config, "Garrisons", "DamageShaderEnabled", true);
            BindAndRemove(config, "Garrisons", "DamageHeatGlowEnabled", true);
            BindAndRemove(config, "Radio", "Volume", 0.65f);

            // Support costs stopped being hand-picked constants and became vanilla-value
            // derived. The old keys are purged rather than reused: an existing config would
            // otherwise keep charging 10 and 8 allocation against a four-figure balance.
            BindAndRemove(config, "Support", "FortificationCost", 10f);
            BindAndRemove(config, "Support", "ArtilleryCost", 8f);
            BindAndRemove(config, "Support", "VehicleAirdrops", true);
            BindAndRemove(config, "Support", "VehicleAirdropCost", 12f);
            BindAndRemove(config, "Support", "ArtilleryDefinitionKey", string.Empty);
        }

        private static void BindAndRemove<T>(ConfigFile config, string section, string key, T defaultValue)
        {
            config.Bind(section, key, defaultValue);
            config.Remove(new ConfigDefinition(section, key));
        }
    }
}
