using System;
using BepInEx.Configuration;

namespace BoscaliSummer
{
    /// <summary>
    /// Small, high-level configuration surface for the current pack. Detailed particle,
    /// spread, scorch and aftermath values are deliberately derived constants: exposing all
    /// of those implementation controls made old configs easy to mis-tune and could defeat
    /// the bounded visual budgets.
    /// </summary>
    internal sealed class ModConfiguration
    {
        public readonly ConfigEntry<bool> FiresEnabled;
        public readonly ConfigEntry<float> FireIntensity;
        public readonly ConfigEntry<bool> DemolishUnoccupiedBuildings;

        public readonly ConfigEntry<bool> BuildingDamageEnabled;

        public readonly ConfigEntry<bool> GarrisonsEnabled;
        public readonly ConfigEntry<int> GarrisonsPerZone;

        public readonly ConfigEntry<bool> VerboseLogging;

        // Derived fire tuning. Intensity changes ignition and spectacle together while the
        // hard visual/site budgets stay fixed for predictable performance.
        public float BulletIgnitionChance => 0.0025f * (0.65f + 0.35f * FireIntensity.Value);
        public float ExplosiveIgnitionChance => 0.06f * (0.65f + 0.35f * FireIntensity.Value);
        // Vehicle losses are a secondary ignition source: substantial enough to sell a
        // burning-out column, but lower than a direct missile strike. Kept derived so the
        // compact config does not grow another performance-sensitive knob.
        public float VehicleExplosionIgnitionChance => ExplosiveIgnitionChance * 0.72f;
        public int MaxActiveFires => 24;
        public float FireLifetime => 90f * (0.82f + 0.18f * FireIntensity.Value);
        public float FireMergeRadius => 72f * (0.88f + 0.12f * FireIntensity.Value);
        public float FireCellCooldown => 8f;
        public float ScorchRadius => 45f;
        public float ScorchRadiusScale => 0.72f;
        public float ForestCellSize => 32f;
        public bool FireSpreadEnabled => FiresEnabled.Value;
        public float FireSpreadInterval => 11f / (0.88f + 0.12f * FireIntensity.Value);
        public float FireSpreadDistance => 62f * (0.90f + 0.10f * FireIntensity.Value);
        public int FireSpreadGenerations => 2;

        // Destruction budgets are fixed in the current performance plan.
        public int MaximumPersistentRuins => 256;
        public int MaximumRuinSmokeVisuals => 24;
        public int MaximumCollapseBursts => 4;
        public float HotRuinSeconds => 120f;

        public float BuildingDamagedHitPoints => 58f;
        public int GarrisonsMinimum => GarrisonsPerZone.Value;
        public int GarrisonsMaximum => GarrisonsPerZone.Value;
        public string GarrisonDefinitionKey => string.Empty;

        public ModConfiguration(ConfigFile config)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                FiresEnabled = config.Bind("Fires", "Enabled", true,
                    "Allow impacts on forests and buildings to ignite fires.");
                FireIntensity = config.Bind("Fires", "Intensity", 1f,
                    new ConfigDescription("Overall ignition, spread and visual intensity. Performance budgets remain bounded.",
                        new AcceptableValueRange<float>(0.5f, 1.5f)));
                DemolishUnoccupiedBuildings = config.Bind("Fires", "DemolishUnoccupiedBuildings", true,
                    "Demolish a civilian building after its fire burns out unless it has a faction owner.");

                BuildingDamageEnabled = config.Bind("Buildings", "DamagedStateEnabled", true,
                    "Show a battered intermediate state before a lightweight building is ruined.");

                GarrisonsEnabled = config.Bind("Garrisons", "Enabled", true,
                    "Turn a few civilian buildings near owned airbases into defensive positions.");
                GarrisonsPerZone = config.Bind("Garrisons", "BuildingsPerZone", 3,
                    new ConfigDescription("Number of occupied civilian buildings per controlled zone.",
                        new AcceptableValueRange<int>(0, 6)));

                VerboseLogging = config.Bind("Debug", "VerboseLogging", false,
                    "Log bounded runtime diagnostics and individual feature events.");

                RemoveLegacyEntries(config);
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
            config.Save();
        }

        private static void RemoveLegacyEntries(ConfigFile config)
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
        }

        private static void BindAndRemove<T>(ConfigFile config, string section, string key, T defaultValue)
        {
            config.Bind(section, key, defaultValue);
            config.Remove(new ConfigDefinition(section, key));
        }

        private static float Clamp(float value, float min, float max) =>
            Math.Max(min, Math.Min(max, value));

        private static int ClampInt(int value, int min, int max) =>
            Math.Max(min, Math.Min(max, value));
    }
}
