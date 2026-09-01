using BepInEx.Configuration;

namespace BoscaliSummer.Features.FireAndDestruction.Configuration
{
    /// <summary>
    /// High-level fire, building damage, and aftermath settings. Detailed effect values
    /// remain derived and hard budgets remain bounded so old configs cannot disable the
    /// performance model.
    /// </summary>
    internal sealed class FireAndDestructionSettings
    {
        public readonly ConfigEntry<bool> FiresEnabled;
        public readonly ConfigEntry<float> FireIntensity;
        public readonly ConfigEntry<bool> DemolishUnoccupiedBuildings;
        public readonly ConfigEntry<bool> BuildingDamageEnabled;

        public float BulletIgnitionChance => 0.0025f * (0.65f + 0.35f * FireIntensity.Value);
        public float ExplosiveIgnitionChance => 0.06f * (0.65f + 0.35f * FireIntensity.Value);
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
        public int MaximumPersistentRuins => 256;
        public int MaximumRuinSmokeVisuals => 24;
        public int MaximumCollapseBursts => 4;
        public float HotRuinSeconds => 120f;
        public float BuildingDamagedHitPoints => 58f;

        public FireAndDestructionSettings(ConfigFile config)
        {
            FiresEnabled = config.Bind("Fires", "Enabled", true,
                "Allow impacts on forests and buildings to ignite fires.");
            FireIntensity = config.Bind("Fires", "Intensity", 1f,
                new ConfigDescription(
                    "Overall ignition, spread and visual intensity. Performance budgets remain bounded.",
                    new AcceptableValueRange<float>(0.5f, 1.5f)));
            DemolishUnoccupiedBuildings = config.Bind("Fires", "DemolishUnoccupiedBuildings", true,
                "Demolish a civilian building after its fire burns out unless it has a faction owner.");
            BuildingDamageEnabled = config.Bind("Buildings", "DamagedStateEnabled", true,
                "Show a battered intermediate state before a lightweight building is ruined.");
        }
    }
}
