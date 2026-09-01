using BepInEx.Configuration;
using BoscaliSummer.Features.FireAndDestruction.Configuration;
using BoscaliSummer.Features.UrbanCombat.Configuration;
using BoscaliSummer.Infrastructure.Diagnostics;

namespace BoscaliSummer
{
    /// <summary>
    /// Composes module-owned settings while preserving the current internal accessors.
    /// The forwarding properties keep this behavior-only extraction small; they can be
    /// removed as the large managers are split into their final feature services.
    /// </summary>
    internal sealed class ModConfiguration
    {
        public FireAndDestructionSettings FireAndDestruction { get; }
        public UrbanCombatSettings UrbanCombat { get; }
        public DiagnosticSettings Diagnostics { get; }

        public ModConfiguration(ConfigFile config)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                FireAndDestruction = new FireAndDestructionSettings(config);
                UrbanCombat = new UrbanCombatSettings(config);
                Diagnostics = new DiagnosticSettings(config);
                LegacyConfigMigration.RemoveEntries(config);
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
            config.Save();
        }

        // Compatibility facade for the current managers. New feature code should use
        // its module settings object directly through FeatureContext.Settings.
        public ConfigEntry<bool> FiresEnabled => FireAndDestruction.FiresEnabled;
        public ConfigEntry<float> FireIntensity => FireAndDestruction.FireIntensity;
        public ConfigEntry<bool> DemolishUnoccupiedBuildings => FireAndDestruction.DemolishUnoccupiedBuildings;
        public ConfigEntry<bool> BuildingDamageEnabled => FireAndDestruction.BuildingDamageEnabled;
        public float BulletIgnitionChance => FireAndDestruction.BulletIgnitionChance;
        public float ExplosiveIgnitionChance => FireAndDestruction.ExplosiveIgnitionChance;
        public float VehicleExplosionIgnitionChance => FireAndDestruction.VehicleExplosionIgnitionChance;
        public int MaxActiveFires => FireAndDestruction.MaxActiveFires;
        public float FireLifetime => FireAndDestruction.FireLifetime;
        public float FireMergeRadius => FireAndDestruction.FireMergeRadius;
        public float FireCellCooldown => FireAndDestruction.FireCellCooldown;
        public float ScorchRadius => FireAndDestruction.ScorchRadius;
        public float ScorchRadiusScale => FireAndDestruction.ScorchRadiusScale;
        public float ForestCellSize => FireAndDestruction.ForestCellSize;
        public bool FireSpreadEnabled => FireAndDestruction.FireSpreadEnabled;
        public float FireSpreadInterval => FireAndDestruction.FireSpreadInterval;
        public float FireSpreadDistance => FireAndDestruction.FireSpreadDistance;
        public int FireSpreadGenerations => FireAndDestruction.FireSpreadGenerations;
        public int MaximumPersistentRuins => FireAndDestruction.MaximumPersistentRuins;
        public int MaximumRuinSmokeVisuals => FireAndDestruction.MaximumRuinSmokeVisuals;
        public int MaximumCollapseBursts => FireAndDestruction.MaximumCollapseBursts;
        public float HotRuinSeconds => FireAndDestruction.HotRuinSeconds;
        public float BuildingDamagedHitPoints => FireAndDestruction.BuildingDamagedHitPoints;
        public ConfigEntry<bool> GarrisonsEnabled => UrbanCombat.GarrisonsEnabled;
        public ConfigEntry<int> GarrisonsPerZone => UrbanCombat.GarrisonsPerZone;
        public int GarrisonsMinimum => UrbanCombat.GarrisonsMinimum;
        public int GarrisonsMaximum => UrbanCombat.GarrisonsMaximum;
        public string GarrisonDefinitionKey => UrbanCombat.GarrisonDefinitionKey;
        public ConfigEntry<bool> VerboseLogging => Diagnostics.VerboseLogging;
    }
}
