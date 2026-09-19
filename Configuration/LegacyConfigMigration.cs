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
            BindAndRemove(config, "Garrisons", "StrongholdHitPoints", 2500f);
            BindAndRemove(config, "Garrisons", "StrongholdPierceArmor", 25f);
            BindAndRemove(config, "Garrisons", "StrongholdBlastArmor", 50f);
            BindAndRemove(config, "Garrisons", "StrongholdDefenseType", "auto");
            BindAndRemove(config, "Air Assault", "InfantryPerFastRope", 8);
            BindAndRemove(config, "Radio", "Volume", 0.65f);

            // The sector grid moved from a fixed dimension count to cells on the map's own
            // base grid, so the old size knob no longer means anything.
            BindAndRemove(config, "Command", "GridResolution", 32);

            // Support costs stopped being hand-picked constants and became vanilla-value
            // derived. The old keys are purged rather than reused: an existing config would
            // otherwise keep charging 10 and 8 allocation against a four-figure balance.
            BindAndRemove(config, "Support", "FortificationCost", 10f);
            BindAndRemove(config, "Support", "ArtilleryCost", 8f);
            BindAndRemove(config, "Support", "VehicleAirdrops", true);
            BindAndRemove(config, "Support", "VehicleAirdropCost", 12f);
            BindAndRemove(config, "Support", "ArtilleryDefinitionKey", string.Empty);

            // The original dynamic weather module was removed wholesale. Purge both its
            // final settings and the presentation keys retired during its earlier rework so
            // a future lightweight implementation starts with no inherited configuration.
            // Remove or revise this block before a replacement reuses any [Weather] key.
            BindAndRemove(config, "Weather", "Enabled", true);
            BindAndRemove(config, "Weather", "ForecastSteps", 8);
            BindAndRemove(config, "Weather", "ForecastStepMinutes", 3f);
            BindAndRemove(config, "Weather", "DebugControls", false);
            BindAndRemove(config, "Weather", "DebugKey", UnityEngine.KeyCode.F11);
            BindAndRemove(config, "Weather", "DebugKeyRequiresCtrl", true);
            BindAndRemove(config, "Weather", "RainEffects", true);
            BindAndRemove(config, "Weather", "RainOnCanopy", true);
            BindAndRemove(config, "Weather", "RainAudio", true);
            BindAndRemove(config, "Weather", "RainEffectDensity", 1f);
            BindAndRemove(config, "Weather", "RainVolume", 1f);
            BindAndRemove(config, "Weather", "Hud", true);
            BindAndRemove(config, "Weather", "Supercells", true);
            BindAndRemove(config, "Weather", "SupercellDetail", 0.6f);
            BindAndRemove(config, "Weather", "RadarRangeKm", 40);
            BindAndRemove(config, "Weather", "ReplaceVanillaClouds", false);
            BindAndRemove(config, "Weather", "CloudSortFudge", -100f);
        }

        private static void BindAndRemove<T>(ConfigFile config, string section, string key, T defaultValue)
        {
            config.Bind(section, key, defaultValue);
            config.Remove(new ConfigDefinition(section, key));
        }
    }
}
