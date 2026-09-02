using BepInEx.Configuration;

namespace BoscaliSummer.Features.FireAndDestruction.Configuration
{
    /// <summary>
    /// High-level fire, impact scorch, and aftermath settings. Detailed effect values
    /// remain derived and hard budgets remain bounded so old configs cannot disable the
    /// performance model.
    /// </summary>
    internal sealed class FireAndDestructionSettings
    {
        public readonly ConfigEntry<bool> FiresEnabled;
        public readonly ConfigEntry<float> FireIntensity;
        public readonly ConfigEntry<bool> DemolishUnoccupiedBuildings;
        public readonly ConfigEntry<bool> ImpactScorchEnabled;

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
        // Impact scorch marks are a local cosmetic. The pool ceiling and the salvo-safe
        // drain rate are fixed derived budgets, never user config.
        public int MaximumImpactScorches => 64;
        public int ImpactScorchQueue => 32;
        public int ImpactScorchesPerFrame => 2;

        public FireAndDestructionSettings(ConfigFile config)
        {
            // Ignition and demolition are decided by the host; scorch marks are drawn locally.
            FiresEnabled = config.Bind("Fires", "Enabled", true,
                "Allow impacts on forests and buildings to ignite fires. " +
                "Host-authoritative: on a server, only the host's value applies.");
            FireIntensity = config.Bind("Fires", "Intensity", 1f,
                new ConfigDescription(
                    "Overall ignition, spread and visual intensity. Performance budgets stay " +
                    "bounded at any value, so this cannot turn a long mission into a slideshow. " +
                    "Host-authoritative: on a server, only the host's value applies.",
                    new AcceptableValueRange<float>(0.5f, 1.5f)));
            DemolishUnoccupiedBuildings = config.Bind("Fires", "DemolishUnoccupiedBuildings", true,
                "Demolish a civilian building after its fire burns out, unless it has a faction " +
                "owner. Host-authoritative: on a server, only the host's value applies.");
            ImpactScorchEnabled = config.Bind("Buildings", "ImpactScorchEnabled", true,
                "Stamp a scorch mark on a building wall where an explosive hit lands. " +
                "Client-local cosmetic: nothing is tracked or sent to other players.");
        }
    }
}
